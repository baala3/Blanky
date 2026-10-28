#!/usr/bin/env bash
# Dev helper for working from WSL: mirrors the repo to the Windows drive (WinUI tooling
# is unreliable on \\wsl$ paths) and runs the Windows .NET SDK there.
#
#   scripts/win.sh build            Debug build of the whole solution (x64)
#   scripts/win.sh test             Run the Blanky.Core tests
#   scripts/win.sh run [args...]    Build, then launch the app with args
#   scripts/win.sh publish          Release publish to artifacts/publish (x64)
set -euo pipefail

repo="$(cd "$(dirname "$0")/.." && pwd)"
win_dir="${BLANKY_WIN_DIR:-/mnt/c/Users/$(cmd.exe /c 'echo %USERNAME%' 2>/dev/null | tr -d '\r')/.blanky-build/repo}"
dotnet="dotnet.exe"

sync() {
  mkdir -p "$win_dir"
  rsync -a --delete --exclude .git --exclude bin --exclude obj --exclude artifacts \
    --exclude node_modules --exclude website/dist --exclude website/.astro \
    "$repo/" "$win_dir/"
}

app_exe="src/Blanky/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/Blanky.exe"

cmd="${1:-build}"
shift || true
sync
cd "$win_dir"
case "$cmd" in
  build) "$dotnet" build Blanky.slnx -c Debug -p:Platform=x64 -nologo -clp:ErrorsOnly\;Summary ;;
  test)
    # Run the xUnit exe directly under a hard time limit, so a hung test fails fast.
    "$dotnet" build tests/Blanky.Core.Tests -c Debug -nologo -clp:ErrorsOnly\;Summary
    cd tests/Blanky.Core.Tests/bin/Debug/net10.0
    status=0
    timeout "${BLANKY_TEST_TIMEOUT:-90}" ./Blanky.Core.Tests.exe "$@" || status=$?
    if [ "$status" -eq 124 ]; then
      echo "Tests timed out; stopping the test process." >&2
      taskkill.exe /IM Blanky.Core.Tests.exe /F >/dev/null 2>&1 || true
    fi
    exit "$status" ;;
  run)
    "$dotnet" build src/Blanky/Blanky.csproj -c Debug -p:Platform=x64 -nologo -clp:ErrorsOnly\;Summary
    "./$app_exe" "$@" ;;
  publish)
    "$dotnet" publish src/Blanky/Blanky.csproj -c Release -p:Platform=x64 -r win-x64 -o artifacts/publish -nologo
    mkdir -p "$repo/artifacts" && rsync -a --delete artifacts/publish/ "$repo/artifacts/publish/" ;;
  *) echo "unknown command: $cmd" >&2; exit 2 ;;
esac
