"""The PM/Tester agent: reviewer and tester, never a developer.

It talks to Hermes (the developer) over the OpenAI-compatible API, asks what
changed and why, and never writes game code. Every method here either asks a
question, files a bug, or judges evidence.
"""
from __future__ import annotations

import json
import os
import re
from dataclasses import dataclass
from typing import Any, Dict, List, Optional

from .hermes_client import HermesClient, HermesUnavailable

SYSTEM_PROMPT = """You are the PM/Tester for the Train Survivor Unity project.

ROLE — STRICT
- You are a product reviewer and QA tester. You do NOT write, edit or design game code.
- The developer is Hermes. It writes code, fixes bugs, and explains its reasoning.
- The CEO is the human. It only ever sees: pass/fail, evidence, and escalations.

WHAT YOU DO
1. Ask the developer for a changelog before testing: what changed, which files,
   why this approach, what could break, and what it did NOT verify.
2. Judge the answer: is the change scoped to the task, is the reasoning sound,
   are the risks named honestly?
3. Run the Unity tests (play mode, logs, screenshot, smoke test) and read the
   evidence yourself.
4. On failure, file ONE bug report: symptom, evidence, expected behaviour,
   reproduction steps, and what must not regress. Be specific and terse.
5. On pass, report to the CEO: what was verified, with the evidence paths.
6. Escalate to the CEO when blocked: tooling broken, contradictory evidence,
   or the retry budget is exhausted.

RULES
- Never accept "it should work" as evidence. Only logs, screenshots, test output.
- Never propose implementation details. Describe the observable defect instead.
- Never write to project files. You may write only inside pm_tester/runs/.
- If the developer reports something as unverified, say so in your report.

FORMAT
Reply with plain text. Keep bug reports under 200 words and always include the
evidence you used. When you finish a review, end with a line:
RESULT: PASS | RESULT: FAIL | RESULT: BLOCKED
"""

# Guard: refuse anything that looks like a request to modify the project.
_WRITE_INTENT = re.compile(
    r"\b(implement|write the code|edit|patch|commit|refactor|fix it yourself|"
    r"add a method|modify|change the file)\b",
    re.IGNORECASE,
)


@dataclass
class PMTesterAgent:
    client: HermesClient
    project: str = "Train Survivor"

    # ------------------------------------------------------------------ guards

    @staticmethod
    def _assert_review_task(task: str) -> None:
        if _WRITE_INTENT.search(task) and not re.search(
            r"\b(review|verify|test|check|assess)\b", task, re.IGNORECASE
        ):
            raise PermissionError(
                "PM/Tester does not write code. Re-phrase as a review or test request, "
                "or hand the task to the developer."
            )

    # ------------------------------------------------------------------ dialogue

    def ask_changelog(self, task: str, context: str = "") -> str:
        """Step 2 of the loop: what changed and why."""
        self._assert_review_task(task)
        message = (
            f"TASK UNDER REVIEW: {task}\n\n"
            "Reply as the developer with a changelog for this task:\n"
            "1. What you changed (files, systems)\n"
            "2. Why this approach, and what you rejected\n"
            "3. How it can be verified (exact steps or dev flags)\n"
            "4. What you did NOT verify, and known risks\n"
        )
        if context:
            message += f"\nContext from the last cycle:\n{context}\n"
        return self.client.chat(message)

    def file_bug(self, task: str, evidence: Dict[str, Any], attempt: int) -> str:
        """Step 4a: a failure goes back to the developer as a bug report."""
        message = (
            f"BUG REPORT for task: {task} (attempt {attempt})\n\n"
            "Test evidence:\n"
            f"{json.dumps(evidence, indent=2)[:4000]}\n\n"
            "Produce: (a) the most likely cause, (b) the minimal fix you propose, "
            "(c) what you will verify after the fix. Do not claim it is fixed yet."
        )
        return self.client.chat(message)

    def review_fix(self, task: str, developer_reply: str) -> str:
        """Judge a proposed fix before re-testing."""
        message = (
            f"Task: {task}\n\nThe developer says it changed:\n{developer_reply}\n\n"
            "As PM/Tester, answer in at most 5 bullets: is this in scope, does the "
            "reasoning hold, what regression could it introduce, and what exactly "
            "should the next test prove?"
        )
        return self.client.chat(message)

    def report_to_ceo(self, task: str, passed: bool, evidence: Dict[str, Any],
                      attempts: int, notes: str = "") -> str:
        """Step 5: the only thing the CEO sees, plus escalations."""
        verdict = "PASS" if passed else "BLOCKED"
        lines = [
            f"PM/TESTER REPORT — {task}",
            f"RESULT: {verdict} after {attempts} attempt(s)",
            "",
            "Evidence:",
        ]
        for key, value in evidence.items():
            lines.append(f"- {key}: {value}")
        if notes:
            lines += ["", notes]
        return "\n".join(lines)

    # ------------------------------------------------------------------ helpers

    def health(self) -> Dict[str, Any]:
        return self.client.health()


def load_env(path: str = ".env") -> None:
    """Tiny .env loader so HERMES_API_KEY can live beside the project."""
    if not os.path.exists(path):
        return
    for raw in open(path, encoding="utf-8"):
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))
