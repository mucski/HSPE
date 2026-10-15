"""Compare the HSPE block of two Studio scene files.

Usage: python hspe_scene_diff.py A.png B.png
HSExtSave appends <sceneExtData>...</sceneExtData> (UTF-8 XML) to the scene file; HSPE's data is its <hspe> child.
Instance ids change every session, so uniqueId / poseControllerId attributes are normalized before comparing.
Exit code 0 = identical, 1 = different, 2 = block missing.
"""
import re
import sys


def hspe_block(path):
    data = open(path, "rb").read()
    start = data.rfind(b"<sceneExtData>")
    if start < 0:
        return None
    end = data.find(b"</sceneExtData>", start)
    ext = data[start:end]
    a = ext.find(b"<hspe>")
    b = ext.find(b"</hspe>")
    if a < 0 or b < 0:
        return None
    return ext[a:b + len(b"</hspe>")].decode("utf-8")


def normalize(xml):
    xml = re.sub(r'uniqueId="-?\d+"', 'uniqueId="#"', xml)
    return re.sub(r'poseControllerId="-?\d+"', 'poseControllerId="#"', xml)


def first_difference(a, b):
    for k in range(min(len(a), len(b))):
        if a[k] != b[k]:
            return k
    return min(len(a), len(b))


def main():
    paths = sys.argv[1:3]
    blocks = [hspe_block(p) for p in paths]
    missing = [p for p, blk in zip(paths, blocks) if blk is None]
    if missing:
        print("hspe block missing in:", missing)
        return 2
    a, b = (normalize(x) for x in blocks)
    if a == b:
        print("identical (%d chars)" % len(a))
        return 0
    i = first_difference(a, b)
    print("DIFFERENT at char %d (lengths %d / %d)" % (i, len(a), len(b)))
    print("A:", a[max(0, i - 120):i + 120])
    print("B:", b[max(0, i - 120):i + 120])
    return 1


if __name__ == "__main__":
    sys.exit(main())
