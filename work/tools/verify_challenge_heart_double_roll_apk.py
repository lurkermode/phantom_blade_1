from __future__ import annotations

import hashlib
import pathlib
import zipfile


source_apk = pathlib.Path(
    "dist/影之刃1_64位修复版_会员刷新次数调整_v64.apk"
)
output_apk = pathlib.Path(
    "dist/影之刃1_64位修复版_挑战心法双抽随机_v65.apk"
)
expected_assembly = pathlib.Path(
    "tmp/assembly-challenge-heart-v65.dll"
)
assembly_entry = "assets/bin/Data/Managed/Assembly-CSharp.dll"
signature_entries = {
    "META-INF/CAMPAIGN.SF",
    "META-INF/CAMPAIGN.RSA",
    "META-INF/MANIFEST.MF",
}


with zipfile.ZipFile(source_apk) as source:
    with zipfile.ZipFile(output_apk) as output:
        if source.testzip() is not None:
            raise ValueError("Source APK has a damaged ZIP entry.")
        if output.testzip() is not None:
            raise ValueError("Output APK has a damaged ZIP entry.")

        patched_assembly = bytes(
            byte ^ 0xFF
            for byte in output.read(assembly_entry)
        )
        if patched_assembly != expected_assembly.read_bytes():
            raise ValueError("Patched assembly does not match the APK.")

        for info in source.infolist():
            if info.filename in signature_entries:
                continue
            if info.filename == assembly_entry:
                continue
            if source.read(info.filename) != output.read(info.filename):
                raise ValueError(
                    f"Unexpected APK change: {info.filename}"
                )

        required_entries = set(signature_entries) | {assembly_entry}
        missing = sorted(
            required_entries - set(output.namelist())
        )
        if missing:
            raise ValueError(f"Missing APK entries: {missing}")

digest = hashlib.sha256(output_apk.read_bytes()).hexdigest()
print(f"Verified {output_apk}")
print(f"SHA256 {digest}")
