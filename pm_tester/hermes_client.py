"""Thin client for the Hermes OpenAI-compatible API server.

Two transports are supported:

* ``/v1/chat/completions`` — plain chat, message history managed here.
* ``/v1/responses`` — preferred when available, because the server can keep the
  conversation by ``previous_response_id`` instead of us resending history.

Nothing in this module is Unity-aware: it is only the pipe between the PM agent
and the developer (Hermes).
"""
from __future__ import annotations

import json
import os
import time
import uuid
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional

import requests


class HermesUnavailable(RuntimeError):
    """The developer endpoint is not answering (gateway down, wrong port, bad key)."""


@dataclass
class HermesClient:
    base_url: str = "http://127.0.0.1:8642/v1"
    model: str = "hermes-agent"
    api_key: Optional[str] = None
    timeout_s: float = 900
    prefer_responses: bool = True
    session_key: str = field(default_factory=lambda: f"pm-tester-{uuid.uuid4().hex[:12]}")

    _history: List[Dict[str, str]] = field(default_factory=list, repr=False)
    _previous_response_id: Optional[str] = field(default=None, repr=False)
    _responses_supported: Optional[bool] = field(default=None, repr=False)

    # ------------------------------------------------------------------ plumbing

    @property
    def _headers(self) -> Dict[str, str]:
        headers = {"Content-Type": "application/json"}
        if self.api_key:
            headers["Authorization"] = f"Bearer {self.api_key}"
        # Keeps one logical conversation on the server side, so /v1/responses and
        # /v1/runs resume the same thread.
        headers["X-Hermes-Session-Key"] = self.session_key
        return headers

    def _post(self, path: str, payload: Dict[str, Any]) -> Dict[str, Any]:
        url = f"{self.base_url.rstrip('/')}/{path.lstrip('/')}"
        try:
            resp = requests.post(url, headers=self._headers, json=payload, timeout=self.timeout_s)
        except requests.RequestException as exc:  # connection refused, DNS, timeout
            raise HermesUnavailable(f"cannot reach {url}: {exc}") from exc

        if resp.status_code == 401:
            raise HermesUnavailable(
                f"{url} rejected the API key (401). Check API_SERVER_KEY in ~/.hermes/.env "
                "and HERMES_API_KEY in pm_tester/.env."
            )
        if resp.status_code >= 400:
            raise HermesUnavailable(f"{url} -> HTTP {resp.status_code}: {resp.text[:400]}")
        return resp.json()

    # ------------------------------------------------------------------ transports

    def chat(self, message: str, system: Optional[str] = None, remember: bool = True) -> str:
        """Send one user message, get the assistant's text back."""
        if self.prefer_responses and self._responses_supported is not False:
            try:
                return self._chat_responses(message, system, remember)
            except HermesUnavailable:
                if self._responses_supported:
                    raise            # responses worked before; this is a real failure
                self._responses_supported = False   # fall through to chat/completions

        return self._chat_completions(message, system, remember)

    def _chat_completions(self, message: str, system: Optional[str], remember: bool) -> str:
        messages: List[Dict[str, str]] = []
        if system:
            messages.append({"role": "system", "content": system})
        messages.extend(self._history)
        messages.append({"role": "user", "content": message})

        body = self._post("chat/completions", {"model": self.model, "messages": messages})
        text = (body.get("choices") or [{}])[0].get("message", {}).get("content", "")
        if remember:
            self._history.append({"role": "user", "content": message})
            self._history.append({"role": "assistant", "content": text})
        return text

    def _chat_responses(self, message: str, system: Optional[str], remember: bool) -> str:
        payload: Dict[str, Any] = {"model": self.model, "input": message}
        if system:
            payload["instructions"] = system
        if self._previous_response_id:
            payload["previous_response_id"] = self._previous_response_id

        body = self._post("responses", payload)
        self._responses_supported = True
        self._previous_response_id = body.get("id")
        text = _extract_responses_text(body)
        if remember:
            self._history.append({"role": "user", "content": message})
            self._history.append({"role": "assistant", "content": text})
        return text

    # ------------------------------------------------------------------ lifecycle

    def reset(self) -> None:
        """Start a fresh conversation (new session key, no history)."""
        self._history.clear()
        self._previous_response_id = None
        self.session_key = f"pm-tester-{uuid.uuid4().hex[:12]}"

    def health(self) -> Dict[str, Any]:
        """Cheap reachability probe used by loop.py before it starts."""
        started = time.time()
        try:
            resp = requests.get(
                f"{self.base_url.rstrip('/')}/models",
                headers=self._headers,
                timeout=10,
            )
            return {
                "reachable": resp.status_code < 500,
                "status": resp.status_code,
                "latency_ms": int((time.time() - started) * 1000),
                "body": resp.text[:300],
            }
        except requests.RequestException as exc:
            return {"reachable": False, "error": str(exc), "status": None}


def _extract_responses_text(body: Dict[str, Any]) -> str:
    """Pull assistant text out of a /v1/responses payload."""
    if isinstance(body.get("output_text"), str):
        return body["output_text"]
    chunks: List[str] = []
    for item in body.get("output") or []:
        for part in item.get("content") or []:
            if part.get("type") in ("output_text", "text") and part.get("text"):
                chunks.append(part["text"])
    return "\n".join(chunks).strip()


def client_from_config(cfg: Dict[str, Any]) -> HermesClient:
    hermes = (cfg or {}).get("hermes", {})
    env_name = hermes.get("api_key_env", "HERMES_API_KEY")
    return HermesClient(
        base_url=hermes.get("base_url", "http://127.0.0.1:8642/v1"),
        model=hermes.get("model", "hermes-agent"),
        api_key=os.environ.get(env_name),
        timeout_s=float(hermes.get("timeout_s", 900)),
        prefer_responses=bool(hermes.get("prefer_responses", True)),
    )


if __name__ == "__main__":  # python -m pm_tester.hermes_client
    import sys

    cfg_path = sys.argv[sys.argv.index("--config") + 1] if "--config" in sys.argv else "config.yaml"
    try:
        import yaml
        cfg = yaml.safe_load(open(cfg_path, encoding="utf-8"))
    except Exception:
        cfg = {}

    c = client_from_config(cfg)
    print(json.dumps(c.health(), indent=2))
    if "--ask" in sys.argv:
        prompt = sys.argv[sys.argv.index("--ask") + 1]
        print("\n--- reply ---\n" + c.chat(prompt))
