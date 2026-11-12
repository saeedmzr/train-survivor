#!/usr/bin/env python3
"""Train Survivor project MCP server.

A deliberately small Model Context Protocol server that exposes exactly two tools
(start_task, get_task_result) over the Hermes Runs API, plus one MCP Events webhook
event (task.completed). It is scoped to one project and one long-lived session.

Transport: Streamable HTTP (POST /mcp). No authentication: the only client is the
local OpenAI Secure MCP Tunnel client, which holds the control-plane credential.

Standard library only, so it runs under the system python.
"""

from __future__ import annotations

import base64
import hashlib
import hmac
import json
import os
import random
import re
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Dict, List, Optional

# --------------------------------------------------------------------------- config

def _default_root() -> str:
    """Repo root: env override, else the Windows checkout, else the Linux one."""
    env = os.environ.get("TS_PROJECT_ROOT")
    if env:
        return env
    candidates = ["C:/Users/saeed/projects/train-survivor",
                  "/home/saeed/projects/train-survivor"]
    for cand in candidates:
        if os.path.isdir(cand):
            return cand
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


PROJECT_ROOT = _default_root()
UNITY_ROOT = os.path.join(PROJECT_ROOT, "unity")
STATE_DIR = os.environ.get("TS_MCP_STATE_DIR",
                           os.path.join(os.path.expanduser("~"), ".train-survivor-mcp"))
SESSION_ID = "train-survivor-project-polish2"
# The gateway's default model can mismatch the configured provider (it did: DeepSeek
# rejects anthropic/claude-opus-4.6). A per-run model keeps the bridge working.
RUN_MODEL = os.environ.get("HERMES_RUN_MODEL", "deepseek-flash")
SERVER_NAME = "train-survivor-mcp"
SERVER_VERSION = "1.0.0"

HERMES_API = os.environ.get("HERMES_API_BASE", "http://127.0.0.1:8642").rstrip("/")
STATE_PATH = os.environ.get("TS_MCP_STATE", os.path.join(STATE_DIR, "state.json"))
KEY_PATH = os.environ.get("TS_MCP_KEY_FILE", os.path.join(STATE_DIR, "api.key"))
LISTEN_HOST = os.environ.get("TS_MCP_HOST", "127.0.0.1")
LISTEN_PORT = int(os.environ.get("TS_MCP_PORT", "8765"))

PROTOCOL_V2 = "2026-07-28"
SUPPORTED_VERSIONS = [PROTOCOL_V2, "2025-06-18", "2025-03-26"]
TERMINAL_STATUSES = {"completed", "failed", "cancelled", "interrupted"}

INSTRUCTIONS = (
    "You are the Train Survivor project worker. The Unity project root is "
    f"{PROJECT_ROOT} (Unity project at {UNITY_ROOT}). Work only inside that project. "
    "Before working, read PROJECT_HANDOFF.md at the project root for current status and next work. "
    "Read docs/ and AGENTS.md before changing behaviour. Report concrete results: what "
    "you ran, what you read, and the outcome. Do not modify anything unless the task "
    "explicitly says to."
)

# --------------------------------------------------------------------------- state

_lock = threading.RLock()
_state: Dict[str, Any] = {"runs": {}, "subs": {}}
_last_protocol = {"version": "2025-06-18"}


def _load_state() -> None:
    try:
        with open(STATE_PATH, "r", encoding="utf-8") as fh:
            data = json.load(fh)
        if isinstance(data, dict):
            _state["runs"] = data.get("runs") or {}
            _state["subs"] = data.get("subs") or {}
    except (OSError, ValueError):
        pass


def _save_state() -> None:
    with _lock:
        blob = json.dumps(_state, separators=(",", ":"), ensure_ascii=False)
    tmp = STATE_PATH + ".tmp"
    os.makedirs(os.path.dirname(STATE_PATH), mode=0o700, exist_ok=True)
    fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as fh:
        fh.write(blob)
    os.replace(tmp, STATE_PATH)


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


# --------------------------------------------------------------------------- Hermes API


def _api_key() -> str:
    key = os.environ.get("HERMES_API_KEY", "").strip()
    if key:
        return key
    try:
        with open(KEY_PATH, "r", encoding="utf-8") as fh:
            return fh.read().strip()
    except OSError:
        return ""


def _hermes(path: str, method: str = "GET", body: Optional[dict] = None) -> dict:
    key = _api_key()
    if not key:
        raise RuntimeError(
            f"Hermes API key missing: set HERMES_API_KEY or write it to {KEY_PATH}"
        )
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(HERMES_API + path, data=data, method=method)
    req.add_header("Authorization", f"Bearer {key}")
    req.add_header("Accept", "application/json")
    if data is not None:
        req.add_header("Content-Type", "application/json")
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            raw = resp.read()
    except urllib.error.HTTPError as exc:
        detail = exc.read().decode("utf-8", "replace")[:500]
        raise RuntimeError(f"Hermes API {method} {path} -> HTTP {exc.code}: {detail}") from exc
    except urllib.error.URLError as exc:
        raise RuntimeError(f"Hermes API unreachable at {HERMES_API}: {exc.reason}") from exc
    if not raw:
        return {}
    try:
        return json.loads(raw)
    except ValueError:
        return {"raw": raw.decode("utf-8", "replace")}


def _start_run(task: str) -> dict:
    payload = {"input": task, "session_id": SESSION_ID, "instructions": INSTRUCTIONS}
    if RUN_MODEL:
        payload["model"] = RUN_MODEL
    accepted = _hermes("/v1/runs", method="POST", body=payload)
    run_id = accepted.get("run_id")
    if not run_id:
        raise RuntimeError(f"Hermes API did not return a run_id: {accepted}")
    with _lock:
        _state["runs"][run_id] = {
            "run_id": run_id,
            "task": task[:2000],
            "session_id": SESSION_ID,
            "status": accepted.get("status", "started"),
            "created_at": _now_iso(),
            "finished_at": None,
            "output": "",
            "error": "",
            "event_sent": False,
        }
    _save_state()
    return {"run_id": run_id, "status": accepted.get("status", "started"), "session_id": SESSION_ID}


def _run_status(run_id: str) -> dict:
    result = _hermes(f"/v1/runs/{urllib.parse.quote(run_id, safe='')}")
    return _normalise_result(run_id, result)


def _normalise_result(run_id: str, result: dict) -> dict:
    status = str(result.get("status") or "unknown")
    output = result.get("output")
    if not isinstance(output, str):
        output = "" if output is None else json.dumps(output, ensure_ascii=False)
    error = result.get("error")
    if isinstance(error, dict):
        error = json.dumps(error, ensure_ascii=False)
    return {
        "run_id": run_id,
        "status": status,
        "session_id": result.get("session_id") or SESSION_ID,
        "output": output,
        "error": error or "",
        "usage": result.get("usage") or {},
        "model": result.get("model") or "",
        "updated_at": result.get("updated_at") or "",
    }


# --------------------------------------------------------------------------- webhook delivery


def _b64_secret_ok(secret: str) -> bool:
    if not secret.startswith("whsec_"):
        return False
    try:
        raw = base64.b64decode(secret[len("whsec_"):], validate=True)
    except Exception:
        return False
    return 24 <= len(raw) <= 64


def _sign(secret: str, msg_id: str, timestamp: str, body: bytes) -> str:
    key = base64.b64decode(secret[len("whsec_"):])
    signed = f"{msg_id}.{timestamp}.".encode() + body
    return "v1," + base64.b64encode(hmac.new(key, signed, hashlib.sha256).digest()).decode()


def _post_signed(url: str, secret: str, msg_id: str, body_obj: dict,
                 sub_id: Optional[str] = None) -> tuple[int, bytes]:
    body = json.dumps(body_obj, separators=(",", ":"), ensure_ascii=False).encode()
    ts = str(int(time.time()))
    headers = {
        "Content-Type": "application/json",
        "webhook-id": msg_id,
        "webhook-timestamp": ts,
        "webhook-signature": _sign(secret, msg_id, ts, body),
    }
    if sub_id:
        headers["X-MCP-Subscription-Id"] = sub_id
    req = urllib.request.Request(url, data=body, method="POST", headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            return resp.status, resp.read()[:2000]
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read()[:2000]
    except urllib.error.URLError as exc:
        return 0, str(exc.reason).encode()


def _verify_callback(url: str, secret: str, sub_id: str) -> None:
    """Challenge handshake required before any delivery starts."""
    nonce = uuid.uuid4().hex
    msg_id = f"msg_verification_{uuid.uuid4().hex[:12]}"
    status, body = _post_signed(
        url, secret, msg_id, {"type": "verification", "challenge": nonce}, sub_id
    )
    if not (200 <= status < 300):
        raise ValueError(("callback", f"callback verification failed: HTTP {status or 'unreachable'}"))
    try:
        echoed = json.loads(body.decode("utf-8", "replace")).get("challenge")
    except ValueError:
        echoed = None
    if not isinstance(echoed, str) or not hmac.compare_digest(echoed, nonce):
        raise ValueError(("callback", "callback verification failed: challenge not echoed"))


def _matches(sub: dict, run: dict) -> bool:
    args = sub.get("arguments") or {}
    if args.get("project") and args["project"] != "train-survivor":
        return False
    if args.get("run_id") and args["run_id"] != run["run_id"]:
        return False
    return True


def _deliver_completion(run: dict) -> None:
    with _lock:
        subs = [s for s in _state["subs"].values() if s.get("name") == "task.completed"]
    if not subs:
        return
    output = run.get("output") or ""
    summary = output.strip()
    if len(summary) > 1500:
        summary = summary[:1500].rstrip() + "\n...[truncated, call get_task_result for the full text]"
    payload = {
        "eventId": f"evt_{run['run_id']}",
        "name": "task.completed",
        "timestamp": run.get("finished_at") or _now_iso(),
        "data": {
            "project": "train-survivor",
            "run_id": run["run_id"],
            "session_id": run.get("session_id") or SESSION_ID,
            "status": run.get("status") or "unknown",
            "task": (run.get("task") or "")[:500],
            "summary": summary,
            "error": (run.get("error") or "")[:500],
            "output_chars": len(output),
        },
        "cursor": None,
    }
    delivered_any = False
    for sub in subs:
        if not _matches(sub, run):
            continue
        for attempt in range(3):
            status, _ = _post_signed(sub["url"], sub["secret"], payload["eventId"], payload, sub["id"])
            if 200 <= status < 300:
                delivered_any = True
                with _lock:
                    sub["last_delivery_at"] = _now_iso()
                    sub["last_status"] = status
                break
            time.sleep(1.5 * (attempt + 1))
        else:
            with _lock:
                sub["last_delivery_at"] = _now_iso()
                sub["last_status"] = status
    with _lock:
        run["event_sent"] = delivered_any
        run["delivery_attempted"] = True
    _save_state()


def _poller() -> None:
    while True:
        try:
            with _lock:
                pending = [
                    dict(r) for r in _state["runs"].values()
                    if r.get("status") not in TERMINAL_STATUSES and not r.get("delivery_attempted")
                ]
            for run in pending:
                try:
                    result = _run_status(run["run_id"])
                except Exception:
                    continue
                status = result["status"]
                if status not in TERMINAL_STATUSES:
                    with _lock:
                        item = _state["runs"].get(run["run_id"])
                        if item:
                            item["status"] = status
                    continue
                with _lock:
                    item = _state["runs"].get(run["run_id"])
                    if item:
                        item.update(
                            status=status,
                            output=(result["output"] or "")[:200000],
                            error=result["error"] or "",
                            finished_at=_now_iso(),
                        )
                _save_state()
                _deliver_completion(dict(_state["runs"].get(run["run_id"]) or run))
            _expire_subs()
        except Exception:
            pass
        time.sleep(2.0)


def _expire_subs() -> None:
    now = time.time()
    with _lock:
        expired = [sid for sid, s in _state["subs"].items() if s.get("expires_at", 0) < now]
        for sid in expired:
            _state["subs"].pop(sid, None)
    if expired:
        _save_state()


# --------------------------------------------------------------------------- MCP definitions

TOOLS: List[dict] = [
    {
        "name": "start_task",
        "title": "Start Train Survivor task",
        "description": (
            "Start a task on the Train Survivor Unity project. Runs in the persistent "
            "'train-survivor-project' Hermes session, so follow-up tasks see the earlier "
            "work. Returns a run_id immediately; the run continues in the background. "
            "Subscribe to task.completed to be woken when it finishes."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "task": {
                    "type": "string",
                    "description": (
                        "What to do, in the Train Survivor project. Follow-ups should "
                        "reference the earlier work (the same session remembers it)."
                    ),
                }
            },
            "required": ["task"],
            "additionalProperties": False,
        },
        "annotations": {
            "title": "Start Train Survivor task",
            "readOnlyHint": False,
            "destructiveHint": False,
            "idempotentHint": False,
            "openWorldHint": False,
        },
    },
    {
        "name": "get_task_result",
        "title": "Get Train Survivor task result",
        "description": (
            "Fetch the current status and full result of a task by run_id. Use it after "
            "task.completed fires, before deciding on a follow-up."
        ),
        "inputSchema": {
            "type": "object",
            "properties": {
                "run_id": {
                    "type": "string",
                    "description": "The run_id returned by start_task or task.completed.",
                }
            },
            "required": ["run_id"],
            "additionalProperties": False,
        },
        "annotations": {
            "title": "Get Train Survivor task result",
            "readOnlyHint": True,
            "destructiveHint": False,
            "idempotentHint": True,
            "openWorldHint": False,
        },
    },
]

EVENTS: List[dict] = [
    {
        "name": "task.completed",
        "description": (
            "Fires when a Train Survivor task started with start_task reaches a terminal "
            "state (completed, failed, cancelled or interrupted). The payload carries the "
            "run_id and a summary; call get_task_result with that run_id for the full text."
        ),
        "delivery": ["webhook"],
        "inputSchema": {
            "type": "object",
            "properties": {
                "project": {
                    "type": "string",
                    "const": "train-survivor",
                    "description": "Project scope. Only 'train-survivor' is served.",
                    "default": "train-survivor",
                },
                "run_id": {
                    "type": "string",
                    "description": "Optional: only deliver the completion of this run.",
                },
            },
            "additionalProperties": False,
        },
        "payloadSchema": {
            "type": "object",
            "properties": {
                "project": {"type": "string"},
                "run_id": {"type": "string"},
                "session_id": {"type": "string"},
                "status": {"type": "string"},
                "task": {"type": "string"},
                "summary": {"type": "string"},
                "error": {"type": "string"},
                "output_chars": {"type": "integer"},
            },
            "required": ["project", "run_id", "session_id", "status", "summary", "output_chars"],
            "additionalProperties": False,
        },
    }
]


def _server_info() -> dict:
    return {"name": SERVER_NAME, "version": SERVER_VERSION}


def _capabilities() -> dict:
    return {"tools": {"listChanged": False}, "events": {}}


def _result(payload: dict) -> dict:
    """MCP 2.0 results carry resultType; older clients ignore the extra field."""
    if _last_protocol["version"] == PROTOCOL_V2:
        return {"resultType": "complete", **payload}
    return payload


def _tool_result(structured: dict, is_error: bool = False,
                 images: Optional[List[dict]] = None,
                 text_view: Optional[dict] = None) -> dict:
    shown = text_view if text_view is not None else structured
    content: List[dict] = [{"type": "text", "text": json.dumps(shown, indent=2, ensure_ascii=False)}]
    if images:
        content.extend(images)
    return _result({
        "content": content,
        "structuredContent": structured,
        "isError": is_error,
    })


# --------------------------------------------------------------------- image evidence
# A completed result may reference local evidence with a literal "MCP_IMAGE: <path>"
# marker. The server reads the file itself and appends a native MCP image block, so the
# model never has to copy base64. Strictly limited to the evidence directory.

EVIDENCE_ROOT = os.environ.get("TS_MCP_EVIDENCE", os.path.join(os.path.expanduser("~"), "train-survivor-evidence"))
MAX_IMAGE_BYTES = 256 * 1024
MAX_IMAGES = 2
_IMAGE_MIME = {".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg",
               ".webp": "image/webp"}
_AUDIO_MIME = {".wav": "audio/wav", ".mp3": "audio/mpeg", ".ogg": "audio/ogg"}
MAX_AUDIO_BYTES = 256 * 1024
MAX_AUDIO = 1
_VIDEO_MIME = {".mp4": "video/mp4"}
MAX_VIDEO_BYTES = 1536 * 1024
MAX_VIDEO = 1


def _file_payload(raw: str, mimes: dict, limit: int) -> Optional[dict]:
    """Validated evidence file -> exact bytes, base64 and metadata."""
    path = (raw or "").strip().strip("<>'\"")
    if not path or not os.path.isabs(path):
        return None
    try:
        real = os.path.realpath(path)
    except OSError:
        return None
    root = os.path.realpath(EVIDENCE_ROOT)
    if not real.startswith(root + os.sep):
        return None
    mime = mimes.get(os.path.splitext(real)[1].lower())
    if mime is None or not os.path.isfile(real):
        return None
    try:
        size = os.path.getsize(real)
        if size <= 0 or size > limit:
            return None
        with open(real, "rb") as fh:
            data = fh.read(limit + 1)
    except OSError:
        return None
    if len(data) > limit:
        return None
    return {
        "name": os.path.basename(real),
        "mime_type": mime,
        "data_base64": base64.b64encode(data).decode("ascii"),
        "sha256": hashlib.sha256(data).hexdigest(),
        "size_bytes": len(data),
    }


def _evidence_attachments(text: str) -> List[dict]:
    blocks: List[dict] = []
    seen = set()
    for pattern, mimes, limit, cap, kind in (
            (r"MCP_IMAGE:\s*(<[^>\n]+>|[^\s'\"\n]+)", _IMAGE_MIME, MAX_IMAGE_BYTES, MAX_IMAGES, "image"),
            (r"MCP_AUDIO:\s*(<[^>\n]+>|[^\s'\"\n]+)", _AUDIO_MIME, MAX_AUDIO_BYTES, MAX_AUDIO, "audio"),
            (r"MCP_VIDEO:\s*(<[^>\n]+>|[^\s'\"\n]+)", _VIDEO_MIME, MAX_VIDEO_BYTES, MAX_VIDEO, "video")):
        found = 0
        for match in re.finditer(pattern, text or ""):
            if found >= cap:
                break
            raw = match.group(1)
            key = os.path.realpath(raw.strip().strip("<>'\""))
            if key in seen:
                continue
            payload = _file_payload(raw, mimes, limit)
            if payload is not None:
                payload["kind"] = kind
                blocks.append(payload)
                seen.add(key)
                found += 1
    return blocks


def _image_block(att: dict) -> dict:
    return {"type": "image", "mimeType": att["mime_type"], "data": att["data_base64"]}


def _mcp_error(code: int, message: str, data: Optional[dict] = None) -> dict:
    err = {"code": code, "message": message}
    if data is not None:
        err["data"] = data
    return err


# --------------------------------------------------------------------------- JSON-RPC


def _handle_call(name: str, args: dict) -> dict:
    if name == "start_task":
        task = args.get("task")
        if not isinstance(task, str) or not task.strip():
            return _tool_result({"error": "task is required"}, is_error=True)
        try:
            return _tool_result(_start_run(task.strip()))
        except Exception as exc:
            return _tool_result({"error": str(exc)}, is_error=True)
    if name == "get_task_result":
        run_id = args.get("run_id")
        if not isinstance(run_id, str) or not run_id.strip():
            return _tool_result({"error": "run_id is required"}, is_error=True)
        try:
            payload = _run_status(run_id.strip())
            text = ""
            if isinstance(payload, dict):
                text = str(payload.get("output") or "")
            attachments = _evidence_attachments(text)
            text_view = payload
            if attachments:
                payload = dict(payload)
                payload["attachments"] = attachments
                text_view = dict(payload)
                text_view["attachments"] = [
                    {k: a[k] for k in ("name", "mime_type", "sha256", "size_bytes", "kind")}
                    for a in attachments
                ]
            return _tool_result(payload,
                               images=[_image_block(a) for a in attachments
                                       if a.get("kind") == "image"],
                               text_view=text_view)
        except Exception as exc:
            return _tool_result({"error": str(exc)}, is_error=True)
    return _tool_result({"error": f"unknown tool: {name}"}, is_error=True)


def _handle_subscribe(params: dict) -> dict:
    name = params.get("name")
    if name != "task.completed":
        raise ValueError(("not_found", f"unknown event: {name!r}"))
    arguments = params.get("arguments") or {}
    if not isinstance(arguments, dict):
        raise ValueError(("invalid", "arguments must be an object"))
    project = arguments.get("project")
    if project not in (None, "", "train-survivor"):
        raise ValueError(("forbidden", "only the train-survivor project is served"))
    delivery = params.get("delivery") or {}
    if delivery.get("mode") != "webhook":
        raise ValueError(("unsupported", "only webhook delivery is supported"))
    url = delivery.get("url") or ""
    secret = delivery.get("secret") or ""
    loopback_dev = (
        os.environ.get("TS_MCP_ALLOW_HTTP") == "1"
        and url.startswith("http://127.0.0.1:")
    )
    if not (url.startswith("https://") or loopback_dev):
        raise ValueError(("invalid", "delivery.url must be https"))
    if not _b64_secret_ok(secret):
        raise ValueError(("invalid", "delivery.secret must be whsec_ + base64 of 24-64 bytes"))

    ttl_ms = params.get("ttlMs")
    ttl = 3600.0
    if isinstance(ttl_ms, (int, float)) and ttl_ms > 0:
        ttl = min(float(ttl_ms) / 1000.0, 21600.0)
    now = time.time()
    sub_id = "sub_" + uuid.uuid4().hex[:16]
    with _lock:
        for sid, existing in _state["subs"].items():
            if (existing.get("name") == name and existing.get("url") == url
                    and (existing.get("arguments") or {}) == arguments):
                sub_id = sid
                break
    # Handshake before any delivery. Cached per (url, name, arguments).
    _verify_callback(url, secret, sub_id)
    sub = {
        "id": sub_id,
        "name": name,
        "arguments": arguments,
        "url": url,
        "secret": secret,
        "created_at": _now_iso(),
        "expires_at": now + ttl,
        "cursor": params.get("cursor"),
    }
    with _lock:
        _state["subs"][sub_id] = sub
    _save_state()
    return _result({
        "id": sub_id,
        "refreshBefore": datetime.fromtimestamp(now + ttl, tz=timezone.utc)
        .isoformat(timespec="seconds").replace("+00:00", "Z"),
        "cursor": None,
        "truncated": False,
    })


def _handle_unsubscribe(params: dict) -> dict:
    name = params.get("name")
    arguments = params.get("arguments") or {}
    url = (params.get("delivery") or {}).get("url")
    removed = 0
    with _lock:
        for sid in list(_state["subs"]):
            sub = _state["subs"][sid]
            if sub.get("name") != name:
                continue
            if url and sub.get("url") != url:
                continue
            if arguments and (sub.get("arguments") or {}) != arguments:
                continue
            _state["subs"].pop(sid, None)
            removed += 1
    if removed:
        _save_state()
    return _result({})


def _dispatch(method: str, params: dict) -> dict:
    if method == "initialize":
        requested = params.get("protocolVersion") or "2025-06-18"
        version = requested if requested in SUPPORTED_VERSIONS else "2025-06-18"
        _last_protocol["version"] = version
        return {
            "protocolVersion": version,
            "capabilities": _capabilities(),
            "serverInfo": _server_info(),
            "instructions": (
                "Train Survivor project tools. start_task runs work in the persistent project "
                "session; subscribe to task.completed to be woken with the run_id, then call "
                "get_task_result and start a follow-up task in the same session."
            ),
        }
    if method == "server/discover":
        _last_protocol["version"] = PROTOCOL_V2
        return {
            "resultType": "complete",
            "supportedVersions": [PROTOCOL_V2],
            "capabilities": _capabilities(),
            "serverInfo": _server_info(),
        }
    if method == "tools/list":
        return _result({"tools": TOOLS})
    if method == "tools/call":
        return _handle_call(params.get("name", ""), params.get("arguments") or {})
    if method == "events/list":
        return _result({"events": EVENTS})
    if method == "events/subscribe":
        return _handle_subscribe(params)
    if method == "events/unsubscribe":
        return _handle_unsubscribe(params)
    if method == "ping":
        return _result({})
    raise KeyError(method)


# --------------------------------------------------------------------------- HTTP transport


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "train-survivor-mcp/1.0"

    def log_message(self, fmt: str, *args: Any) -> None:  # keep journald quiet
        if os.environ.get("TS_MCP_DEBUG"):
            super().log_message(fmt, *args)

    # -- helpers ------------------------------------------------------------
    def _send(self, code: int, body: bytes, ctype: str = "application/json") -> None:
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def _rpc(self, msg: dict) -> Optional[dict]:
        mid = msg.get("id")
        method = msg.get("method")
        params = msg.get("params") or {}
        if method is None:
            return None
        try:
            result = _dispatch(method, params if isinstance(params, dict) else {})
            return {"jsonrpc": "2.0", "id": mid, "result": result}
        except KeyError:
            return {"jsonrpc": "2.0", "id": mid,
                    "error": _mcp_error(-32601, f"Method not found: {method}")}
        except ValueError as exc:
            kind, text = exc.args[0] if exc.args and isinstance(exc.args[0], tuple) else ("invalid", str(exc))
            code = {"not_found": -32023, "forbidden": -32024,
                    "unsupported": -32026, "callback": -32027}.get(kind, -32602)
            return {"jsonrpc": "2.0", "id": mid, "error": _mcp_error(code, text)}
        except Exception as exc:  # never leak a 500 to the client
            return {"jsonrpc": "2.0", "id": mid, "error": _mcp_error(-32603, str(exc))}

    # -- routes -------------------------------------------------------------
    def do_GET(self) -> None:  # noqa: N802
        path = self.path.split("?")[0]
        if path in ("/healthz", "/"):
            self._send(200, json.dumps({"ok": True, "server": SERVER_NAME,
                                        "project": PROJECT_ROOT}).encode())
        elif path == "/mcp":
            # Streamable HTTP: POST only; no server-initiated SSE stream.
            self.send_response(405)
            self.send_header("Allow", "POST")
            self.send_header("Content-Length", "0")
            self.end_headers()
        elif path.startswith("/.well-known/oauth"):
            # This server has no OAuth. An empty 404 is the unambiguous "no metadata
            # here"; a JSON body makes tunnel-client parse it as a broken metadata
            # document and log "protected resource metadata missing resource".
            self._send(404, b"", "text/plain")
        else:
            self._send(404, b'{"error":"not found"}')

    do_HEAD = do_GET

    def do_POST(self) -> None:  # noqa: N802
        if self.path.split("?")[0] != "/mcp":
            self._send(404, b'{"error":"not found"}')
            return
        try:
            length = int(self.headers.get("Content-Length") or 0)
            if length <= 0 or length > 2 * 1024 * 1024:
                self._send(400, b'{"error":"bad length"}')
                return
            raw = self.rfile.read(length)
            msg = json.loads(raw)
        except Exception:
            self._send(400, b'{"error":"invalid json"}')
            return
        if isinstance(msg, list):
            out = [r for r in (self._rpc(m) for m in msg if isinstance(m, dict)) if r]
            if not out:
                self._send(202, b"")
                return
            self._send(200, json.dumps(out).encode())
            return
        if not isinstance(msg, dict):
            self._send(400, b'{"error":"invalid request"}')
            return
        reply = self._rpc(msg)
        if reply is None:
            self._send(202, b"")
            return
        self._send(200, json.dumps(reply).encode())


class _QuietServer(ThreadingHTTPServer):
    """Clients (tunnel-client, curl probes) drop keep-alive sockets constantly;
    socketserver prints a full traceback for each reset unless we swallow it."""

    daemon_threads = True
    allow_reuse_address = True

    def handle_error(self, request, client_address):
        exc = sys.exc_info()[1]
        if isinstance(exc, (ConnectionResetError, ConnectionAbortedError, BrokenPipeError)):
            return
        super().handle_error(request, client_address)


def main() -> None:
    os.makedirs(os.path.dirname(STATE_PATH), mode=0o700, exist_ok=True)
    _load_state()
    threading.Thread(target=_poller, name="run-poller", daemon=True).start()
    server = _QuietServer((LISTEN_HOST, LISTEN_PORT), Handler)
    print(f"{SERVER_NAME} {SERVER_VERSION} on http://{LISTEN_HOST}:{LISTEN_PORT}/mcp "
          f"(project {PROJECT_ROOT}, session {SESSION_ID})", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
