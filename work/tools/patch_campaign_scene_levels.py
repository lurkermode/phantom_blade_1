from __future__ import annotations

import argparse
import pathlib
import shutil
import struct
import sys
import tempfile


TOOL_DIR = pathlib.Path(r"E:\AI Games\tools\unitypy")
if str(TOOL_DIR) not in sys.path:
    sys.path.insert(0, str(TOOL_DIR))

import UnityPy


TARGET_LEVELS = {
    10204: 1,
    61503: 63,
}


class Reader:
    def __init__(self, data: bytearray) -> None:
        self.data = data
        self.offset = 0

    def read(self, size: int) -> bytes:
        start = self.offset
        self.offset += size
        if self.offset > len(self.data):
            raise EOFError(
                f"Wanted {size} bytes at {start}, size={len(self.data)}"
            )
        return bytes(self.data[start : self.offset])

    def int32(self) -> int:
        return struct.unpack("<i", self.read(4))[0]

    def byte(self) -> int:
        return self.read(1)[0]

    def align(self) -> None:
        self.offset = (self.offset + 3) // 4 * 4

    def string(self) -> str:
        size = self.int32()
        value = self.read(size).decode("utf-8")
        self.align()
        return value

    def pptr(self) -> None:
        self.int32()
        self.int32()

    def ints(self) -> None:
        for _ in range(self.int32()):
            self.int32()

    def strings(self) -> None:
        for _ in range(self.int32()):
            self.string()


def skip_main_city(reader: Reader) -> None:
    reader.int32()
    reader.string()
    reader.string()
    reader.string()
    for _ in range(4):
        reader.int32()
    reader.ints()
    reader.strings()


def read_battle(
    reader: Reader,
    patch: dict[int, int] | None = None,
) -> tuple[int, int]:
    scene_id = reader.int32()
    reader.string()
    reader.string()
    reader.string()
    min_level_offset = reader.offset
    min_level = reader.int32()
    if patch is not None and scene_id in patch:
        min_level = patch[scene_id]
        struct.pack_into("<i", reader.data, min_level_offset, min_level)

    for _ in range(3):
        reader.int32()
    reader.ints()
    for _ in range(19):
        reader.int32()
    reader.ints()
    reader.byte()
    reader.align()
    reader.byte()
    reader.align()
    reader.int32()
    return scene_id, min_level


def load_scene_data(bundle_path: pathlib.Path):
    environment = UnityPy.load(str(bundle_path))
    obj = next(
        item
        for item in environment.objects
        if item.type.name == "MonoBehaviour"
    )
    return environment, obj


def patch_bundle(
    input_path: pathlib.Path,
    output_path: pathlib.Path,
) -> None:
    environment, obj = load_scene_data(input_path)
    raw = bytearray(obj.get_raw_data())
    reader = Reader(raw)
    reader.pptr()
    reader.byte()
    reader.align()
    reader.pptr()
    root_name = reader.string()
    if root_name != "SceneData":
        raise ValueError(f"Unexpected scene data root: {root_name!r}")

    for _ in range(reader.int32()):
        skip_main_city(reader)

    battle_count = reader.int32()
    found = {}
    for _ in range(battle_count):
        scene_id, min_level = read_battle(reader, TARGET_LEVELS)
        if scene_id in TARGET_LEVELS:
            found[scene_id] = min_level

    missing = sorted(set(TARGET_LEVELS) - set(found))
    if missing:
        raise ValueError(f"Scene records were not found: {missing}")

    obj.set_raw_data(bytes(raw))
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(
        dir=str(output_path.parent)
    ) as temporary_dir:
        environment.save(pack="original", out_path=temporary_dir)
        produced = pathlib.Path(temporary_dir) / input_path.name
        if not produced.is_file():
            raise FileNotFoundError(
                f"UnityPy did not produce {produced.name}"
            )
        shutil.copyfile(produced, output_path)


def verify_bundle(bundle_path: pathlib.Path) -> dict[int, int]:
    _, obj = load_scene_data(bundle_path)
    reader = Reader(bytearray(obj.get_raw_data()))
    reader.pptr()
    reader.byte()
    reader.align()
    reader.pptr()
    reader.string()
    for _ in range(reader.int32()):
        skip_main_city(reader)

    found = {}
    for _ in range(reader.int32()):
        scene_id, min_level = read_battle(reader)
        if scene_id in TARGET_LEVELS:
            found[scene_id] = min_level
    return found


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()

    patch_bundle(args.input, args.output)
    actual = verify_bundle(args.output)
    if actual != TARGET_LEVELS:
        raise ValueError(
            f"Unexpected patched levels: {actual}"
        )
    print(
        "Patched SceneData levels: "
        + ", ".join(
            f"{scene_id}={level}"
            for scene_id, level in sorted(actual.items())
        )
    )


if __name__ == "__main__":
    main()
