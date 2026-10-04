"""Unity's fileID for a script class that lives in a precompiled assembly.

Scene/prefab YAML references such a script as {fileID: <this>, guid: <dll .meta guid>, type: 3}.
The value is the first 4 bytes (little-endian int32) of MD4("s\\0\\0\\0" + namespace + class name),
where "s\\0\\0\\0" is class id 115 (MonoScript) as int32.
"""

import struct


def _md4(message: bytes) -> bytes:
    def f(x, y, z): return (x & y) | (~x & z)
    def g(x, y, z): return (x & y) | (x & z) | (y & z)
    def h(x, y, z): return x ^ y ^ z
    def rotl(v, s): return ((v << s) | (v >> (32 - s))) & 0xFFFFFFFF

    length = len(message) * 8
    message += b"\x80" + b"\x00" * ((55 - len(message)) % 64) + struct.pack("<Q", length)
    a, b, c, d = 0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476

    for offset in range(0, len(message), 64):
        x = struct.unpack("<16I", message[offset:offset + 64])
        aa, bb, cc, dd = a, b, c, d
        for i, s in zip([0, 4, 8, 12] * 4, [3, 7, 11, 19] * 4):
            pass
        # round 1
        for k in range(16):
            s = (3, 7, 11, 19)[k % 4]
            if k % 4 == 0: a = rotl((a + f(b, c, d) + x[k]) & 0xFFFFFFFF, s)
            elif k % 4 == 1: d = rotl((d + f(a, b, c) + x[k]) & 0xFFFFFFFF, s)
            elif k % 4 == 2: c = rotl((c + f(d, a, b) + x[k]) & 0xFFFFFFFF, s)
            else: b = rotl((b + f(c, d, a) + x[k]) & 0xFFFFFFFF, s)
        # round 2
        for n, k in enumerate((0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15)):
            s = (3, 5, 9, 13)[n % 4]
            if n % 4 == 0: a = rotl((a + g(b, c, d) + x[k] + 0x5A827999) & 0xFFFFFFFF, s)
            elif n % 4 == 1: d = rotl((d + g(a, b, c) + x[k] + 0x5A827999) & 0xFFFFFFFF, s)
            elif n % 4 == 2: c = rotl((c + g(d, a, b) + x[k] + 0x5A827999) & 0xFFFFFFFF, s)
            else: b = rotl((b + g(c, d, a) + x[k] + 0x5A827999) & 0xFFFFFFFF, s)
        # round 3
        for n, k in enumerate((0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15)):
            s = (3, 9, 11, 15)[n % 4]
            if n % 4 == 0: a = rotl((a + h(b, c, d) + x[k] + 0x6ED9EBA1) & 0xFFFFFFFF, s)
            elif n % 4 == 1: d = rotl((d + h(a, b, c) + x[k] + 0x6ED9EBA1) & 0xFFFFFFFF, s)
            elif n % 4 == 2: c = rotl((c + h(d, a, b) + x[k] + 0x6ED9EBA1) & 0xFFFFFFFF, s)
            else: b = rotl((b + h(c, d, a) + x[k] + 0x6ED9EBA1) & 0xFFFFFFFF, s)
        a, b, c, d = (a + aa) & 0xFFFFFFFF, (b + bb) & 0xFFFFFFFF, (c + cc) & 0xFFFFFFFF, (d + dd) & 0xFFFFFFFF

    return struct.pack("<4I", a, b, c, d)


def script_file_id(namespace: str, class_name: str) -> int:
    digest = _md4(b"s\0\0\0" + (namespace + class_name).encode("utf-8"))
    return struct.unpack("<i", digest[:4])[0]
