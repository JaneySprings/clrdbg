#!/bin/sh
# Builds the agent for Android and lays it out as the launch's device-library folder:
# <repository>/out/remote-target/android/<abi>/libremotecoreclrtarget.so, where the app build picks it up as a native
# library. Point a launch's 'remoteCoreclrTarget' at <repository>/out/remote-target.
#   ./build-android.sh [--enable-trace] [abi...]
# The abis are arm64-v8a (devices) and x86_64 (the emulator), both when none is given. --enable-trace builds the agent's
# Log calls in: they go to logcat under the tag 'remotecoreclrtarget'. The NDK is taken from ANDROID_NDK_HOME, else the
# newest one under $ANDROID_HOME/ndk (install one with 'sdkmanager "ndk;<version>"')
set -e
cd "$(dirname "$0")"
TRACE=""
ABIS=""
for ARGUMENT in "$@"; do
    case "$ARGUMENT" in
        --enable-trace) TRACE="-DCLRDBG_TRACE" ;;
        *) ABIS="$ABIS $ARGUMENT" ;;
    esac
done
if [ -z "$ABIS" ]; then
    ABIS="arm64-v8a x86_64"
fi
NDK=${ANDROID_NDK_HOME:-$(ls -d "${ANDROID_HOME:-$HOME/android}"/ndk/* 2>/dev/null | sort -V | tail -1)}
if [ -z "$NDK" ] || [ ! -d "$NDK/toolchains/llvm/prebuilt" ]; then
    echo "no Android NDK found: set ANDROID_NDK_HOME or install one under \$ANDROID_HOME/ndk" >&2
    exit 1
fi
TOOLCHAIN=$(ls -d "$NDK"/toolchains/llvm/prebuilt/* | head -1)/bin
API=21
SOURCES="*.cpp Protocol/*.cpp Debugger/*.cpp"
for ABI in $ABIS; do
    case "$ABI" in
        arm64-v8a) TARGET=aarch64-linux-android ;;
        x86_64) TARGET=x86_64-linux-android ;;
        *) echo "unknown abi '$ABI'" >&2; exit 1 ;;
    esac
    OUT=../../out/remote-target/android/$ABI
    mkdir -p "$OUT"
    # libc++ is linked in: the app need not ship libc++_shared.so; liblog serves the trace
    "$TOOLCHAIN/$TARGET$API-clang++" -std=c++17 -O1 -g -fPIC -shared -fvisibility=hidden -static-libstdc++ -I. $TRACE \
        -o "$OUT/libremotecoreclrtarget.so" $SOURCES -llog
    echo "built $(cd "$OUT" && pwd)/libremotecoreclrtarget.so"
done
