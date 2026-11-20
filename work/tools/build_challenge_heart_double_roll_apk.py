from __future__ import annotations

import hashlib
import pathlib
import zipfile


source_apk = pathlib.Path(
    "dist/影之刃1_64位修复版_会员刷新次数调整_v64.apk"
)
output_apk = pathlib.Path(
    "dist/影之刃1_64位修复版_挑战心法双抽随机_v65-unsigned.apk"
)
assembly = pathlib.Path(
    "tmp/assembly-challenge-heart-v65.dll"
)
assembly_entry = "assets/bin/Data/Managed/Assembly-CSharp.dll"

replacements = {
    assembly_entry: bytes(byte ^ 0xFF for byte in assembly.read_bytes()),
}
signature_entries = {
    "META-INF/CAMPAIGN.SF",
    "META-INF/CAMPAIGN.RSA",
    "META-INF/MANIFEST.MF",
}

with zipfile.ZipFile(source_apk) as source:
    with zipfile.ZipFile(output_apk, "w") as output:
        for info in source.infolist():
            if info.filename in signature_entries:
                continue
            output.writestr(
                info,
                replacements.get(
                    info.filename,
                    source.read(info),
                ),
            )

digest = hashlib.sha256(output_apk.read_bytes()).hexdigest()
print(f"Wrote {output_apk} ({output_apk.stat().st_size} bytes)")
print(f"SHA256 {digest}")
