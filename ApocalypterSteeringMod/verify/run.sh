#!/bin/bash
# Compile-check + logic tests for ApocalypterSteeringMod without the game.
# Needs: a .NET SDK (8 or newer; tested on 10), and BepInEx 5 core DLLs in verify/refs/:
#   0Harmony.dll BepInEx.dll MonoMod.Utils.dll MonoMod.RuntimeDetour.dll Mono.Cecil*.dll
#   (from BepInEx_win_x64_5.4.x.zip -> BepInEx/core/)
# Unity / NWH / PlayMaker are replaced by compile stubs in stubs/ (signatures from gamecode/).
# The DLL built here is for checking only: do NOT install it. Build the real one with dotnet build.
set -e
cd "$(dirname "$0")"
PLUGIN=../plugin
SRC=$(find $PLUGIN -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' 2>/dev/null || true)
if [ $(echo "$SRC" | grep -c '\.cs$') -lt 10 ]; then echo "plugin sources not found next to verify/ (expected ../plugin)"; exit 1; fi
DOTNET_ROOT="$(dirname "$(readlink -f "$(which dotnet)")")"
CSC_PATH="$(ls -d "$DOTNET_ROOT"/sdk/*/Roslyn/bincore/csc.dll | sort -V | tail -1)"
NS=$(ls -d "$DOTNET_ROOT"/packs/NETStandard.Library.Ref/*/ref/netstandard2.1 2>/dev/null | tail -1)
if [ -z "$NS" ]; then
  # No NETStandard.Library.Ref pack installed: use the NETCore.App.Ref pack, which
  # includes the netstandard.dll facade.
  NS=$(ls -d "$DOTNET_ROOT"/packs/Microsoft.NETCore.App.Ref/*/ref/net*/ | sort -V | tail -1)
  NS="${NS%/}"
fi
FW=()
for f in "$NS"/*.dll; do FW+=("-r:$f"); done
mkdir -p out && cp refs/*.dll out/
RC='{ "runtimeOptions": { "tfm": "net8.0", "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.0" }, "rollForward": "LatestMajor" } }'

dotnet "$CSC_PATH" -nologo -noconfig -nostdlib -t:library -langversion:latest -nowarn:CS0436 "${FW[@]}" -out:out/UnityEngine.dll stubs/*.cs
dotnet "$CSC_PATH" -nologo -noconfig -nostdlib -t:library -langversion:latest -warn:4 "${FW[@]}" -r:out/UnityEngine.dll -r:out/BepInEx.dll -r:out/0Harmony.dll \
  -out:out/ApocalypterSteeringMod.dll $SRC
echo "compile: OK ($(echo "$SRC" | wc -l) source files)"

dotnet "$CSC_PATH" -nologo -noconfig -nostdlib -t:exe -langversion:latest "${FW[@]}" -r:out/UnityEngine.dll -r:out/BepInEx.dll -r:out/0Harmony.dll \
  -r:out/ApocalypterSteeringMod.dll -out:out/Tests.dll tests/Tests.cs
echo "$RC" > out/Tests.runtimeconfig.json
dotnet out/Tests.dll

# Steering prefix from source with a FieldRefAccess shim (BepInEx's Harmony can't initialise on .NET 8).
dotnet "$CSC_PATH" -nologo -noconfig -nostdlib -t:exe -langversion:latest "${FW[@]}" -r:out/UnityEngine.dll -out:out/PrefixTests.dll \
  tests/prefix/*.cs $PLUGIN/Patching/TractionEdgeSteeringPatch.cs $PLUGIN/Settings/*.cs $PLUGIN/Game/GameSettingsReader.cs
echo "$RC" > out/PrefixTests.runtimeconfig.json
dotnet out/PrefixTests.dll
