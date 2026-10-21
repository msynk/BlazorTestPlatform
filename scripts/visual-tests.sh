#!/usr/bin/env bash
# Runs the browser suites inside the Playwright image that matches the pinned Microsoft.Playwright package, so
# screenshots match CI byte for byte.
#
#   scripts/visual-tests.sh                   # compare against committed chromium-linux baselines
#   scripts/visual-tests.sh --update          # write missing baselines (review, then commit them)
#   scripts/visual-tests.sh --update=changed  # after a Playwright upgrade: rewrite changed baselines, relabel the rest
#   scripts/visual-tests.sh --print-image     # print the image tag and exit
#
# The image tag is derived from Directory.Packages.props, so upgrading Playwright is a one-line change.
# BTP_PLAYWRIGHT_IMAGE overrides it. Requires Docker.
set -euo pipefail

# Git Bash on Windows: keep container paths (/repo) as-is and pass Docker a Windows path for the mount.
export MSYS_NO_PATHCONV=1
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && (pwd -W 2>/dev/null || pwd))"
PLAYWRIGHT_VERSION="$(sed -n 's/.*Include="Microsoft\.Playwright" Version="\([^"]*\)".*/\1/p' "$ROOT/Directory.Packages.props" | head -n 1)"
if [ -z "$PLAYWRIGHT_VERSION" ]; then
  echo "Could not read the Microsoft.Playwright version from Directory.Packages.props" >&2
  exit 1
fi

IMAGE="${BTP_PLAYWRIGHT_IMAGE:-mcr.microsoft.com/playwright/dotnet:v${PLAYWRIGHT_VERSION}-noble}"
DOTNET_CHANNEL="10.0"
MODE="none"
case "${1:-}" in
  --print-image) echo "$IMAGE"; exit 0 ;;
  --update) MODE="missing" ;;
  --update=*) MODE="${1#--update=}" ;;
esac

echo "Playwright $PLAYWRIGHT_VERSION, image $IMAGE, BTP_UPDATE_SNAPSHOTS=$MODE"
docker run --rm --init --ipc=host \
  -v "$ROOT:/repo" -w /repo \
  -v btp-nuget:/root/.nuget/packages \
  -v btp-dotnet:/opt/dotnet10 \
  -e BTP_UPDATE_SNAPSHOTS="$MODE" \
  -e BTP_PROJECT_DIR=/repo/samples/Contoso.UI.Tests \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  "$IMAGE" bash -lc "
    set -e
    if [ ! -x /opt/dotnet10/dotnet ]; then
      curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel $DOTNET_CHANNEL --install-dir /opt/dotnet10
    fi
    export PATH=/opt/dotnet10:\$PATH DOTNET_ROOT=/opt/dotnet10
    dotnet test --project samples/Contoso.UI.Tests --artifacts-path /tmp/btp-artifacts -- \
      --filter-class '*CrossHostingTests' --filter-class '*VisualTests' --filter-class '*InAppPlayTests'
  "
