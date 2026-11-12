# PM/Tester

A second agent that reviews and tests the developer's (Hermes) work on **Train Survivor**.
It never writes game code — it asks what changed, drives Unity, reads the evidence, and
escalates a verdict to the CEO.

```
pm_tester/
├── agent.py           PM/Tester persona: asks for changelogs, files bugs, writes CEO reports
├── hermes_client.py   OpenAI-compatible client for the Hermes API server
├── unity_tester.py    Unity MCP client (+ adb fallback): play mode, logs, screenshot, tests
├── loop.py            State machine: TASK → CHANGELOG → TEST → BUG ↺ → REPORT
├── config.yaml        Endpoints, model, retry budget, MCP tool names
├── .env               HERMES_API_KEY (gitignored)
├── requirements.txt
└── runs/<timestamp>/  run.json + screenshots + logcat for every run
```

## 1. The developer endpoint (Hermes)

Enabled in `~/AppData/Local/hermes/.env`:

```
API_SERVER_ENABLED=true
API_SERVER_KEY=<the key in pm_tester/.env>
```

The gateway only reads these at start, so restart it once:

```bash
hermes gateway restart --yes     # --yes because it interrupts live chat sessions
```

Then:

| | |
|---|---|
| URL | `http://127.0.0.1:8642/v1` |
| Chat | `POST /v1/chat/completions` (also `/v1/responses`, `/v1/runs`) |
| Model name | `hermes-agent` |
| Auth | `Authorization: Bearer $HERMES_API_KEY` |
| Session | optional `X-Hermes-Session-Key` keeps one conversation on the server |

Smoke test:

```bash
curl http://127.0.0.1:8642/v1/chat/completions \
  -H "Authorization: Bearer $HERMES_API_KEY" -H "Content-Type: application/json" \
  -d '{"model":"hermes-agent","messages":[{"role":"user","content":"ping"}]}'
```

## 2. Unity MCP bridge

**Ludus:** not a Unity package — the projects under that name are trading/cyber-range MCP
servers. The functioning Unity Editor MCP servers are **Singtaa/UnityMCP** and CoplayDev's
`unity-mcp`. This project uses Singtaa.

Installed at `unity/Packages/com.singtaa.unity-mcp`:

```bash
cd unity/Packages
git clone https://github.com/Singtaa/UnityMCP.git com.singtaa.unity-mcp
```

Open the project in the Unity Editor (not batchmode). The package compiles, auto-starts a
Node bridge, and serves MCP over HTTP:

| | |
|---|---|
| MCP HTTP | `http://127.0.0.1:5173` |
| Editor bridge | `127.0.0.1:52100` |
| Auth | bearer token if `Auth Enabled` is on — put it in `UNITY_MCP_TOKEN` |

Check it: `python -m pm_tester.loop --health --task x` — `unity.ok: true` and the tool list
means the bridge is live.

Tool names differ between Unity MCP distributions. If yours differ, edit
`unity_mcp.tools` in `config.yaml`; `--health` prints the actual names.

## 3. Running the loop

```bash
cd pm_tester
pip install -r requirements.txt
python -m pm_tester.loop --task "verify the game launches without errors"
```

Other forms:

```bash
python -m pm_tester.loop --task "verify the game launches without errors" --dry-run
python -m pm_tester.loop --task "check turret recoil" --test "EditMode:TurretRigTests"
python -m pm_tester.loop --health --task probe
```

Exit code is `0` on PASS, `1` otherwise. Every run writes `runs/<timestamp>/run.json` with
the full transcript, the test evidence, and the verdict.

### The loop

1. **TASK** — the CEO supplies a task.
2. **CHANGELOG** — the PM asks Hermes what changed, why, how to verify, and what it did not
   verify. A vague answer is itself a defect.
3. **TEST** — `unity_tester.smoke_test()`: enter Play Mode, collect console logs, capture a
   screenshot, flag fatal errors (the known `InputUpdateType.None` input exception is
   ignored by name).
4. **BUG** — on failure the PM files symptom/evidence/expected/repro and Hermes replies with
   a cause and a fix plan; the PM reviews that reply before re-testing. Bounded by
   `pm.max_retries` (default 3).
5. **REPORT** — PF passes, or blocked: one report for the CEO with evidence paths.

## 4. Role separation

- `agent._assert_review_task()` raises `PermissionError` if a task reads as "implement /
  patch / fix it yourself" without a review verb — the PM cannot be talked into writing code.
- The PM writes only inside `pm_tester/runs/`.
- The system prompt repeats the rule and requires evidence, not assertions.

## 5. Fallback when no editor is open

`unity_mcp.fallback` drives the built APK on the emulator instead: install, launch, read
`logcat -s Unity:V`, `screencap`. The same `{ok, detail, evidence}` shape is returned, so the
loop runs without a GUI Unity session (slower, but real evidence).

## Assumptions and limits

- The API server binds to `127.0.0.1` on the gateway host; the PM runs on the same machine.
- The Unity Editor must be open for the MCP path; batchmode builds do not start the bridge.
- `unity_tester` assumes HTTP JSON-RPC. A stdio-only MCP server needs a different transport.
- The smoke test proves "no fatal errors + a frame was rendered". It does not prove gameplay
  correctness — that needs named tests or scripted input.
