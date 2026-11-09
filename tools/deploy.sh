#!/usr/bin/env bash
# Build the Android APK, install it on the running emulator/device, and launch it.
#
#   tools/deploy.sh              # ARM64 release build (shipping)
#   tools/deploy.sh --emulator   # x86_64 build (the emulator cannot run Unity's ARM64 BTI code)
#   tools/deploy.sh --no-build   # install whatever APK is already there
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="$HOME/Unity/Hub/Editor/6000.0.85f2/Editor/Unity"
AH="$HOME/Unity/Hub/Editor/6000.0.85f2/Editor/Data/PlaybackEngines/AndroidPlayer"
ADB="$AH/SDK/platform-tools/adb"
PKG="com.trainsurvivor.game"

MODE="arm64"
APK=""
for arg in "$@"; do
  case "$arg" in
    --emulator) MODE="emulator" ;;
    --no-build) MODE="nobuild" ;;
    --arm64)    MODE="arm64" ;;
  esac
done

case "$MODE" in
  emulator)
    METHOD="TrainSurvivor.EditorTools.BuildSmoke.BuildEmulator"
    APK="$ROOT/unity/Builds/Android/TrainSurvivor-emulator.apk" ;;
  arm64)
    METHOD="TrainSurvivor.EditorTools.BuildSmoke.BuildArm64"
    APK="$ROOT/unity/Builds/Android/TrainSurvivor-smoke.apk" ;;
  nobuild)
    APK="${APK:-$ROOT/unity/Builds/Android/TrainSurvivor-emulator.apk}" ;;
esac

if [[ "$MODE" != "nobuild" ]]; then
  echo "== building ($MODE) =="
  "$UNITY" -batchmode -quit -nographics \
    -projectPath "$ROOT/unity" \
    -executeMethod "$METHOD" \
    -logFile "$ROOT/unity/Logs/build.log" || {
      echo "build failed, see unity/Logs/build.log"; exit 1; }
fi

echo "== waiting for device =="
"$ADB" wait-for-device
until [[ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" == "1" ]]; do sleep 2; done

# Unity 6 picks the launcher activity at build time (UnityPlayerActivity or the newer
# AppUIActivity); resolve it instead of hardcoding one.
ACTIVITY="$("$ADB" shell cmd package resolve-activity --brief "$PKG" 2>/dev/null | tr -d '\r' | tail -1)"
[[ "$ACTIVITY" == */* ]] || ACTIVITY="$PKG/com.unity3d.player.UnityPlayerActivity"

echo "== install =="
"$ADB" install -r "$APK"

echo "== launch =="
"$ADB" shell am force-stop "$PKG" >/dev/null 2>&1 || true
"$ADB" shell am start -n "$ACTIVITY" >/dev/null
"$ADB" logcat -c
echo "done. live log:  $ADB logcat -s Unity:V"
