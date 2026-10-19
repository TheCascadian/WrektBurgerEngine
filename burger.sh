#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-${TMPDIR:-/tmp}/burgerengine-dotnet}"
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
mkdir -p "$DOTNET_CLI_HOME"
DOTNET_BIN="${BURGER_DOTNET:-dotnet}"
if ! command -v "$DOTNET_BIN" >/dev/null 2>&1; then
    for candidate in "${DOTNET_ROOT:-}/dotnet" "$PWD/.dotnet/dotnet" "/home/oddsoul/.dotnet/dotnet"; do
        if [ -x "$candidate" ]; then DOTNET_BIN="$candidate"; break; fi
    done
fi
if ! command -v "$DOTNET_BIN" >/dev/null 2>&1 && [ ! -x "$DOTNET_BIN" ]; then
    echo "dotnet SDK/runtime not found; set BURGER_DOTNET or DOTNET_ROOT" >&2
    exit 127
fi
"$DOTNET_BIN" restore BurgerEngine.sln
"$DOTNET_BIN" build BurgerEngine.sln -c Release -nologo --no-restore
exec "$DOTNET_BIN" src/Burger.Tools/bin/Release/net8.0/burger.dll "$@"
