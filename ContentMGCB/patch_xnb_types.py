"""Patch .xnb files built by MGCB (on .NET) to be loadable by FNA on .NET Framework.

MGCB, running on .NET Core/8, writes type names like
    Microsoft.Xna.Framework.Content.ListReader`1[[System.Char, System.Private.CoreLib, Version=8.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e]]
whereas FNA on .NET Framework 4.x needs the same types fully qualified against mscorlib.

This script walks the type-reader table at the head of every .xnb and rewrites
the assembly-qualified name fragments, re-encoding the 7-bit varint length.
"""

import os
import sys


OLD = (
    b"System.Private.CoreLib, Version=8.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e"
)
NEW = (
    b"mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
)


def read_varint(buf, pos):
    n = 0
    shift = 0
    while True:
        b = buf[pos]
        pos += 1
        n |= (b & 0x7F) << shift
        if (b & 0x80) == 0:
            break
        shift += 7
    return n, pos


def write_varint(n):
    out = bytearray()
    while True:
        if n < 0x80:
            out.append(n)
            return bytes(out)
        out.append((n & 0x7F) | 0x80)
        n >>= 7


def patch(buf: bytes) -> bytes | None:
    if buf[:3] != b"XNB":
        return None
    pos = 3
    pos += 1  # target platform
    pos += 1  # version
    pos += 1  # flags
    pos += 4  # file size
    # type reader count
    count, pos = read_varint(buf, pos)
    out = bytearray(buf[:pos])
    changed = False
    for _ in range(count):
        length, new_pos = read_varint(buf, pos)
        name = buf[new_pos : new_pos + length]
        new_name = name.replace(OLD, NEW)
        if new_name != name:
            changed = True
        new_len_bytes = write_varint(len(new_name))
        out += new_len_bytes
        out += new_name
        pos = new_pos + length
        # version (int32 little-endian)
        out += buf[pos : pos + 4]
        pos += 4
    out += buf[pos:]
    return bytes(out) if changed else None


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "bin", "Content")
    patched = 0
    scanned = 0
    for dirpath, _, files in os.walk(root):
        for f in files:
            if not f.endswith(".xnb"):
                continue
            scanned += 1
            path = os.path.join(dirpath, f)
            with open(path, "rb") as fh:
                data = fh.read()
            new = patch(data)
            if new is not None:
                with open(path, "wb") as fh:
                    fh.write(new)
                patched += 1
    print(f"scanned {scanned} xnb files, patched {patched}")


if __name__ == "__main__":
    main()
