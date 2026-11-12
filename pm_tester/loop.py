"""Orchestration state machine: CEO task -> PM review -> Unity test -> pass/retry/block.

    python -m pm_tester.loop --task "verify the game launches without errors"

States
    TASK      -> CEO gives a task
    CHANGELOG -> PM asks the developer what changed and why
    TEST      -> PM runs the Unity checks
    BUG       -> on failure, PM files a bug report and the developer proposes a fix
    REPORT    -> pass (or budget exhausted): one report for the CEO

The PM never edits project files. The only writes are evidence files under
pm_tester/runs/<timestamp>/.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional

import yaml

from .agent import PMTesterAgent, SYSTEM_PROMPT, load_env
from .hermes_client import HermesUnavailable, client_from_config
from .unity_tester import UnityTester

PASSED, FAILED, BLOCKED = "PASS", "FAIL", "BLOCKED"


@dataclass
class Run:
    task: str
    started: float = field(default_factory=time.time)
    attempts: int = 0
    transcript: List[Dict[str, Any]] = field(default_factory=list)
    evidence: Dict[str, Any] = field(default_factory=dict)
    verdict: str = BLOCKED
    notes: List[str] = field(default_factory=list)

    def add(self, role: str, content: str) -> None:
        self.transcript.append({"role": role, "at": round(time.time(), 2), "content": content})


def load_config(path: str) -> Dict[str, Any]:
    with open(path, encoding="utf-8") as fh:
        return yaml.safe_load(fh) or {}


def run_loop(task: str, cfg: Dict[str, Any], task_test: Optional[str] = None,
             dry_run: bool = False) -> Dict[str, Any]:
    root = os.path.dirname(os.path.abspath(__file__))
    run_dir = os.path.join(root, cfg.get("runs_dir", "runs"),
                           time.strftime("%Y%m%d_%H%M%S"))
    os.makedirs(run_dir, exist_ok=True)

    client = client_from_config(cfg)
    pm = PMTesterAgent(client=client)
    tester = UnityTester(cfg, run_dir)
    max_retries = int((cfg.get("pm") or {}).get("max_retries", 3))

    run = Run(task=task)
    run.add("ceo", task)

    # --- preflight -----------------------------------------------------------
    health = client.health()
    run.evidence["hermes_endpoint"] = health
    if not health.get("reachable"):
        run.verdict, run.notes = BLOCKED, [
            "developer endpoint unreachable — is the gateway running with API_SERVER_ENABLED=true?",
            str(health),
        ]
        return _finish(run, run_dir)

    probe = tester.probe()
    run.evidence["unity_backend"] = probe

    attempt = 0
    developer_context = ""
    while attempt <= max_retries:
        attempt += 1
        run.attempts = attempt

        # --- CHANGELOG ------------------------------------------------------
        try:
            changelog = pm.ask_changelog(task, developer_context)
        except HermesUnavailable as exc:
            run.verdict, run.notes = BLOCKED, [f"developer call failed: {exc}"]
            return _finish(run, run_dir)
        run.add("hermes", changelog)

        if dry_run:
            run.verdict = BLOCKED
            run.notes.append("dry run: stopped after the changelog request")
            return _finish(run, run_dir)

        # --- TEST -----------------------------------------------------------
        if task_test:
            test = tester.run_tests(task_test.split(":")[0], task_test.split(":")[-1])
        else:
            test = tester.smoke_test()
        run.evidence[f"test_attempt_{attempt}"] = test
        run.add("tester", json.dumps(test, indent=2)[:2000])

        if test.get("ok"):
            run.verdict = PASSED
            report = pm.report_to_ceo(task, True, run.evidence, attempt)
            run.add("pm", report)
            return _finish(run, run_dir)

        # --- BUG ------------------------------------------------------------
        try:
            bug_reply = pm.file_bug(task, test, attempt)
        except HermesUnavailable as exc:
            run.verdict, run.notes = BLOCKED, [f"could not file bug: {exc}"]
            return _finish(run, run_dir)
        run.add("hermes", bug_reply)

        try:
            review = pm.review_fix(task, bug_reply)
            run.add("pm", review)
            developer_context = f"Previous bug reply:\n{bug_reply}\n\nPM review:\n{review}"
        except HermesUnavailable:
            developer_context = bug_reply

    run.verdict = BLOCKED
    run.notes.append(f"retry budget exhausted after {max_retries} attempts")
    report = pm.report_to_ceo(task, False, run.evidence, run.attempts,
                              notes="Blocked: retry budget exhausted.")
    run.add("pm", report)
    return _finish(run, run_dir)


def _finish(run: Run, run_dir: str) -> Dict[str, Any]:
    payload = {
        "task": run.task,
        "verdict": run.verdict,
        "attempts": run.attempts,
        "evidence": run.evidence,
        "notes": run.notes,
        "transcript": run.transcript,
        "elapsed_s": round(time.time() - run.started, 1),
        "run_dir": run_dir,
    }
    out = os.path.join(run_dir, "run.json")
    with open(out, "w", encoding="utf-8") as fh:
        json.dump(payload, fh, indent=2)

    print("=" * 70)
    print(f"TASK     {run.task}")
    print(f"VERDICT  {run.verdict}  (attempts: {run.attempts}, {payload['elapsed_s']}s)")
    for key, value in run.evidence.items():
        if isinstance(value, dict) and value.get("evidence"):
            print(f"  {key}: {value['evidence']}")
    for note in run.notes:
        print(f"  ! {note}")
    print(f"ARTEFACT {out}")
    print("=" * 70)
    return payload


def main(argv: Optional[List[str]] = None) -> int:
    ap = argparse.ArgumentParser(description="PM/Tester loop for Train Survivor")
    ap.add_argument("--task", required=True, help="what the CEO wants verified")
    ap.add_argument("--config", default=os.path.join(os.path.dirname(__file__), "config.yaml"))
    ap.add_argument("--test", help="run a named test instead of the smoke test, e.g. EditMode:Foo")
    ap.add_argument("--dry-run", action="store_true", help="only ask for the changelog")
    ap.add_argument("--health", action="store_true", help="probe both endpoints and exit")
    args = ap.parse_args(argv)

    load_env(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".env"))
    cfg = load_config(args.config)

    if args.health:
        client = client_from_config(cfg)
        print(json.dumps({
            "hermes": client.health(),
            "unity": UnityTester(cfg, os.path.join(os.path.dirname(__file__), "runs", "health")).probe(),
        }, indent=2))
        return 0

    result = run_loop(args.task, cfg, task_test=args.test, dry_run=args.dry_run)
    return 0 if result["verdict"] == PASSED else 1


if __name__ == "__main__":
    sys.exit(main())
