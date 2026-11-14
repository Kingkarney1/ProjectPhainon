#!/usr/bin/env bash
# Checks the project's C# without launching the Unity editor (so it needs no editor licence).
#
#   1. Runs the simulation test suite on the Mono runtime that ships with the editor.
#   2. Type-checks every project assembly against the editor's real UnityEngine/UnityEditor
#      assemblies, plus reference builds of the UI, TextMeshPro and Input System packages
#      compiled from the package sources that ship with the editor.
#
# It does not import assets, compile shaders or build the scene: only the editor can do that.
#
# Usage: Tools/verify_without_unity.sh            (set UNITY_EDITOR_DIR to use another editor)
set -uo pipefail

EDITOR="${UNITY_EDITOR_DIR:-$HOME/Unity/Hub/Editor/6000.6.9f1/Editor}"
D="$EDITOR/Data"
PROJECT="$(cd "$(dirname "$0")/.." && pwd)"
P="$PROJECT/Assets/Phainon"
WORK="${XDG_CACHE_HOME:-$HOME/.cache}/phainon-verify"
mkdir -p "$WORK/refs" "$WORK/src"

[ -d "$D" ] || { echo "Unity editor not found at $EDITOR"; exit 2; }
B="$D/Resources/PackageManager/BuiltInPackages"
export DOTNET_ROOT="$D/DotNetSdk" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
ROSLYN="$(ls -d "$D"/DotNetSdk/sdk/*/Roslyn/bincore/csc.dll | head -1)"
csc() { "$D/DotNetSdk/dotnet" "$ROSLYN" -nologo -nostdlib -langversion:9 "$@"; }

API="$D/UnityReferenceAssemblies/unity-4.8-api"
BASE=()
for f in mscorlib System System.Core System.Xml System.Runtime.Serialization System.Numerics System.Data System.Xml.Linq; do BASE+=("-r:$API/$f.dll"); done
BASE+=("-r:$API/Facades/netstandard.dll" "-r:$API/Facades/System.Runtime.dll")
ENGINE=(); for f in "$D"/Managed/UnityEngine/UnityEngine*.dll; do ENGINE+=("-r:$f"); done
ALL=();    for f in "$D"/Managed/UnityEngine/*.dll; do ALL+=("-r:$f"); done

VERSIONS="UNITY_6000_6_OR_NEWER;UNITY_6000_5_OR_NEWER;UNITY_6000_4_OR_NEWER;UNITY_6000_3_OR_NEWER;UNITY_6000_2_OR_NEWER;UNITY_6000_1_OR_NEWER;UNITY_6000_0_OR_NEWER;UNITY_2023_3_OR_NEWER;UNITY_2023_2_OR_NEWER;UNITY_2023_1_OR_NEWER;UNITY_2022_3_OR_NEWER;UNITY_2022_2_OR_NEWER;UNITY_2022_1_OR_NEWER;UNITY_2021_3_OR_NEWER;UNITY_2021_2_OR_NEWER;UNITY_2021_1_OR_NEWER;UNITY_2020_3_OR_NEWER;UNITY_2020_2_OR_NEWER;UNITY_2020_1_OR_NEWER;UNITY_2019_4_OR_NEWER;UNITY_2019_3_OR_NEWER;UNITY_2019_2_OR_NEWER;UNITY_2019_1_OR_NEWER;UNITY_2018_4_OR_NEWER;UNITY_2018_3_OR_NEWER;UNITY_2018_1_OR_NEWER;UNITY_2017_4_OR_NEWER;UNITY_2017_1_OR_NEWER;UNITY_5_6_OR_NEWER;UNITY_6000"
PLAYER="$VERSIONS;UNITY_STANDALONE;UNITY_STANDALONE_LINUX;ENABLE_INPUT_SYSTEM;NET_STANDARD_2_1;ENABLE_MONO;PACKAGE_PHYSICS;PACKAGE_PHYSICS2D;PACKAGE_ANIMATION;PACKAGE_UITOOLKIT;UNITY_INPUT_SYSTEM_ENABLE_UI;UNITY_INPUT_SYSTEM_ENABLE_PHYSICS;UNITY_INPUT_SYSTEM_ENABLE_PHYSICS2D"
EDITOR_DEFS="$PLAYER;UNITY_EDITOR;UNITY_EDITOR_64;UNITY_EDITOR_LINUX;UNITY_INCLUDE_TESTS"
QUIET="-nowarn:0618,0649,0414,0169,0067,0108,0109,0219,0612,0672,1701,1702,0436,8632,0162,0168,0252,0253,0659,0661,0660,1717,0164,0420,0809,3021,0114,0465,0728"

# ---------------------------------------------------------------- reference assemblies for packages
if [ ! -f "$WORK/refs/Unity.InputSystem.dll" ]; then
  echo "Building package reference assemblies (first run only)..."
  find "$B/com.unity.ugui/Runtime/UGUI" "$B/com.unity.ugui/Runtime/InternalBridge" -name "*.cs" | sed 's/.*/"&"/' > "$WORK/ugui.rsp"
  csc -unsafe $QUIET -target:library -define:"$EDITOR_DEFS" -out:"$WORK/refs/UnityEngine.UI.dll" "${BASE[@]}" "${ALL[@]}" @"$WORK/ugui.rsp" | grep "error" | head -5

  # One TextMeshPro call uses an engine-internal overload that an outside build cannot see; it is
  # stubbed in a private copy of that file. The public API being checked against is unchanged.
  sed 's/GlyphPairAdjustmentRecord\[\] pairAdjustmentRecords = FontEngine.GetGlyphPairAdjustmentRecords(m_GlyphIndexList, out int recordCount);/GlyphPairAdjustmentRecord[] pairAdjustmentRecords = null; int recordCount = 0;/' \
      "$B/com.unity.ugui/Runtime/TMP/TMP_FontAsset.cs" > "$WORK/src/TMP_FontAsset.cs"
  find "$B/com.unity.ugui/Runtime/TMP" -name "*.cs" ! -name "TMP_FontAsset.cs" | sed 's/.*/"&"/' > "$WORK/tmp.rsp"
  echo "\"$WORK/src/TMP_FontAsset.cs\"" >> "$WORK/tmp.rsp"
  csc -unsafe $QUIET -target:library -define:"$EDITOR_DEFS" -out:"$WORK/refs/Unity.TextMeshPro.dll" "${BASE[@]}" "${ALL[@]}" -r:"$WORK/refs/UnityEngine.UI.dll" @"$WORK/tmp.rsp" | grep "error" | head -5

  rm -rf "$WORK/src/inputsystem" && mkdir -p "$WORK/src/inputsystem"
  tar -xzf "$(ls "$D"/Resources/PackageManager/Editor/com.unity.inputsystem-*.tgz | head -1)" -C "$WORK/src/inputsystem"
  find "$WORK/src/inputsystem/package/InputSystem" -name "*.cs" -not -path "*/Editor/*" -not -path "*/InputForUI/*" | sed 's/.*/"&"/' > "$WORK/input.rsp"
  csc -unsafe $QUIET -target:library -define:"$PLAYER" -out:"$WORK/refs/Unity.InputSystem.dll" "${BASE[@]}" "${ALL[@]}" -r:"$WORK/refs/UnityEngine.UI.dll" @"$WORK/input.rsp" | grep "error" | head -5
fi
for f in UnityEngine.UI Unity.TextMeshPro Unity.InputSystem; do
  [ -f "$WORK/refs/$f.dll" ] || { echo "Could not build reference assembly $f.dll"; exit 2; }
done
PKG=("-r:$WORK/refs/UnityEngine.UI.dll" "-r:$WORK/refs/Unity.TextMeshPro.dll" "-r:$WORK/refs/Unity.InputSystem.dll")

# ---------------------------------------------------------------- 1. simulation tests
status=0
NUNIT="$B/com.unity.ext.nunit/net472/unity-custom/nunit.framework.dll"
MONO="$D/MonoBleedingEdge/bin-linux64/mono"; [ -x "$MONO" ] || MONO="$D/MonoBleedingEdge/bin/mono"
MONOAPI="$D/MonoBleedingEdge/lib/mono/4.7.1-api"
cat > "$WORK/src/Runner.cs" <<'CS'
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
public static class Runner
{
    public static int Main()
    {
        int passed = 0, failed = 0;
        foreach (Type type in typeof(Phainon.Tests.MissionTests).Assembly.GetTypes())
        foreach (MethodInfo method in type.GetMethods().Where(m => m.GetCustomAttributes(typeof(TestAttribute), false).Length > 0))
        {
            try { method.Invoke(Activator.CreateInstance(type), null); passed++; Console.WriteLine("  PASS  " + method.Name); }
            catch (TargetInvocationException e) { failed++; Console.WriteLine("  FAIL  " + method.Name + "\n        " + e.InnerException.Message.Trim().Replace("\n", "\n        ")); }
        }
        Console.WriteLine("  " + passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
CS
echo "== Simulation tests"
cp -f "$NUNIT" "$WORK/"
if csc -target:exe -out:"$WORK/simtests.exe" -r:"$MONOAPI/mscorlib.dll" -r:"$MONOAPI/System.dll" -r:"$MONOAPI/System.Core.dll" -r:"$NUNIT" \
     $(find "$P/Scripts/Simulation" "$P/Tests/EditMode" -name "*.cs") "$WORK/src/Runner.cs"; then
  (cd "$WORK" && MONO_PATH="$D/MonoBleedingEdge/lib/mono/4.5" "$MONO" simtests.exe) || status=1
else
  status=1
fi

# ---------------------------------------------------------------- 2. type check
check() { # name, then compiler arguments
  local name="$1"; shift
  local out; out="$(csc -target:library "$@" 2>&1 | grep -E "error|warning" | sed "s|$PROJECT/||" | sort -u)"
  if echo "$out" | grep -q "error"; then echo "== $name: ERRORS"; echo "$out" | grep error | head -40; status=1
  else echo "== $name: ok ($(echo "$out" | grep -c warning) warnings)"; fi
}
check "Phainon.Simulation" -out:"$WORK/refs/Phainon.Simulation.dll" "${BASE[@]}" $(find "$P/Scripts/Simulation" -name "*.cs")
SIM="-r:$WORK/refs/Phainon.Simulation.dll"
check "Phainon.Runtime (player)" -define:"$PLAYER" -out:"$WORK/refs/Phainon.Runtime.player.dll" "${BASE[@]}" "${ENGINE[@]}" "${PKG[@]}" "$SIM" $(find "$P/Scripts/Runtime" -name "*.cs")
check "Phainon.Runtime (editor)" -define:"$EDITOR_DEFS" -out:"$WORK/refs/Phainon.Runtime.dll" "${BASE[@]}" "${ALL[@]}" "${PKG[@]}" "$SIM" $(find "$P/Scripts/Runtime" -name "*.cs")
check "Phainon.Editor" -define:"$EDITOR_DEFS" -out:"$WORK/refs/Phainon.Editor.dll" "${BASE[@]}" "${ALL[@]}" "${PKG[@]}" "$SIM" -r:"$WORK/refs/Phainon.Runtime.dll" $(find "$P/Scripts/Editor" -name "*.cs")

# Unity only links a component or asset class to its script when the file is named after the class.
for f in $(find "$P/Scripts/Runtime" -name "*.cs"); do
  for c in $(grep -oE "class \w+ : (MonoBehaviour|ScriptableObject|CockpitDisplay|Interactable|MaskableGraphic)" "$f" | awk '{print $2}'); do
    [ "$c" = "$(basename "$f" .cs)" ] || { echo "== File naming: $c must live in $c.cs (found in ${f#$PROJECT/})"; status=1; }
  done
done

[ $status -eq 0 ] && echo "All checks passed." || echo "Some checks FAILED."
exit $status
