"""Unity test driver for the PM/Tester.

Two back-ends behind one interface:

1. **MCP** — a Unity MCP server (Singtaa/UnityMCP by default) that drives the
   editor: Play Mode, console logs, screenshots, test runs.
2. **adb fallback** — when no editor is open, drive the built APK on the
   emulator: install, launch, read logcat, screencap. Same contract, so the
   loop keeps working without a GUI session.

Every method returns a dict shaped {ok, detail, evidence: {...}} so the caller
can attach it to a run folder without knowing which backend ran.
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import time
from dataclasses import dataclass
from typing import Any, Dict, List, Optional

import requests


# ---------------------------------------------------------------------------- MCP


class MCPError(RuntimeError):
    pass


@dataclass
class MCPClient:
    """Minimal JSON-RPC client for an HTTP MCP server."""

    base_url: str = "http://127.0.0.1:5173"
    token: Optional[str] = None
    timeout_s: float = 60
    _id: int = 0

    def _headers(self) -> Dict[str, str]:
        headers = {"Content-Type": "application/json", "Accept": "application/json"}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        return headers

    def call(self, method: str, params: Optional[Dict[str, Any]] = None) -> Any:
        self._id += 1
        payload = {"jsonrpc": "2.0", "id": self._id, "method": method, "params": params or {}}
        try:
            resp = requests.post(self.base_url, headers=self._headers(), json=payload,
                                 timeout=self.timeout_s)
        except requests.RequestException as exc:
            raise MCPError(f"cannot reach MCP server at {self.base_url}: {exc}") from exc

        if resp.status_code >= 400:
            raise MCPError(f"MCP HTTP {resp.status_code}: {resp.text[:300]}")
        body = resp.json()
        if "error" in body:
            raise MCPError(f"MCP error: {body['error']}")
        return body.get("result")

    def initialize(self) -> Dict[str, Any]:
        return self.call("initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "train-survivor-pm-tester", "version": "1.0"},
        })

    def list_tools(self) -> List[Dict[str, Any]]:
        return (self.call("tools/list") or {}).get("tools", [])

    def call_tool(self, name: str, arguments: Optional[Dict[str, Any]] = None) -> Any:
        return self.call("tools/call", {"name": name, "arguments": arguments or {}})


# ---------------------------------------------------------------------------- tester


class UnityTester:
    def __init__(self, cfg: Dict[str, Any], run_dir: str):
        self.cfg = cfg or {}
        mcp = self.cfg.get("unity_mcp", {})
        self.tools = mcp.get("tools", {}) or {}
        self.mcp = MCPClient(
            base_url=mcp.get("base_url", "http://127.0.0.1:5173"),
            token=os.environ.get(mcp.get("auth_token_env", "UNITY_MCP_TOKEN")),
            timeout_s=float(mcp.get("request_timeout_s", 60)),
        )
        self.fallback_cfg = (mcp.get("fallback") or {})
        self.run_dir = run_dir
        os.makedirs(run_dir, exist_ok=True)

    # ------------------------------------------------------------------ probing

    def probe(self) -> Dict[str, Any]:
        """Is the MCP server alive, and what tools does it expose?"""
        try:
            info = self.mcp.initialize()
            tools = [t.get("name") for t in self.mcp.list_tools()]
            return {"ok": True, "backend": "mcp", "server": info, "tools": tools}
        except MCPError as exc:
            return {"ok": False, "backend": "none", "detail": str(exc),
                    "fallback_available": bool(self.fallback_cfg.get("enabled"))}

    def _tool(self, key: str, default: str) -> str:
        return self.tools.get(key, default)

    # ------------------------------------------------------------------ MCP actions

    def load_scene(self, path: Optional[str] = None) -> Dict[str, Any]:
        """Open the bootstrap scene: Play Mode otherwise runs whatever was last open."""
        scene = path or self.cfg.get("unity_mcp", {}).get("scene")
        if not scene:
            return {"ok": True, "backend": "mcp", "detail": "no scene configured"}
        return self._do_mcp("scene_load",
                            {self._tool("scene_load", "unity_scene_load"): {"path": scene}},
                            waits=2.0)

    def enter_play_mode(self, enter: bool = True) -> Dict[str, Any]:
        key = "play_mode_enter" if enter else "play_mode_exit"
        return self._do_mcp("play_mode",
                            {self._tool(key, "unity_playmode_enter"): {}},
                            waits=3.0)

    def exit_play_mode(self) -> Dict[str, Any]:
        return self.enter_play_mode(False)

    def console_logs(self, max_entries: int = 200) -> Dict[str, Any]:
        return self._do_mcp("console_logs",
                            {self._tool("console_logs", "unity_console_logs"):
                             {"maxEntries": max_entries}})

    def screenshot(self, name: str = "unity") -> Dict[str, Any]:
        """Capture the Game view. The server returns image content, which we persist."""
        path = os.path.join(self.run_dir, f"{name}.png")
        tool = self._tool("screenshot", "unity_capture_game_view")
        last = "no image in tool result"
        try:
            # The first capture after entering Play Mode can hit the 8 s bridge
            # timeout while the runtime builds the world; retry rather than fail.
            for attempt, pause in enumerate((0.0, 3.0, 5.0), start=1):
                if pause:
                    time.sleep(pause)
                result = self.mcp.call_tool(tool, {})
                saved = _save_image(result, path)
                if saved:
                    return {"ok": True, "backend": "mcp",
                            "detail": f"captured game view (attempt {attempt})",
                            "evidence": {"screenshot": saved}}
                last = _text(result)[:200] or last
            return {"ok": False, "backend": "mcp", "detail": last}
        except MCPError as exc:
            if not self.fallback_cfg.get("enabled"):
                return {"ok": False, "backend": "mcp", "detail": f"screenshot failed: {exc}"}
            return self._fallback("screenshot")

    def run_tests(self, mode: str = "EditMode", test_name: Optional[str] = None) -> Dict[str, Any]:
        args: Dict[str, Any] = {"testMode": mode}
        if test_name:
            args["testFilter"] = test_name
        args["resultsPath"] = os.path.join(self.run_dir, "test-results.xml")
        return self._do_mcp("tests", {self._tool("run_tests", "unity_test_run_sync"): args},
                            waits=10.0)

    def _do_mcp(self, label: str, calls: Dict[str, Dict[str, Any]], waits: float = 0.0) -> Dict[str, Any]:
        try:
            name, arguments = next(iter(calls.items()))
            started = time.time()
            result = self.mcp.call_tool(name, arguments)
            failed = bool(isinstance(result, dict) and result.get("isError"))
            if waits:
                time.sleep(min(waits, 2.0))     # let the editor settle
            if label == "play_mode" and not failed:
                time.sleep(3.0)                 # world is built at runtime
            return {"ok": not failed, "backend": "mcp", "detail": _text(result),
                    "elapsed_s": round(time.time() - started, 2)}
        except (MCPError, StopIteration) as exc:
            if not self.fallback_cfg.get("enabled"):
                return {"ok": False, "backend": "mcp", "detail": f"{label} failed: {exc}"}
            return self._fallback(label)

    # ------------------------------------------------------------------ adb fallback

    def _adb(self, *args: str, timeout: int = 90) -> subprocess.CompletedProcess:
        adb = self.fallback_cfg.get("adb", "adb")
        return subprocess.run([adb, *args], capture_output=True, text=True, timeout=timeout)

    def _fallback(self, label: str) -> Dict[str, Any]:
        """No editor: drive the APK on the emulator/device instead."""
        notes = [f"mcp unavailable, using adb for {label}"]
        try:
            state = self._adb("get-state", timeout=15)
            if "device" not in (state.stdout or ""):
                self._adb("reconnect", timeout=20)
                time.sleep(2)
                state = self._adb("get-state", timeout=15)
            if "device" not in (state.stdout or ""):
                return {"ok": False, "backend": "adb", "detail": "; ".join(notes + ["no device"])}

            pkg = self.fallback_cfg.get("package", "com.trainsurvivor.game")
            act = self.fallback_cfg.get("activity")
            apk = os.path.abspath(self.fallback_cfg.get("apk", ""))

            if label == "play_mode" and os.path.exists(apk):
                self._adb("install", "-r", apk, timeout=240)
                notes.append(f"installed {os.path.basename(apk)}")

            self._adb("shell", "am", "force-stop", pkg, timeout=30)
            self._adb("logcat", "-c", timeout=20)
            self._adb("shell", "am", "start", "-n", f"{pkg}/{act}" if act else pkg, timeout=30)
            time.sleep(float(self.fallback_cfg.get("play_seconds", 20)))

            if label == "console_logs":
                logs = self._adb("logcat", "-d", "-s", "Unity:V", timeout=60).stdout or ""
                keep = [l for l in logs.splitlines()
                        if any(k in l for k in ("Exception", "error", "[", "CRASH"))]
                path = os.path.join(self.run_dir, "logcat.txt")
                open(path, "w", encoding="utf-8").write("\n".join(keep))
                return {"ok": True, "backend": "adb", "detail": f"{len(keep)} relevant lines",
                        "evidence": {"logcat": path}, "notes": notes}

            if label == "screenshot":
                path = os.path.join(self.run_dir, "screen.png")
                self._adb("shell", "screencap", "-p", "/sdcard/pm.png", timeout=60)
                self._adb("pull", "/sdcard/pm.png", path, timeout=90)
                return {"ok": os.path.exists(path), "backend": "adb",
                        "detail": "captured via adb", "evidence": {"screenshot": path}, "notes": notes}

            return {"ok": True, "backend": "adb", "detail": "launched via adb", "notes": notes}
        except Exception as exc:                      # noqa: BLE001 - report, never crash the loop
            return {"ok": False, "backend": "adb", "detail": f"{exc}"}

    # ------------------------------------------------------------------ smoke test

    def smoke_test(self) -> Dict[str, Any]:
        """'Game launches without errors' — the check the CEO asked to start with."""
        probe = self.probe()
        evidence: Dict[str, Any] = {"backend": probe.get("backend")}
        problems: List[str] = []

        self.enter_play_mode(False)             # a scene can only load outside play mode
        time.sleep(1.5)
        scene = self.load_scene()
        evidence["scene"] = scene.get("detail", "")
        if not scene.get("ok"):
            problems.append(f"scene load failed: {scene.get('detail')}")

        play = self.enter_play_mode(True)
        evidence["play_mode"] = play.get("detail", "")
        if not play.get("ok"):
            problems.append(f"play mode failed: {play.get('detail')}")

        logs = self.console_logs()
        evidence.update(logs.get("evidence") or {})
        log_text = (logs.get("detail") or "") + json.dumps(logs.get("evidence") or {})
        if logs.get("evidence", {}).get("logcat"):
            try:
                log_text += open(logs["evidence"]["logcat"], encoding="utf-8", errors="ignore").read()
            except OSError:
                pass

        # The InputSystem device-change exception is known and unrelated to gameplay.
        ignored = ("InputUpdateType.None",)
        fatal = [line for line in log_text.splitlines()
                 if any(k in line for k in ("Exception", "NullReference", "error CS", "CRASH"))
                 and not any(i in line for i in ignored)]
        evidence["fatal_errors"] = len(fatal)
        if fatal:
            problems.append(f"{len(fatal)} fatal log lines")
            path = os.path.join(self.run_dir, "fatal_errors.txt")
            open(path, "w", encoding="utf-8").write("\n".join(fatal[:200]))
            evidence["fatal_log"] = path

        shot = self.screenshot("smoke")
        evidence.update(shot.get("evidence") or {})
        if not shot.get("ok"):
            problems.append(f"screenshot failed: {shot.get('detail')}")

        return {
            "ok": not problems,
            "detail": "game launched without fatal errors" if not problems else "; ".join(problems),
            "evidence": evidence,
        }


def _save_image(result: Any, path: str) -> Optional[str]:
    """MCP image content arrives as {type: image, data: <base64>}."""
    import base64
    for item in (result or {}).get("content", []) if isinstance(result, dict) else []:
        if isinstance(item, dict) and item.get("type") == "image" and item.get("data"):
            try:
                with open(path, "wb") as fh:
                    fh.write(base64.b64decode(item["data"]))
                return path
            except (ValueError, OSError):
                return None
    return None


def _text(result: Any) -> str:
    """MCP tool results arrive as {content:[{type:text,text:...}]} or plain JSON."""
    if result is None:
        return ""
    if isinstance(result, dict) and "content" in result:
        parts = [c.get("text", "") for c in result["content"] if isinstance(c, dict)]
        return "\n".join(p for p in parts if p)[:4000]
    return json.dumps(result)[:4000]
