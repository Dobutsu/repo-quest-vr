"""Decode a built Addressables content catalog into the entries an editor would need to rebuild it.

The AssetRipper export has the addressable assets but not the editor-only AddressableAssetSettings/groups. The player
catalog still records, per asset: its bundle, its address and labels. Output (JSON list):
  {"path": "Assets/...", "bundle": "<bundle file>", "address": "...", "labels": [...], "guid": "<original GUID>"}
The catalog is catalog.json (Addressables 1.x, R.E.P.O. up to v0.4.4) or catalog.bin (Addressables 2.x, since v0.5.0;
a catalog.json left over from an older install sits next to it, so settings.json says which one the game loads).
Usage: python scripts/addressables_catalog.py <catalog.json | catalog.bin | aa folder> [--out entries.json]
"""

import argparse
import base64
import json
import re
import struct
import sys
from pathlib import Path

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


class BinaryCatalog:
    """Addressables 2.x catalog.bin (BinaryStorageBuffer, ContentCatalogData.ResourceLocator in the package). Values are
    referenced by byte offset; arrays keep their byte size in the 4 bytes before the offset; uint.MaxValue is null."""

    NULL = 0xFFFFFFFF
    UNICODE = 0x80000000
    DYNAMIC = 0x40000000
    OFFSET_MASK = 0x3FFFFFFF

    def __init__(self, data: bytes):
        self.data = data

    def u32(self, offset: int, index: int = 0) -> int:
        return struct.unpack_from("<I", self.data, offset + 4 * index)[0]

    def array(self, offset: int) -> list[int]:
        """uint array (object ids, or the raw words of a struct array)"""
        if offset == self.NULL:
            return []
        count = self.u32(offset - 4) // 4
        return list(struct.unpack_from(f"<{count}I", self.data, offset))

    def plain_string(self, string_id: int) -> str:
        offset = string_id & self.OFFSET_MASK
        length = self.u32(offset - 4)
        raw = self.data[offset:offset + length]
        return raw.decode("utf-16-le" if string_id & self.UNICODE else "ascii")

    def string(self, string_id: int, separator: str) -> str | None:
        """A string, or one split at a separator into parts stored once each: a chain from the last part back"""
        if string_id == self.NULL:
            return None
        if not string_id & self.DYNAMIC or not separator:
            return self.plain_string(string_id)
        parts, next_id = [], string_id
        while next_id != self.NULL:
            offset = next_id & self.OFFSET_MASK
            parts.append(self.plain_string(self.u32(offset)))
            next_id = self.u32(offset, 1)
        return separator.join(reversed(parts))

    def type_name(self, offset: int) -> str | None:
        if offset == self.NULL:
            return None
        return self.string(self.u32(offset, 1), ".")

    def object(self, offset: int):
        """A boxed value: (type id, value id)"""
        if offset == self.NULL:
            return None
        kind = self.type_name(self.u32(offset))
        value = self.u32(offset, 1)
        if kind == "System.String":
            # string id + the separator it was split at (a char)
            return self.string(self.u32(value), chr(struct.unpack_from("<H", self.data, value + 4)[0]))
        if kind == "System.Int32":
            return struct.unpack_from("<i", self.data, value)[0]
        if kind == "UnityEngine.Hash128":
            return self.data[value:value + 16].hex()
        return f"<{kind}>"

    def location(self, offset: int) -> dict:
        primary_key, internal_id, provider, dependencies, _hash, _extra, _type = struct.unpack_from("<IIIIiII", self.data, offset)
        return {"primary": self.string(primary_key, "/"), "internal_id": self.string(internal_id, "/"),
                "provider": self.string(provider, "."), "dependencies": self.array(dependencies)}

    def decode(self) -> list[dict]:
        _magic, _version, keys_offset = struct.unpack_from("<iiI", self.data, 0)
        words = self.array(keys_offset)
        keys_for_location: dict[int, list] = {}
        for name_offset, set_offset in zip(words[0::2], words[1::2]):
            key = self.object(name_offset)
            for location in self.array(set_offset):
                keys_for_location.setdefault(location, []).append(key)

        locations = {offset: self.location(offset) for offset in keys_for_location}
        # dependencies may point at bundle locations no key leads to
        for loc in list(locations.values()):
            for dep in loc["dependencies"]:
                if dep not in locations:
                    locations[dep] = self.location(dep)

        result = []
        for offset, loc in locations.items():
            if not loc["provider"].endswith("BundledAssetProvider"):
                continue
            entry_keys = [k for k in keys_for_location.get(offset, []) if isinstance(k, str)]
            address = loc["primary"] or loc["internal_id"]
            labels = sorted({k for k in entry_keys if k != address and not GUID_KEY.match(k) and not k.startswith("Assets/")})
            guids = sorted({k for k in entry_keys if GUID_KEY.match(k)})
            # The first dependency is the bundle that holds the asset, the others the bundles it needs
            bundles = [locations[d] for d in loc["dependencies"]]
            bundle = re.split(r"[\\/]", bundles[0]["internal_id"])[-1] if bundles else None
            result.append({"path": loc["internal_id"], "bundle": bundle, "address": address, "labels": labels,
                           "guid": guids[0] if guids else None})

        unique = {}
        for item in sorted(result, key=lambda i: i["path"]):
            unique.setdefault(item["path"], item)
        return list(unique.values())


def load(path: Path) -> list[dict]:
    """Entries of a catalog file, or of the catalog an aa folder's settings.json names"""
    if path.is_dir():
        settings = json.loads((path / "settings.json").read_text(encoding="utf-8"))
        main = next(c for c in settings["m_CatalogLocations"] if "AddressablesMainContentCatalog" in c["m_Keys"])
        path = path / re.split(r"[\\/]", main["m_InternalId"])[-1]
    data = path.read_bytes()
    if data.lstrip()[:1] == b"{":
        return decode(json.loads(data))
    return BinaryCatalog(data).decode()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("catalog")
    parser.add_argument("--out")
    args = parser.parse_args()

    entries = load(Path(args.catalog))
    text = json.dumps(entries, indent=2, ensure_ascii=False)
    if args.out:
        open(args.out, "w", newline="\n").write(text)
    print(text if not args.out else f"{len(entries)} entries -> {args.out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
