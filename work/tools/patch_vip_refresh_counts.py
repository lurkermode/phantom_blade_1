from __future__ import annotations

import argparse
import copy
import pathlib
import shutil
import sys
import tempfile


UNITYPY_DIR = pathlib.Path(r"E:\AI Games\tools\unitypy")
if str(UNITYPY_DIR) not in sys.path:
    sys.path.insert(0, str(UNITYPY_DIR))

import UnityPy  # noqa: E402
from UnityPy.helpers import TypeTreeHelper  # noqa: E402
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator  # noqa: E402


PROJECT_DIR = pathlib.Path(__file__).resolve().parents[2]
TARGET_VIP_LEVEL = 16
TARGET_REFRESH_COUNTS = {
    100: 20,   # 招财令
    200: 20,   # 经验书房
    500: 20,   # 宝石猎场
    600: 20,   # 星辰之力
    9999: 20,  # 挑战
}
UNCHANGED_REFRESH_COUNTS = {
    3: 7,      # 论剑
}
EXPECTED_SOURCE_COUNTS = {
    100: 8,
    200: 8,
    500: 8,
    600: 8,
    3: 7,
    9999: 5,
}


def load_bundle(bundle_path: pathlib.Path):
    TypeTreeHelper.read_typetree_boost = None
    generator = TypeTreeGenerator("4.7.2f1")
    for dll_path in (PROJECT_DIR / "tmp" / "managed").glob("*.dll"):
        try:
            generator.load_dll(dll_path.read_bytes())
        except Exception:
            pass

    environment = UnityPy.load(str(bundle_path))
    environment.typetree_generator = generator
    data = next(
        obj
        for obj in environment.objects
        if obj.type.name == "MonoBehaviour"
    )
    return environment, data


def find_vip_level(tree: dict, vip_level: int) -> dict:
    matches = [
        bonus
        for bonus in tree["bonusData"]
        if bonus["viplevel"] == vip_level
    ]
    if len(matches) != 1:
        raise ValueError(
            f"Expected one VIP level {vip_level}, found {len(matches)}"
        )
    return matches[0]


def refresh_counts(vip: dict) -> dict[int, int]:
    counts: dict[int, int] = {}
    for refresh in vip["dungeonRefresh"]:
        event_id = refresh["eventID"]
        if event_id in counts:
            raise ValueError(f"Duplicate refresh event ID {event_id}")
        counts[event_id] = refresh["refreshTime"]
    return counts


def validate_counts(vip: dict, expected: dict[int, int]) -> None:
    actual = refresh_counts(vip)
    for event_id, count in expected.items():
        if actual.get(event_id) != count:
            raise ValueError(
                f"Unexpected refresh count for event {event_id}: "
                f"expected {count}, found {actual.get(event_id)}"
            )


def patch_tree(tree: dict) -> dict:
    patched = copy.deepcopy(tree)
    vip = find_vip_level(patched, TARGET_VIP_LEVEL)
    validate_counts(vip, EXPECTED_SOURCE_COUNTS)

    for refresh in vip["dungeonRefresh"]:
        event_id = refresh["eventID"]
        if event_id in TARGET_REFRESH_COUNTS:
            refresh["refreshTime"] = TARGET_REFRESH_COUNTS[event_id]

    validate_counts(vip, TARGET_REFRESH_COUNTS)
    validate_counts(vip, UNCHANGED_REFRESH_COUNTS)
    return patched


def patch_bundle(
    input_path: pathlib.Path,
    output_path: pathlib.Path,
) -> None:
    environment, data = load_bundle(input_path)
    source_tree = data.read_typetree()
    expected_tree = patch_tree(source_tree)

    data.save_typetree(expected_tree)
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

    _, verified_data = load_bundle(output_path)
    actual_tree = verified_data.read_typetree()
    if actual_tree != expected_tree:
        raise ValueError(
            "The saved VIP data differs from the expected patched data"
        )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    args = parser.parse_args()

    patch_bundle(args.input, args.output)
    print(
        f"Patched VIP{TARGET_VIP_LEVEL}: "
        "招财令/经验房/宝石猎场/星辰之力/挑战 = 20, 论剑 = 7"
    )
    print(f"Wrote {args.output}")


if __name__ == "__main__":
    main()
