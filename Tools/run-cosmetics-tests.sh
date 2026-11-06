#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET="${DOTNET:-dotnet}"
PYTHON="${PYTHON:-python3}"
"$PYTHON" -m unittest discover -s Tests/Cosmetics -p 'test_*.py' -v
# Output/ is shared by both configurations; leave the Release pair together.
"$DOTNET" build Light/LightPluginMain.csproj -c Debug --nologo
"$DOTNET" build Light/LightPluginMain.csproj -c Release --nologo
"$DOTNET" run --project Tests/Cosmetics/Cosmetics.csproj -c Release -- Output/Light.dll
