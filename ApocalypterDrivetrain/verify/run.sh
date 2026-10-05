#!/bin/bash
# Compile-check + logic tests for ApocalypterDrivetrain without the game.
# Needs: a .NET SDK (8 or newer), and BepInEx 5 core DLLs in verify/refs/:
#   0Harmony.dll BepInEx.dll MonoMod.Utils.dll MonoMod.RuntimeDetour.dll Mono.Cecil*.dll
#   (from BepInEx_win_x64_5.4.x.zip -> BepInEx/core/)
# Unity / NWH / PlayMaker are replaced by compile stubs in stubs/ (signatures from gamecode/).
# The DLL built here is for checking only: do NOT install it. Build the real one with dotnet build.
#
# Steps:
#   1. stubs/            -> out/UnityEngine.dll  (all game-side types, one stub assembly)
#   2. plugin/Model/     -> out/Model.dll        WITHOUT Unity/NWH/BepInEx refs: proves the model is pure
#   3. plugin/ (all)     -> out/ApocalypterDrivetrain.dll  (C# 9, as the real csproj)
#   4. tests/Tests.cs    -> run
set -e
cd "$(dirname "$0")"
PLUGIN=../plugin
SPO=../inputs/SPO.Vehicle/HostInterfaces.cs
SRC=$(find $PLUGIN -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' 2>/dev/null || true)
if [ $(echo "$SRC" | grep -c '\.cs$') -lt 10 ]; then echo "plugin sources not found next to verify/ (expected ../plugin)"; exit 1; fi
if [ ! -f "$SPO" ]; then echo "SPO.Vehicle interfaces not found ($SPO)"; exit 1; fi
MODEL_SRC=$(find $PLUGIN/Model -name '*.cs')
DOTNET_ROOT="$(dirname "$(readlink -f "$(which dotnet)")")"
CSC_PATH="$(ls -d "$DOTNET_ROOT"/sdk/*/Roslyn/bincore/csc.dll | sort -V | tail -1)"
NS=$(ls -d "$DOTNET_ROOT"/packs/NETStandard.Library.Ref/*/ref/netstandard2.1 2>/dev/null | tail -1)
if [ -z "$NS" ]; then
  # SDK 9+ no longer ships the NETStandard.Library.Ref pack. Use the copy cached by the
  # harness (nuget.org/api/v2/package/NETStandard.Library.Ref/2.1.0, MIT) — the same refs
  # the SDK used to carry — fetching it on first run if needed. Without it the pure-model
  # compile would fall back to NETCore.App.Ref and the ModelIsolation test would fail.
  NSCACHE="$PWD/.cache/NETStandard.Library.Ref/2.1.0"
  if [ ! -d "$NSCACHE/ref/netstandard2.1" ]; then
    echo "fetching NETStandard.Library.Ref 2.1.0 into $NSCACHE"
    mkdir -p "$NSCACHE/ref"
    if command -v curl >/dev/null 2>&1 && command -v unzip >/dev/null 2>&1 \
       && curl -fsSL -o "$NSCACHE/pkg.nupkg" https://www.nuget.org/api/v2/package/NETStandard.Library.Ref/2.1.0 \
       && unzip -q -o "$NSCACHE/pkg.nupkg" -d "$NSCACHE/pkg" \
       && mv "$NSCACHE/pkg/ref/netstandard2.1" "$NSCACHE/ref/netstandard2.1"; then
      rm -rf "$NSCACHE/pkg" "$NSCACHE/pkg.nupkg"
    else
      echo "WARNING: could not fetch NETStandard.Library.Ref; the model-purity check will fail"
    fi
  fi
  NS="$NSCACHE/ref/netstandard2.1"
fi
if [ ! -d "$NS" ]; then
  # Last resort: the NETCore.App.Ref pack, which includes the netstandard.dll facade.
  NS=$(ls -d "$DOTNET_ROOT"/packs/Microsoft.NETCore.App.Ref/*/ref/net*/ | sort -V | tail -1)
  NS="${NS%/}"
fi
FW=()
for f in "$NS"/*.dll; do FW+=("-r:$f"); done
rm -rf out
mkdir -p out && cp refs/*.dll out/
RC='{ "runtimeOptions": { "tfm": "net8.0", "framework": { "name": "Microsoft.NETCore.App", "version": "8.0.0" }, "rollForward": "LatestMajor" } }'

dotnet "$CSC_PATH" -nologo -noconfig -nostdlib -t:library -langversion:latest -nowarn:CS0436 "${FW[@]}" -out:out/UnityEngine.dll stubs/*.cs
echo "stubs: OK"

# -warnaserror: the model must stay warning-free; any Unity/NWH/BepInEx use fails here (no refs given).
dotnet "$CSC_PATH" -nologo -noconfig -nostdlib -t:library -langversion:9 -warn:4 -warnaserror "${FW[@]}" \
  -out:out/Model.dll $MODEL_SRC $SPO
echo "model (pure, no Unity/NWH/BepInEx refs): OK ($(echo "$MODEL_SRC" | wc -l) source files)"

dotnet "$CSC_PATH" -nologo -noconfig -nostdlib -t:library -langversion:9 -warn:4 -warnaserror "${FW[@]}" -r:out/UnityEngine.dll -r:out/BepInEx.dll -r:out/0Harmony.dll \
  -out:out/ApocalypterDrivetrain.dll $SRC $SPO
echo "compile: OK ($(echo "$SRC" | wc -l) source files)"

dotnet "$CSC_PATH" -nologo -noconfig -nostdlib -t:exe -langversion:latest "${FW[@]}" -r:out/UnityEngine.dll -r:out/BepInEx.dll -r:out/0Harmony.dll \
  -r:out/ApocalypterDrivetrain.dll -out:out/Tests.dll tests/Tests.cs
echo "$RC" > out/Tests.runtimeconfig.json
dotnet out/Tests.dll
