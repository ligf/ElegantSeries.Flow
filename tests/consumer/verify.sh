#!/usr/bin/env bash
# NuGet consumer verification: packs the five packages, restores the
# standalone ConsumerApp against ONLY the freshly packed feed (plus
# nuget.org for third-party dependencies), then builds and runs it —
# once as a normal build and once published with NativeAOT.
# See README.md in this directory for details and the CI wiring.
set -euo pipefail

cd "$(dirname "$0")"
REPO_ROOT="$(cd ../.. && pwd)"
FEED="$PWD/feed"
WORK="$PWD/.work"

DOTNET="$(command -v dotnet || true)"
if [ -z "$DOTNET" ] && [ -x "$HOME/.dotnet/dotnet" ]; then
    DOTNET="$HOME/.dotnet/dotnet"
fi
if [ -z "$DOTNET" ]; then
    echo "dotnet SDK not found on PATH or in ~/.dotnet" >&2
    exit 1
fi

RID="${FLOW_VERIFY_RID:-linux-x64}"

rm -rf "$FEED" "$WORK"
mkdir -p "$FEED" "$WORK"

# Restore into an isolated global packages folder. A warm
# ~/.nuget/packages may already hold an ElegantSeries.Flow package with
# the exact version being packed (MinVer versions repeat across runs
# of the same commit), and NuGet serves a restored package from that
# cache without ever consulting the feed — the consumer would silently
# test stale bits instead of the packages packed below. The isolated
# folder lives under WORK, so it starts empty on every run and the
# consumer always resolves from the feed.
export NUGET_PACKAGES="$WORK/nuget-packages"

echo "==> Packing packages"
for project in ElegantSeries.Flow ElegantSeries.Flow.Mvvm ElegantSeries.Flow.Avalonia ElegantSeries.Flow.WPF ElegantSeries.Flow.Diagnostics; do
    "$DOTNET" pack "$REPO_ROOT/src/$project" -c Release -o "$FEED" --nologo -v q > /dev/null
done

# The packed version (MinVer, derived from the git tags) is read back
# from the core package file name so the consumer references exactly
# what was packed.
CORE_PACKAGE="$(ls "$FEED"/ElegantSeries.Flow.[0-9]*.nupkg | head -1)"
VERSION="$(basename "$CORE_PACKAGE" .nupkg)"
VERSION="${VERSION#ElegantSeries.Flow.}"
echo "==> Packed version: $VERSION"

echo "==> Consumer build + run (JIT)"
"$DOTNET" run --project ConsumerApp -c Release -p:FlowVersion="$VERSION"

echo "==> Consumer publish + run (NativeAOT, $RID)"
"$DOTNET" publish ConsumerApp -c Release -r "$RID" -p:PublishAot=true -p:FlowVersion="$VERSION" -o "$WORK/aot" --nologo -v q > /dev/null
"$WORK/aot/ConsumerApp"

echo "==> VERIFY PASS"
