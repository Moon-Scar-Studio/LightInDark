#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET="${DOTNET:-dotnet}"
PYTHON="${PYTHON:-python3}"
"$PYTHON" Tests/Regression/extract_config.py
# Both configurations write Output/. Keep builds sequential and leave Release as the deliverable.
"$DOTNET" build Light/LightPluginMain.csproj -c Debug --nologo
"$DOTNET" build Light/LightPluginMain.csproj -c Release --nologo
"$DOTNET" build VerificationServer/VerificationServer.csproj -c Release --nologo
"$PYTHON" -m unittest discover -s Tests/Cosmetics -p 'test_*.py' -v
"$DOTNET" run --project Tests/Cosmetics/Cosmetics.csproj -c Release -- Output/Light.dll
"$DOTNET" run --project Tests/Regression/Regression.csproj -- \
  VerificationServer/bin/Release/net10.0/LightVerificationServer.dll Output/Light.dll
"$DOTNET" run --project Tests/Gameplay/Gameplay.csproj -- Output/LightInDark.dll
"$DOTNET" run --project Tests/Rpc/Rpc.csproj
"$DOTNET" run --project Tests/Lobby/Lobby.csproj -c Debug -- Output/Light.dll
"$DOTNET" run --project Tests/Lobby/Lobby.csproj -c Release -- Output/Light.dll
"$DOTNET" build Tests/ApiConsumer/ApiConsumer.csproj -c Release --nologo
