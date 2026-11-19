from __future__ import annotations

import hashlib
import pathlib
import zipfile


source_apk = pathlib.Path(
    "dist/影之刃1_64位修复版_密令次数调整_v57.apk"
)
output_apk = pathlib.Path(
    "dist/影之刃1_64位修复版_普通关卡停止装备掉落_v60-unsigned.apk"
)
assembly = pathlib.Path(
    "tmp/assembly-normal-no-equipment-v60.dll"
)

replacements = {
    "assets/bin/Data/Managed/Assembly-CSharp.dll":
        bytes(byte ^ 0xFF for byte in assembly.read_bytes()),
}

with zipfile.ZipFile(source_apk) as source:
    with zipfile.ZipFile(output_apk, "w") as output:
        for info in source.infolist():
            output.writestr(
                info,
                replacements.get(info.filename, source.read(info)),
            )

digest = hashlib.sha256(output_apk.read_bytes()).hexdigest()
print(f"Wrote {output_apk} ({output_apk.stat().st_size} bytes)")
print(f"SHA256 {digest}")
