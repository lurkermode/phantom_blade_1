from __future__ import annotations

import argparse
import pathlib
import shutil
import sys
import tempfile


DEFAULT_UNITYPY_DIR = pathlib.Path(r"E:\AI Games\tools\unitypy")
GRANNY_POUCH_ID = 910080
NEW_INDEX_START = 230
NEW_EXCHANGE_LIMIT = 99
TOP_DIRECT_ITEMS = (220081, 220071)
TOP_DIRECT_LIMITS = {
    220081: 1,
    220071: 1,
}

FIVE_STAR_FRAGMENT_IDS = {
    920025,
    920026,
    920027,
    920028,
    920029,
    920030,
    920031,
    920032,
    920034,
    920035,
    920036,
    920037,
    920038,
    920039,
    920040,
    920041,
    920042,
    920043,
    920044,
    920045,
}

SIX_STAR_FRAGMENT_IDS = {
    920046,
    920047,
    920048,
    920049,
    920050,
    920051,
    920054,
    920057,
    920058,
    920059,
    920060,
    920061,
    920062,
    920063,
    920065,
    920066,
    920067,
}

ONE_POUCH_ADD_FRAGMENTS = (
    (920042, "绝世之机关术"),
    (920043, "天岭护体"),
    (920039, "花钱如流水！"),
    (920025, "佛魔断业杀"),
    (920041, "万剑归零"),
    (920044, "云落九天"),
    (920045, "千手机神"),
)

TWO_POUCH_ADD_FRAGMENTS = (
    (920046, "暗魔之王"),
    (920048, "风云骤合"),
    (920047, "龙家霸业"),
    (920065, "沉思者之暗语"),
)

TWO_POUCH_FRAGMENTS = {
    920054,
    920057,
    920050,
    920051,
    920049,
    920063,
}


def import_unitypy(unitypy_dir: pathlib.Path):
    if str(unitypy_dir) not in sys.path:
        sys.path.insert(0, str(unitypy_dir))

    import UnityPy  # noqa: F401
    from UnityPy.helpers import TypeTreeHelper
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

    return (
        UnityPy,
        TypeTreeHelper,
        TypeTreeGenerator,
    )


def load_store_data(
    bundle_path: pathlib.Path,
    unitypy_dir: pathlib.Path,
    managed_dir: pathlib.Path,
):
    UnityPy, TypeTreeHelper, TypeTreeGenerator = import_unitypy(
        unitypy_dir
    )
    TypeTreeHelper.read_typetree_boost = None

    generator = TypeTreeGenerator("4.7.2f1")
    for dll_path in managed_dir.glob("*.dll"):
        generator.load_dll(dll_path.read_bytes())

    environment = UnityPy.load(str(bundle_path))
    environment.typetree_generator = generator
    obj = next(
        item
        for item in environment.objects
        if item.type.name == "MonoBehaviour"
    )
    tree = obj.read_typetree()
    if tree.get("m_Name") != "StoreData":
        raise ValueError(
            f"Unexpected StoreData root: {tree.get('m_Name')!r}"
        )
    return environment, obj, tree


def patch_store(tree: dict) -> None:
    items = tree["blackStoreItemList"]
    by_pid = {}
    used_indexes = set()
    for item in items:
        pid = item["itemPID"]
        if pid in by_pid:
            raise ValueError(
                f"Duplicate Black Store itemPID in config: {pid}"
            )
        by_pid[pid] = item
        used_indexes.add(item["index"])

    for pid in TWO_POUCH_FRAGMENTS:
        item = by_pid.get(pid)
        if item is None:
            raise ValueError(f"Black Store itemPID not found: {pid}")
        item["itemRule"] = 12010
        item["matPIDList"] = [GRANNY_POUCH_ID]
        item["matCountList"] = [2]

    def ensure_fragment(pid: int, heart_name: str, amount: int) -> None:
        item = by_pid.get(pid)
        if item is None:
            next_index = NEW_INDEX_START
            while next_index in used_indexes:
                next_index += 1
            used_indexes.add(next_index)
            item = {
                "index": next_index,
                "desc": f"铁马互市兑换{heart_name}残页",
                "itemPID": pid,
                "itemRule": 12010,
                "matPIDList": [GRANNY_POUCH_ID],
                "matCountList": [amount],
                "startTime": "00:00:00",
                "endTime": "23:59:59",
                "exchangeCount": NEW_EXCHANGE_LIMIT,
            }
            items.append(item)
            by_pid[pid] = item
            return
        item["itemRule"] = 12010
        item["matPIDList"] = [GRANNY_POUCH_ID]
        item["matCountList"] = [amount]

    for pid, heart_name in ONE_POUCH_ADD_FRAGMENTS:
        ensure_fragment(pid, heart_name, 1)
    for pid, heart_name in TWO_POUCH_ADD_FRAGMENTS:
        ensure_fragment(pid, heart_name, 2)

    for item in items:
        if item["itemPID"] not in TOP_DIRECT_ITEMS:
            item["exchangeCount"] = NEW_EXCHANGE_LIMIT

    material_key = lambda item: (
        tuple(item["matPIDList"]),
        tuple(item["matCountList"]),
        item["index"],
    )
    reordered = [by_pid[pid] for pid in TOP_DIRECT_ITEMS]
    for star_ids in (FIVE_STAR_FRAGMENT_IDS, SIX_STAR_FRAGMENT_IDS):
        group = [item for item in items if item["itemPID"] in star_ids]
        reordered.extend(sorted(group, key=material_key))

    known_ids = (
        set(TOP_DIRECT_ITEMS) |
        FIVE_STAR_FRAGMENT_IDS |
        SIX_STAR_FRAGMENT_IDS
    )
    extras = [item for item in items if item["itemPID"] not in known_ids]
    reordered.extend(sorted(extras, key=material_key))
    if len(reordered) != len(items):
        raise ValueError("Unexpected Black Store item count during sorting")
    items[:] = reordered


def verify_store(tree: dict) -> None:
    items = tree["blackStoreItemList"]
    by_pid = {item["itemPID"]: item for item in items}
    for pid, _ in ONE_POUCH_ADD_FRAGMENTS:
        item = by_pid.get(pid)
        if item is None:
            raise ValueError(f"Added fragment missing after save: {pid}")
        if item["matPIDList"] != [GRANNY_POUCH_ID]:
            raise ValueError(f"Unexpected added cost for {pid}")
        if item["matCountList"] != [1]:
            raise ValueError(f"Unexpected added amount for {pid}")
    for pid, _ in TWO_POUCH_ADD_FRAGMENTS:
        item = by_pid.get(pid)
        if item is None:
            raise ValueError(f"Added fragment missing after save: {pid}")
        if item["matPIDList"] != [GRANNY_POUCH_ID]:
            raise ValueError(f"Unexpected added cost for {pid}")
        if item["matCountList"] != [2]:
            raise ValueError(f"Unexpected added amount for {pid}")
        if item["itemRule"] != 12010:
            raise ValueError(f"Unexpected added output rule for {pid}")
    for pid in TWO_POUCH_FRAGMENTS:
        item = by_pid.get(pid)
        if item is None:
            raise ValueError(f"Updated fragment missing after save: {pid}")
        if item["matPIDList"] != [GRANNY_POUCH_ID]:
            raise ValueError(f"Unexpected updated cost for {pid}")
        if item["matCountList"] != [2]:
            raise ValueError(f"Unexpected updated amount for {pid}")
        if item["itemRule"] != 12010:
            raise ValueError(f"Unexpected one-fragment rule for {pid}")

    for item in items:
        expected_limit = TOP_DIRECT_LIMITS.get(
            item["itemPID"],
            NEW_EXCHANGE_LIMIT,
        )
        if item["exchangeCount"] != expected_limit:
            raise ValueError(
                f"Unexpected exchange limit for {item['itemPID']}: "
                f"{item['exchangeCount']}"
            )

    ordered_pids = [item["itemPID"] for item in items]
    if tuple(ordered_pids[:2]) != TOP_DIRECT_ITEMS:
        raise ValueError(
            f"Unexpected top Black Store order: {ordered_pids[:2]}"
        )

    material_key = lambda item: (
        tuple(item["matPIDList"]),
        tuple(item["matCountList"]),
        item["index"],
    )
    for star_ids in (FIVE_STAR_FRAGMENT_IDS, SIX_STAR_FRAGMENT_IDS):
        actual = [
            item for item in items if item["itemPID"] in star_ids
        ]
        if actual != sorted(actual, key=material_key):
            raise ValueError(
                "Black Store fragments are not ordered by exchange material."
            )

    first_five = min(ordered_pids.index(pid) for pid in FIVE_STAR_FRAGMENT_IDS)
    last_five = max(ordered_pids.index(pid) for pid in FIVE_STAR_FRAGMENT_IDS)
    first_six = min(ordered_pids.index(pid) for pid in SIX_STAR_FRAGMENT_IDS)
    last_six = max(ordered_pids.index(pid) for pid in SIX_STAR_FRAGMENT_IDS)
    if not last_five < first_six or last_six >= len(ordered_pids) - 1:
        raise ValueError(
            "Black Store fragments are not ordered 5-star then 6-star."
        )


def patch_bundle(
    input_path: pathlib.Path,
    output_path: pathlib.Path,
    unitypy_dir: pathlib.Path,
    managed_dir: pathlib.Path,
) -> None:
    environment, obj, tree = load_store_data(
        input_path,
        unitypy_dir,
        managed_dir,
    )
    patch_store(tree)
    obj.save_typetree(tree)

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

    _, _, saved_tree = load_store_data(
        output_path,
        unitypy_dir,
        managed_dir,
    )
    verify_store(saved_tree)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=pathlib.Path)
    parser.add_argument("output", type=pathlib.Path)
    parser.add_argument(
        "--unitypy-dir",
        type=pathlib.Path,
        default=DEFAULT_UNITYPY_DIR,
    )
    parser.add_argument(
        "--managed-dir",
        type=pathlib.Path,
        default=pathlib.Path("tmp/managed"),
    )
    args = parser.parse_args()

    patch_bundle(
        args.input,
        args.output,
        args.unitypy_dir,
        args.managed_dir,
    )
    print(
        "Patched 7 one-pouch additions, 4 two-pouch additions, "
        "and 6 existing two-pouch exchanges."
    )


if __name__ == "__main__":
    main()
