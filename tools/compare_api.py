"""Compare the declarations + resources of two HSPENeo.dll builds with ildasm.

Usage: python compare_api.py [original.dll] [fork.dll]
Every type and its fields, method signatures, properties and events (private ones too, not method bodies) are
compared per type, independent of the order the compiler emitted them. Reports members REMOVED from the original
(breaking for anything that uses them) and members ADDED by the fork. Also checks PoseController._modules (VNGE
reads it by reflection) and the manifest resources. Compiler-generated names are ignored.
Exit code 0 = nothing removed and resources identical, 1 otherwise.
Note: ildasm's /NOIL switch produces an empty file for these assemblies, so a full dump is parsed instead.
"""
import os
import re
import subprocess
import sys
import tempfile

ILDASM = r"C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\ildasm.exe"
A = sys.argv[1] if len(sys.argv) > 1 else r"D:\HS\PluginDev\HSPE\_original\HSPENeo.dll"
B = sys.argv[2] if len(sys.argv) > 2 else r"D:\HS\PluginDev\HSPE\bin\Release\HSPENeo.dll"

GENERATED = re.compile(r"'?<[^>']*>[a-zA-Z]__[\w`]*'?|'?<>[a-zA-Z0-9_]+__[\w`]*'?|<PrivateImplementationDetails>[^\s]*|__StaticArrayInitTypeSize=\d+|CS\$<>[\w]+|'<>c'|'<>9'")
MEMBER = re.compile(r"^\.(field|method|property|event)\b")
# Differences checked by hand and accepted (Joan's repo has a newer ToolBox_Shared than the installed 2.12.0.2 build):
# GenericPlugin.Name went from abstract to virtual-with-default - same member, nothing that calls it breaks.
ACCEPTED_REMOVED = {
    "ToolBox.GenericPlugin :: .method public hidebysig newslot specialname abstract virtual instance string get_Name() cil managed",
}
# MainWindow's small-window state moved to Kit.Small.PoseInput (MainWindow._input) and a few
# fields went private -> internal for the kit Options popover. All PRIVATE, so not part of the drop-in contract; a
# string scan of every installed plugin DLL and VNGE .py found no reference to any of them.
_PHASE2_MAINWINDOW = [
    "bool _currentModeIK", "bool _positionOperationWorld", "bool _shortcutRegisterMode",
    "bool _xMove", "bool _xRot", "bool _yMove", "bool _yRot", "bool _zMove", "bool _zRot",
    "class [UnityEngine.UI]UnityEngine.UI.Button _shortcutKeyButton",
    "class [UnityEngine.UI]UnityEngine.UI.Toggle _anklesCorrectionByDefaultToggle",
    "class [UnityEngine.UI]UnityEngine.UI.Toggle _crotchCorrectionByDefaultToggle",
    "float32 _intensityValue",
    "initonly class [mscorlib]System.Collections.Generic.List`1<valuetype ['Assembly-CSharp-firstpass']RootMotion.FinalIK.FullBodyBipedChain> _ikBendGoalTargets",
    "initonly class [mscorlib]System.Collections.Generic.List`1<valuetype ['Assembly-CSharp-firstpass']RootMotion.FinalIK.FullBodyBipedEffector> _ikBoneTargets",
    "initonly valuetype [UnityEngine]UnityEngine.Quaternion[] _lastIKBonesRotations",
    "initonly valuetype [UnityEngine]UnityEngine.Vector3[] _lastIKBendGoalsPositions",
    "initonly valuetype [UnityEngine]UnityEngine.Vector3[] _lastIKBonesPositions",
    "valuetype [UnityEngine]UnityEngine.KeyCode _mainWindowKeyCode",
    "valuetype [UnityEngine]UnityEngine.Quaternion _lastFKBonesRotation",
    "valuetype [UnityEngine]UnityEngine.Vector2 _delta",
]
ACCEPTED_REMOVED.update("HSPE.MainWindow :: .field private " + f for f in _PHASE2_MAINWINDOW)


def dump(path):
    fd, out = tempfile.mkstemp(suffix=".il")
    os.close(fd)
    subprocess.run([ILDASM, path, "/OUT=" + out], check=True, capture_output=True)
    with open(out, encoding="utf-8", errors="replace") as f:
        text = f.read()
    base = os.path.splitext(out)[0]
    for ext in (".il", ".res"):
        try:
            os.remove(base + ext)
        except OSError:
            pass
    return text


def clean(s):
    s = re.sub(r"//.*$", "", s)
    s = GENERATED.sub("<gen>", s)
    return re.sub(r"\s+", " ", s).strip()


def members(text):
    """{type path: set(member declarations)} - nesting tracked with a brace stack."""
    lines = text.splitlines()
    result = {}
    stack = []          # (type name, brace depth at which it opened)
    depth = 0
    i = 0
    pending_class = None
    while i < len(lines):
        s = lines[i].strip()
        if s.startswith(".class"):
            header = [s]
            while i + 1 < len(lines) and lines[i + 1].strip() != "{" and not lines[i + 1].strip().startswith("{"):
                i += 1
                header.append(lines[i].strip())
            h = clean(" ".join(header))
            name = re.sub(r"\s+(extends|implements)\b.*$", "", h).split(" ")[-1]
            pending_class = name
            i += 1
            continue
        if s.startswith(".method") or MEMBER.match(s):
            sig = [s]
            if s.startswith(".method"):
                while i + 1 < len(lines) and lines[i + 1].strip() != "{":
                    i += 1
                    sig.append(lines[i].strip())
            owner = "/".join(n for n, _ in stack) or "<module>"
            if "<gen>" not in owner:
                result.setdefault(owner, set()).add(clean(" ".join(sig)))
            i += 1
            continue
        opens = s.count("{")
        closes = s.count("}")
        if opens and pending_class is not None:
            depth += 1
            stack.append((pending_class, depth))
            result.setdefault("/".join(n for n, _ in stack), set())
            pending_class = None
            opens -= 1
        depth += opens
        for _ in range(closes):
            if stack and stack[-1][1] == depth:
                stack.pop()
            depth -= 1
        i += 1
    return {k: v for k, v in result.items() if "<gen>" not in k}


def main():
    ta, tb = dump(A), dump(B)
    ma, mb = members(ta), members(tb)
    removed, added = [], []
    for t in sorted(set(ma) | set(mb)):
        if t not in mb:
            removed.append("TYPE " + t)
            continue
        if t not in ma:
            added.append("TYPE " + t)
            continue
        for m in sorted(ma[t] - mb[t]):
            if (t + " :: " + m) not in ACCEPTED_REMOVED:
                removed.append(t + " :: " + m)
        for m in sorted(mb[t] - ma[t]):
            added.append(t + " :: " + m)
    print("types: original %d, fork %d" % (len(ma), len(mb)))
    print("REMOVED (%d):" % len(removed))
    for r in removed:
        print("  - " + r)
    print("ADDED (%d):" % len(added))
    for a in added[:80]:
        print("  + " + a)
    if len(added) > 80:
        print("  ... %d more" % (len(added) - 80))
    ok = not removed
    if re.search(r"\.field (private|family) (initonly )?class \[mscorlib\]System\.Collections\.Generic\.List`1<class HSPE\.AMModules\.AdvancedModeModule> _modules", tb):
        print("_modules field present")
    else:
        ok = False
        print("MISSING: PoseController._modules")
    ra = sorted(set(re.findall(r"\.mresource public ([^\s]+)", ta)))
    rb = sorted(set(re.findall(r"\.mresource public ([^\s]+)", tb)))
    print("resources:", "identical" if ra == rb else ("DIFFERENT", ra, rb))
    ok = ok and ra == rb
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
