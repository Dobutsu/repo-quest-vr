"""Decode a built Addressables content catalog (catalog.json) into the entries an editor would need to rebuild it.

The AssetRipper export has the addressable assets but not the editor-only AddressableAssetSettings/groups. The player
catalog still records, per asset: its bundle, its address and labels. Output (JSON list):
  {"path": "Assets/...", "bundle": "<bundle file>", "address": "...", "labels": [...], "guid": "<original GUID>"}
Usage: python scripts/addressables_catalog.py <catalog.json> [--out entries.json]
"""

import argparse
import base64
import json
import re
import struct
import sys

GUID_KEY = re.compile(r"^[0-9a-f]{32}$")


def read_keys(data: bytes) -> list:
    count = struct.unpack_from("<i", data, 0)[0]
    pos, keys = 4, []
    for _ in range(count):
        kind = data[pos]
        pos += 1
        if kind in (0, 1):  # ascii / unicode string
            length = struct.unpack_from("<i", data, pos)[0]
            pos += 4
            raw = data[pos:pos + length]
            pos += length
            keys.append(raw.decode("ascii" if kind == 0 else "utf-16-le"))
        elif kind == 4:  # int32
            keys.append(struct.unpack_from("<i", data, pos)[0])
            pos += 4
        elif kind == 7:  # hash128 string form
            length = data[pos]
            pos += 1
            keys.append(data[pos:pos + length].decode("ascii"))
            pos += length
        else:
            raise ValueError(f"unsupported key type {kind} at offset {pos - 1}")
    return keys


def read_buckets(data: bytes) -> list[list[int]]:
    count = struct.unpack_from("<i", data, 0)[0]
    pos, buckets = 4, []
    for _ in range(count):
        _, entry_count = struct.unpack_from("<ii", data, pos)
        pos += 8
        buckets.append(list(struct.unpack_from(f"<{entry_count}i", data, pos)))
        pos += 4 * entry_count
    return buckets


def read_entries(data: bytes) -> list[tuple]:
    count = struct.unpack_from("<i", data, 0)[0]
    return [struct.unpack_from("<7i", data, 4 + i * 28) for i in range(count)]


def decode(catalog: dict) -> list[dict]:
    ids = catalog["m_InternalIds"]
    providers = catalog["m_ProviderIds"]
    keys = read_keys(base64.b64decode(catalog["m_KeyDataString"]))
    buckets = read_buckets(base64.b64decode(catalog["m_BucketDataString"]))
    entries = read_entries(base64.b64decode(catalog["m_EntryDataString"]))

    keys_for_entry: dict[int, list] = {}
    for key, bucket in zip(keys, buckets):
        for entry in bucket:
            keys_for_entry.setdefault(entry, []).append(key)

    # internal id index -> bundle file, for bundle entries
    bundle_of_key = {}
    for index, (internal_id, provider, *_rest) in enumerate(entries):
        if providers[provider].endswith("AssetBundleProvider"):
            for key in keys_for_entry.get(index, []):
                bundle_of_key[key] = re.split(r"[\\/]", ids[internal_id])[-1]

    result = []
    for index, (internal_id, provider, dependency_key, _dep_hash, _data, primary_key, _type) in enumerate(entries):
        if not providers[provider].endswith("BundledAssetProvider"):
            continue
        entry_keys = [k for k in keys_for_entry.get(index, []) if isinstance(k, str)]
        address = keys[primary_key] if 0 <= primary_key < len(keys) else ids[internal_id]
        labels = sorted({k for k in entry_keys if k != address and not GUID_KEY.match(k) and not k.startswith("Assets/")})
        guids = sorted({k for k in entry_keys if GUID_KEY.match(k)})
        bundle = bundle_of_key.get(keys[dependency_key]) if dependency_key >= 0 else None
        # The asset's GUID in the original project: Localization refers to tables by it
        result.append({"path": ids[internal_id], "bundle": bundle, "address": address, "labels": labels,
                       "guid": guids[0] if guids else None})

    # Sub-objects of one asset appear once per object; keep one entry per path
    unique = {}
    for item in result:
        unique.setdefault(item["path"], item)
    return list(unique.values())


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("catalog")
    parser.add_argument("--out")
    args = parser.parse_args()

    entries = decode(json.load(open(args.catalog)))
    text = json.dumps(entries, indent=2, ensure_ascii=False)
    if args.out:
        open(args.out, "w", newline="\n").write(text)
    print(text if not args.out else f"{len(entries)} entries -> {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
