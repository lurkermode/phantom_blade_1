from __future__ import annotations

import hashlib
import pathlib
import zipfile


source_apk = pathlib.Path(
    "dist/影之刃1_64位修复版_战力任务修复_v63.apk"
)
replacement = pathlib.Path(
    "tmp/VIPBonusData-vip-refresh-v64.unity3d"
)
output_apk = pathlib.Path(
    "dist/影之刃1_64位修复版_会员刷新次数调整_v64-unsigned.apk"
)

entry = "assets/VIPBonusData.unity3d"
payload = replacement.read_bytes()

with zipfile.ZipFile(source_apk) as source:
    with zipfile.ZipFile(output_apk, "w") as output:
        for info in source.infolist():
            output.writestr(
                info,
                payload if info.filename == entry else source.read(info),
            )

digest = hashlib.sha256(output_apk.read_bytes()).hexdigest()
print(f"Wrote {output_apk} ({output_apk.stat().st_size} bytes)")
print(f"SHA256 {digest}")
