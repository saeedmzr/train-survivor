#!/usr/bin/env bash
# Windows (git-bash) deploy: build the x86_64 APK, install it on the emulator, launch it.
#
#   tools/deploy-win.sh            # build + install + launch
#   tools/deploy-win.sh --no-build # install the existing APK
#   tools/deploy-win.sh --avd      # also boot the AVD if no device is attached
#
# Overridable: UNITY_EDITOR, ANDROID_SDK, AVD_NAME
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TO_WIN() { cygpath -m "$1" 2>/dev/null || echo "$1"; }
HOME_WIN="$(TO_WIN "$HOME")"
ROOT_WIN="$(TO_WIN "$ROOT")"
UNITY_EDITOR="${UNITY_EDITOR:-C:/Program Files/Unity/Hub/Editor/6000.0.85f2/Editor/Unity.exe}"
ANDROID_SDK="${ANDROID_SDK:-$HOME_WIN/Android/sdk}"
AVD_NAME="${AVD_NAME:-train_survivor}"
ADB="$ANDROID_SDK/platform-tools/adb.exe"
EMULATOR="$ANDROID_SDK/emulator/emulator.exe"
APK="$ROOT_WIN/unity/Builds/Android/TrainSurvivor-emulator.apk"
PKG="com.trainsurvivor.game"

BUILD=1
BOOT=0
for arg in "$@"; do
  case "$arg" in
    --no-build) BUILD=0 ;;
    --avd)      BOOT=1 ;;
  esac
done

if [[ "$BOOT" == "1" ]]; then
  if ! "$ADB" get-state >/dev/null 2>&1; then
    echo "== booting AVD $AVD_NAME =="
    ( cd "$ANDROID_SDK/emulator" && ./emulator.exe -avd "$AVD_NAME" -no-snapshot -gpu angle_indirect \
        -no-audio -memory 4096 -cores 8 >/dev/null 2>&1 & )
  fi
fi

if [[ "$BUILD" == "1" ]]; then
  echo "== building (x86_64 emulator) =="
  "$UNITY_EDITOR" -batchmode -quit -nographics \
    -projectPath "$ROOT_WIN/unity" \
    -executeMethod TrainSurvivor.EditorTools.BuildSmoke.BuildEmulator \
    -logFile "$ROOT_WIN/unity/Logs/build.log" || {
      echo "build failed, see unity/Logs/build.log"; exit 1; }
fi

echo "== waiting for device =="
"$ADB" start-server >/dev/null
for _ in $(seq 1 120); do
  [[ "$("$ADB" get-state 2>/dev/null | tr -d '\r')" == "device" ]] && break
  sleep 2
done
until [[ "$("$ADB" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" == "1" ]]; do sleep 2; done

echo "== install =="
"$ADB" install -r "$APK" | tail -2

# Unity 6 picks the launcher activity at build time (UnityPlayerActivity or the newer
# AppUIActivity); resolve it instead of hardcoding one.
ACTIVITY="$("$ADB" shell cmd package resolve-activity --brief "$PKG" 2>/dev/null | tr -d '\r' | tail -1)"
[[ "$ACTIVITY" == */* ]] || ACTIVITY="$PKG/com.unity3d.player.UnityPlayerActivity"

echo "== launch $ACTIVITY =="
"$ADB" shell am force-stop "$PKG" >/dev/null 2>&1 || true
"$ADB" shell am start -n "$ACTIVITY" >/dev/null
"$ADB" logcat -c
echo "done. live log:  \"$ADB\" logcat -s Unity:V"
