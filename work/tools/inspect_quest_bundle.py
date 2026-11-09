import json
import os
import sys

TOOL_DIR = r"E:\AI Games\tools\unitypy"
if TOOL_DIR not in sys.path:
    sys.path.insert(0, TOOL_DIR)

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator


def object_name(obj):
    try:
        data = obj.read()
        for attr in ("m_Name", "name"):
            value = getattr(data, attr, None)
            if isinstance(value, str):
                return value
    except Exception:
        pass
    return ""


def main():
    bundle_path = sys.argv[1]
    managed_dir = sys.argv[2]
    output_path = sys.argv[3]
    env = UnityPy.load(bundle_path)
    serialized_file = env.assets[0]
    unity_version = serialized_file.unity_version.strip().splitlines()[0]
    generator = TypeTreeGenerator(unity_version)
    generator.load_local_dll_folder(managed_dir)
    env.typetree_generator = generator
    output = []

    for obj in env.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        name = object_name(obj)
        entry = {
            "path_id": obj.path_id,
            "name": name,
        }
        try:
            entry["typetree"] = obj.read_typetree()
        except Exception as exc:
            entry["error"] = repr(exc)
        output.append(entry)

    with open(output_path, "w", encoding="utf-8") as handle:
        json.dump(output, handle, ensure_ascii=False, indent=2)
    print("objects={0} after={1}".format(
        len(output),
        os.path.getsize(output_path),
    ))


if __name__ == "__main__":
    main()
