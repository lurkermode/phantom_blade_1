from __future__ import annotations

import argparse
import base64
import hashlib
import os
import pathlib
import struct
import tempfile
import zipfile

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding
from cryptography.hazmat.primitives.serialization import pkcs12


MANIFEST_NAME = "META-INF/MANIFEST.MF"
BLOCK_NAME = "META-INF/CAMPAIGN.SF"
SIGNATURE_NAME = "META-INF/CAMPAIGN.RSA"
SIGNATURE_NAMES = {MANIFEST_NAME, BLOCK_NAME, SIGNATURE_NAME}


def der_length(length: int) -> bytes:
    if length < 0x80:
        return bytes((length,))
    encoded = length.to_bytes((length.bit_length() + 7) // 8, "big")
    return bytes((0x80 | len(encoded),)) + encoded


def der(tag: int, content: bytes) -> bytes:
    return bytes((tag,)) + der_length(len(content)) + content


def der_sequence(*values: bytes) -> bytes:
    return der(0x30, b"".join(values))


def der_set(*values: bytes) -> bytes:
    return der(0x31, b"".join(values))


def der_integer(value: int) -> bytes:
    encoded = value.to_bytes(max(1, (value.bit_length() + 7) // 8), "big")
    if encoded[0] & 0x80:
        encoded = b"\x00" + encoded
    return der(0x02, encoded)


def der_oid(value: str) -> bytes:
    parts = [int(part) for part in value.split(".")]
    encoded = bytearray((parts[0] * 40 + parts[1],))
    for part in parts[2:]:
        chunk = bytearray((part & 0x7F,))
        part >>= 7
        while part:
            chunk.append(0x80 | (part & 0x7F))
            part >>= 7
        encoded.extend(reversed(chunk))
    return der(0x06, bytes(encoded))


def der_null() -> bytes:
    return b"\x05\x00"


def manifest_line(line: str) -> bytes:
    raw = line.encode("utf-8")
    if len(raw) <= 70:
        return raw + b"\r\n"
    chunks = [raw[:70]]
    raw = raw[70:]
    while raw:
        chunks.append(b" " + raw[:69])
        raw = raw[69:]
    return b"\r\n".join(chunks) + b"\r\n"


def digest_entry(name: str, data: bytes) -> tuple[bytes, bytes]:
    digest = base64.b64encode(hashlib.sha1(data).digest()).decode("ascii")
    section = (
        manifest_line("Name: " + name)
        + manifest_line("SHA1-Digest: " + digest)
        + b"\r\n"
    )
    return section, manifest_line("SHA1-Digest: " + digest) + b"\r\n"


def build_pkcs7(
    signature: bytes,
    certificate: object,
    issuer_der: bytes,
    serial_number: int,
) -> bytes:
    certificates = der(0xA0, certificate)
    signer_info = der_sequence(
        der_integer(1),
        der_sequence(issuer_der, der_integer(serial_number)),
        der_sequence(der_oid("1.3.14.3.2.26"), der_null()),
        der_sequence(
            der_oid("1.2.840.113549.1.1.1"),
            der_null(),
        ),
        der(0x04, signature),
    )
    signed_data = der_sequence(
        der_integer(1),
        der_set(der_sequence(der_oid("1.3.14.3.2.26"), der_null())),
        der_sequence(der_oid("1.2.840.113549.1.7.1")),
        certificates,
        der_set(signer_info),
    )
    return der_sequence(
        der_oid("1.2.840.113549.1.7.2"),
        der(0xA0, signed_data),
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument("--keystore", type=pathlib.Path)
    parser.add_argument("--password")
    args = parser.parse_args()

    keystore = args.keystore or pathlib.Path(
        "tmp/campaignsync-test.jks"
    )
    password_text = args.password or os.environ.get(
        "CAMPAIGN_KEY_PASSWORD"
    )
    if not password_text:
        raise ValueError(
            "Pass --password or set CAMPAIGN_KEY_PASSWORD."
        )
    password = password_text.encode("utf-8")
    private_key, certificate, chain = pkcs12.load_key_and_certificates(
        keystore.read_bytes(),
        password,
    )
    if private_key is None or certificate is None:
        raise ValueError("PKCS#12 key or certificate was not found.")

    with zipfile.ZipFile(args.input) as source:
        entries = [
            info
            for info in source.infolist()
            if not info.is_dir() and info.filename not in SIGNATURE_NAMES
        ]
        manifest = bytearray(
            manifest_line("Manifest-Version: 1.0")
            + manifest_line("Created-By: 1.0 (Android)")
            + b"\r\n"
        )
        sf_entries = bytearray()
        for info in sorted(entries, key=lambda item: item.filename):
            manifest_section, sf_section = digest_entry(
                info.filename,
                source.read(info),
            )
            manifest.extend(manifest_section)
            sf_entries.extend(
                manifest_line("Name: " + info.filename)
                + sf_section
                + b"\r\n"
            )

        block = (
            manifest_line("Signature-Version: 1.0")
            + manifest_line("Created-By: 1.0 (Android)")
            + manifest_line(
                "SHA1-Digest-Manifest: "
                + base64.b64encode(
                    hashlib.sha1(manifest).digest()
                ).decode("ascii")
            )
            + b"\r\n"
            + bytes(sf_entries)
        )
        signature = private_key.sign(
            block,
            padding.PKCS1v15(),
            hashes.SHA1(),
        )
        signature_block = build_pkcs7(
            signature,
            certificate.public_bytes(serialization.Encoding.DER),
            certificate.issuer.public_bytes(),
            certificate.serial_number,
        )

        args.output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(
            dir=args.output.parent,
            suffix=".apk",
            delete=False,
        ) as temporary:
            temporary_path = pathlib.Path(temporary.name)

        try:
            with zipfile.ZipFile(temporary_path, "w") as output:
                for name, data in (
                    (MANIFEST_NAME, bytes(manifest)),
                    (BLOCK_NAME, block),
                    (SIGNATURE_NAME, signature_block),
                ):
                    output.writestr(name, data)
                for info in entries:
                    output.writestr(info, source.read(info))
            os.replace(temporary_path, args.output)
        finally:
            if temporary_path.exists():
                temporary_path.unlink()

    print(
        f"Signed {args.output} with APK/JAR SHA1 v1 signature "
        f"({args.output.stat().st_size} bytes)"
    )


if __name__ == "__main__":
    main()
