#!/usr/bin/env bash
# Launch the Train Survivor Android emulator (Pixel 5 / API 34) with host GPU.
# Usage: tools/run-emulator.sh [avd-name]
set -euo pipefail

AH="$HOME/Unity/Hub/Editor/6000.0.85f2/Editor/Data/PlaybackEngines/AndroidPlayer"
export ANDROID_SDK_ROOT="$AH/SDK"
export ANDROID_HOME="$AH/SDK"
AVD="${1:-TrainSurvivor34}"

# -gpu host  -> real NVIDIA GPU via the emulator's GLES translator (fast, default)
# If the emulator dies on startup, try:  -gpu swiftshader_indirect  (slow but bulletproof)
exec "$AH/SDK/emulator/emulator" \
  -avd "$AVD" \
  -no-snapshot \
  -no-audio \
  -gpu host \
  -memory 4096 \
  -cores 6
