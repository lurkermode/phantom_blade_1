from __future__ import annotations

import pathlib
import zipfile


apk_path = pathlib.Path(
    "dist/影之刃1_64位修复版_普通关卡停止装备掉落_v60.apk"
)
assembly_path = pathlib.Path(
    "tmp/assembly-normal-no-equipment-v60.dll"
)
assembly_entry = "assets/bin/Data/Managed/Assembly-CSharp.dll"

with zipfile.ZipFile(apk_path) as archive:
    protected = archive.read(assembly_entry)

decoded = bytes(byte ^ 0xFF for byte in protected)
if decoded != assembly_path.read_bytes():
    raise ValueError("APK assembly does not match the patched DLL")

print(f"assembly_matches=True")
print(f"apk_size={apk_path.stat().st_size}")
