#!/usr/bin/env python3
"""Decode known PNG pixel patterns with the SPRX's production PNG reader."""

from pathlib import Path
import binascii
import os
import struct
import subprocess
import tempfile
import zlib


ROOT = Path(__file__).resolve().parents[1]


def chunk(kind, body):
    return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", binascii.crc32(kind + body))


def png(width, height, color_type, rows, compression=6, extra=()):
    channels = {0: 1, 2: 3, 6: 4}[color_type]
    assert len(rows) == height
    assert all(len(row) == width * channels for row in rows)
    scanlines = b"".join(b"\x00" + row for row in rows)
    header = struct.pack(">IIBBBBB", width, height, 8, color_type, 0, 0, 0)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) +
            b"".join(chunk(kind, body) for kind, body in extra) +
            chunk(b"IDAT", zlib.compress(scanlines, compression)) + chunk(b"IEND", b""))


HARNESS = r'''
#include "png.hpp"
#include <cstdio>
#include <cstdlib>
#include <vector>

int main(int argc, char** argv) {
    if (argc != 4) return 2;
    const int width = std::atoi(argv[2]);
    const int height = std::atoi(argv[3]);
    FILE* input = std::fopen(argv[1], "rb");
    if (!input) return 3;
    std::fseek(input, 0, SEEK_END);
    const long size = std::ftell(input);
    std::rewind(input);
    if (size <= 0) return 4;
    std::vector<unsigned char> file(static_cast<size_t>(size));
    if (std::fread(file.data(), 1, file.size(), input) != file.size()) return 5;
    std::fclose(input);
    PngImage image{};
    const char* error = nullptr;
    if (!Png_Open(file.data(), file.size(), &image, &error)) {
        std::fprintf(stderr, "PNG open: %s\n", error ? error : "unknown");
        return 6;
    }
    std::vector<unsigned char> pixels(static_cast<size_t>(width) * height * 4);
    if (!Png_Scale(image, pixels.data(), width, height, static_cast<size_t>(width) * 4, &error)) {
        std::fprintf(stderr, "PNG decode: %s\n", error ? error : "unknown");
        return 7;
    }
    if (std::fwrite(pixels.data(), 1, pixels.size(), stdout) != pixels.size()) return 8;
    return 0;
}
'''


with tempfile.TemporaryDirectory(prefix="bo3-png-fixtures-") as folder:
    temp = Path(folder)
    source = temp / "decode.cpp"
    program = temp / "decode"
    source.write_text(HARNESS, encoding="utf-8")
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                    "-fsanitize=address,undefined", "-I", str(ROOT), str(source),
                    str(ROOT / "png.cpp"), "-o", str(program)], check=True)

    red = bytes((255, 0, 0, 255))
    blue = bytes((0, 0, 255, 255))
    green = bytes((0, 255, 0, 255))
    black = bytes((0, 0, 0, 255))
    transparent = bytes((25, 50, 75, 0))
    fixtures = [
        ("rgba-default", png(3, 2, 6, [red + green + blue, blue + transparent + red]),
         red + green + blue + blue + transparent + red, 3, 2),
        ("rgba-stored", png(3, 2, 6, [red + green + blue, blue + transparent + red], 0),
         red + green + blue + blue + transparent + red, 3, 2),
        ("rgb", png(2, 2, 2, [bytes((255, 0, 0, 0, 255, 0)), bytes((0, 0, 255, 0, 0, 0))]),
         red + green + blue + black, 2, 2),
        ("gray", png(2, 2, 0, [bytes((0, 255)), bytes((100, 128))]),
         bytes((0, 0, 0, 255, 255, 255, 255, 255, 100, 100, 100, 255, 128, 128, 128, 255)), 2, 2),
    ]

    for name, data, expected, width, height in fixtures:
        file = temp / (name + ".png")
        file.write_bytes(data)
        result = subprocess.run([str(program), str(file), str(width), str(height)],
                                capture_output=True, check=True, env={**os.environ, "ASAN_OPTIONS": "detect_leaks=0"})
        assert result.stdout == expected, f"{name}: unexpected decoded pixels: {result.stdout!r}"

print("PASS: production PNG decoder handles RGBA (compressed/stored), RGB, and grayscale fixture pixels")
