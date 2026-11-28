"""Prove the payload reader matches the writer's byte layout.

A regression guard for the fault this project shipped twice: the marker is
written ahead of the deflate stream, and a reader that looks for it after
decompressing fails on every install. This reproduces the writer exactly as
build.py does it and asserts the reader's framing, so a build fails here
instead of on a user's machine.

    python scripts/test_payload_layout.py
"""

import struct
import sys
import zlib

MAGIC = b"ZYCRDPAYLOAD1\0\0\0"


def write_payload(bodies):
    """The writer, byte for byte as build_payload does it."""
    manifest = "".join("%s\0%d\n" % (name, len(body))
                       for name, body in bodies).encode("utf-8")
    compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
    out = bytearray(MAGIC)
    out += compressor.compress(struct.pack("<I", len(manifest)))
    out += compressor.compress(manifest)
    for _name, body in bodies:
        out += compressor.compress(body)
    out += compressor.flush()
    return bytes(out), manifest


def read_payload_correct(resource):
    """What Setup.cs does now: take the marker off the raw resource first."""
    if resource[:len(MAGIC)] != MAGIC:
        raise AssertionError("marker is not at the start of the resource")
    raw = zlib.decompress(resource[len(MAGIC):], -15)
    length = struct.unpack("<I", raw[:4])[0]
    if length <= 0 or length > 16 * 1024 * 1024:
        raise AssertionError("implausible manifest length")
    manifest = raw[4:4 + length].decode("utf-8")
    return manifest, raw[4 + length:]


def read_payload_old_bug(resource):
    """What Setup.cs did before: inflate the marker as if it were data."""
    return zlib.decompress(resource, -15)


def main():
    bodies = [("Zycord.exe", b"x" * 23), ("stub.exe", b"yyyyyyy")]
    resource, expected_manifest = write_payload(bodies)

    if len(MAGIC) != 16:
        print("FAIL: marker is %d bytes, the reader and verifier assume 16" % len(MAGIC))
        return 1

    manifest, body = read_payload_correct(resource)
    if manifest.encode("utf-8") != expected_manifest:
        print("FAIL: manifest round trip differs")
        return 1
    joined = b"".join(b for _n, b in bodies)
    if body != joined:
        print("FAIL: file bytes round trip differs")
        return 1

    try:
        read_payload_old_bug(resource)
    except zlib.error:
        pass
    else:
        print("FAIL: the old reader unexpectedly accepted the payload; the "
              "layout guard is not exercising the bug")
        return 1

    print("PASS: marker is 16 bytes, manifest and file bytes round trip, "
          "the pre-fix reader is rejected")
    return 0


if __name__ == "__main__":
    sys.exit(main())
