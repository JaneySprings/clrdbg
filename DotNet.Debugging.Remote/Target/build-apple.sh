#!/bin/sh
# Builds the agent for the Apple targets, signs each ad hoc, and lays them out as the launch's device-library folder:
# <repository>/out/remote-target/<platform>/<rid>/libremotecoreclrtarget.dylib. Point a launch's 'remoteCoreclrTarget'
# at <repository>/out/remote-target.
#   ./build-apple.sh [--enable-trace] [rid...]
# The rids are maccatalyst-arm64, maccatalyst-x64, iossimulator-arm64, iossimulator-x64 and ios-arm64; without any,
# all of them in that order, so the Mac Catalyst libraries are built before a missing iOS SDK can stop the script.
# --enable-trace builds the agent's Log calls in: they print to the app's standard output
set -e
cd "$(dirname "$0")"
TRACE=""
RIDS=""
for ARGUMENT in "$@"; do
    case "$ARGUMENT" in
        --enable-trace) TRACE="-DCLRDBG_TRACE" ;;
        *) RIDS="$RIDS $ARGUMENT" ;;
    esac
done
if [ -z "$RIDS" ]; then
    RIDS="maccatalyst-arm64 maccatalyst-x64 iossimulator-arm64 iossimulator-x64 ios-arm64"
fi
SOURCES="*.cpp Protocol/*.cpp Debugger/*.cpp"
for RID in $RIDS; do
    case "$RID" in
        maccatalyst-arm64) PLATFORM=maccatalyst; TARGET=arm64-apple-ios17.0-macabi; SDK=macosx ;;
        maccatalyst-x64) PLATFORM=maccatalyst; TARGET=x86_64-apple-ios17.0-macabi; SDK=macosx ;;
        iossimulator-arm64) PLATFORM=ios; TARGET=arm64-apple-ios15.0-simulator; SDK=iphonesimulator ;;
        iossimulator-x64) PLATFORM=ios; TARGET=x86_64-apple-ios15.0-simulator; SDK=iphonesimulator ;;
        ios-arm64) PLATFORM=ios; TARGET=arm64-apple-ios15.0; SDK=iphoneos ;;
        *) echo "unknown rid '$RID'" >&2; exit 1 ;;
    esac
    OUT=../../out/remote-target/$PLATFORM/$RID
    mkdir -p "$OUT"
    clang++ -std=c++17 -O1 -g -fPIC -dynamiclib -fvisibility=hidden -I. $TRACE \
        -target "$TARGET" -isysroot "$(xcrun --sdk "$SDK" --show-sdk-path)" \
        -install_name @rpath/libremotecoreclrtarget.dylib \
        -o "$OUT/libremotecoreclrtarget.dylib" $SOURCES
    codesign --force --sign - "$OUT/libremotecoreclrtarget.dylib"
    echo "built $(cd "$OUT" && pwd)/libremotecoreclrtarget.dylib"
done
