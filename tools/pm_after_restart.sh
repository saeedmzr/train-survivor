#!/usr/bin/env bash
# Detached runner: survives the gateway restart that kills the live chat turn.
LOG="C:/Users/saeed/projects/train-survivor/pm_tester/runs/after_restart.log"
: > "$LOG"
say() { echo "[$(date +%H:%M:%S)] $*" >> "$LOG"; }
say "runner started, waiting 45s for the chat turn to end"
sleep 45

say "restarting gateway"
hermes gateway restart --yes < /dev/null >> "$LOG" 2>&1
say "restart command returned $?"

KEY=$(grep '^API_SERVER_KEY=' "$HOME/AppData/Local/hermes/.env" | cut -d= -f2)
for i in $(seq 1 60); do
  code=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 -H "Authorization: Bearer $KEY" http://127.0.0.1:8642/v1/models 2>/dev/null)
  if [ "$code" = "200" ]; then say "api server up after $((i*3))s"; break; fi
  sleep 3
done
say "api check finished (last code=$code)"

cd "C:/Users/saeed/projects/train-survivor" || exit 1
say "running pm_tester loop"
/c/Users/saeed/Apps/img3d-venv/Scripts/python.exe -m pm_tester.loop   --task "verify the game launches without errors" >> "$LOG" 2>&1
say "loop exit=$?"
say "latest run dir: $(ls -1dt pm_tester/runs/*/ | head -1)"
