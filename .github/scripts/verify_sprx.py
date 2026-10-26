"""Inspect SCE module metadata and compare its packaged FSELF payloads.

Format references: OpenOrbis/create-fself pkg/oelf and pkg/fself, and
SPRX/make_fself.py. This does not establish Sony signing or PS4 execution.
"""
import base64
import hashlib
from pathlib import Path
import struct


def require(condition, message):
    if not condition:
        raise ValueError(message)


def nid(name):
    if name.startswith("__PS4_NID_"):
        return name[10:].replace("_plus", "+").replace("_minus", "-")
    data = name.encode() + bytes.fromhex("518D64A635DED8C1E6B039B1C3E55230")
    return base64.b64encode(hashlib.sha1(data).digest()[:8][::-1]).decode().rstrip("=").replace("/", "-")


class Elf:
    def __init__(self, data):
        self.data = data
        require(len(data) >= 64 and data[:7] == b"\x7fELF\x02\x01\x01", "Expected little-endian ELF64")
        fields = struct.unpack_from("<HHIQQQIHHHHHH", data, 16)
        self.type, machine, version, self.entry, phoff, shoff, _, ehsize, phsize, phnum, shsize, shnum, shstr = fields
        require(machine == 62 and version == 1 and ehsize == 64 and phsize == 56 and 0 < phnum < 32,
                "Invalid x86-64 program headers")
        self.phend = phoff + phsize * phnum
        require(self.phend <= len(data), "Truncated program headers")
        self.ph = [struct.unpack_from("<IIQQQQQQ", data, phoff + i * phsize) for i in range(phnum)]
        self.sections, self.section_names = [], []
        if shnum:
            require(shsize == 64 and shoff + shnum * shsize <= len(data), "Truncated section headers")
            self.sections = [struct.unpack_from("<IIQQQQIIQQ", data, shoff + i * shsize) for i in range(shnum)]
            names = self.sections[shstr]
            strings = self.slice(names[4], names[5])
            self.section_names = [self.string(strings, s[0]) for s in self.sections]

    def slice(self, offset, size):
        require(0 <= offset <= len(self.data) and 0 <= size <= len(self.data) - offset, "Truncated ELF data")
        return self.data[offset:offset + size]

    @staticmethod
    def string(data, offset):
        end = data.find(b"\0", offset)
        require(0 <= offset < len(data) and end >= offset, "Invalid ELF string")
        return data[offset:end].decode("ascii")

    def segment(self, kind):
        found = [p for p in self.ph if p[0] == kind]
        require(len(found) == 1, f"Expected one segment {kind:#x}")
        return found[0]

    def executable(self, address):
        return any(p[0] == 1 and p[1] & 1 and p[3] <= address < p[3] + p[5] for p in self.ph)

    def globals(self):
        symbols = {}
        for section in self.sections:
            if section[1] != 2:
                continue
            strings = self.sections[section[6]]
            strings = self.slice(strings[4], strings[5])
            for offset in range(section[4], section[4] + section[5], 24):
                name, info, _, index, value, size = struct.unpack_from("<IBBHQQ", self.data, offset)
                if index and info >> 4 in (1, 2):
                    symbols[self.string(strings, name)] = (value, size)
        return symbols


def verify(elf_path, oelf_path, sprx_path, configuration="Debug"):
    source, target = Elf(Path(elf_path).read_bytes()), Elf(Path(oelf_path).read_bytes())
    require(target.type == 0xFE18 and target.data[7] == 9, "Expected a FreeBSD SCE dynamic library")
    for name, section in zip(source.section_names, source.sections):
        require(not (name in (".init_array", ".tdata", ".tbss") and section[5]),
                "GoldHEN CRT needs explicit support for new constructors or TLS")
    for p in target.ph:
        if p[0] in (1, 0x61000010):
            require(p[7] == 0x4000 and p[2] % 0x4000 == p[3] % 0x4000, "Invalid PS4 load alignment")
            require(p[1] & 3 != 3, "Unexpected writable executable segment")
    module = target.segment(0x61000002)
    require(module[5] == 24 and struct.unpack("<QQQ", target.slice(module[2], 24)) ==
            (0x18, 0x13C13F4BF, 0x4508101), "Missing GoldHEN module parameters")
    dynamic = target.segment(2)
    tags = {}
    for offset in range(dynamic[2], dynamic[2] + dynamic[5], 16):
        tag, value = struct.unpack_from("<QQ", target.data, offset)
        tags.setdefault(tag, []).append(value)
    globals_ = source.globals()
    for tag, name in ((12, "_init"), (13, "_fini")):
        require(name in globals_ and tags.get(tag) == [globals_[name][0]] and target.executable(globals_[name][0]),
                f"Invalid GoldHEN {name} entrypoint")
    require(tags.get(0x61000031, [0])[0] > 0 and tags.get(0x6100002D, [0])[0] > 0, "Missing SCE relocations")
    dynlib = target.segment(0x61000000)
    strings = target.slice(dynlib[2] + tags[0x61000035][0], tags[0x61000037][0])
    libraries = {value >> 48: Elf.string(strings, value & 0xFFFFFFFF) for value in tags[0x61000015]}
    alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+-"
    symbols = {}
    start, size = dynlib[2] + tags[0x61000039][0], tags[0x6100003F][0]
    for offset in range(start, start + size, 24):
        name, _, _, _, value, symbol_size = struct.unpack("<IBBHQQ", target.slice(offset, 24))
        if name:
            symbols[Elf.string(strings, name)] = (value, symbol_size)
    exports = ("_init", "_fini", "module_start", "module_stop", "plugin_load", "plugin_unload", "g_pluginName")
    for name in exports:
        require(name in globals_ and symbols.get(nid(name) + "#A#A") == globals_[name], f"Missing SCE export {name}")
    required_libraries = {"libkernel", "libSceLibcInternal", "libSceSysmodule", "libSceNet", "libScePad",
                          "libSceMouse", "libSceUserService", "libSceSystemService"}
    if configuration == "Debug":
        required_libraries.add("libkernel_unity")
        for function in ("sceKernelInstallExceptionHandler", "sceKernelRaiseException"):
            for library in ("libkernel", "libkernel_unity"):
                index = next((i for i, value in libraries.items() if value == library), -1)
                require(index > 0 and nid(function) + "#" + alphabet[index] + "#B" in symbols,
                        f"Missing real {library} diagnostic import {function}")
    require(set(libraries.values()) == required_libraries, "Unexpected target import libraries")
    container = Path(sprx_path).read_bytes()
    require(len(container) >= 32 and container[:8] == b"\x4f\x15\x3d\x1d\0\x01\x01\x12", "Invalid FSELF header")
    require(struct.unpack_from("<Q", container, 16)[0] == len(container), "Truncated FSELF")
    count = struct.unpack_from("<H", container, 24)[0]
    require(0 < count < 64, "Invalid FSELF segment count")
    embedded = 32 + count * 32
    require(container[embedded:embedded + target.phend] == target.data[:target.phend], "FSELF/OELF headers differ")
    payloads = set()
    for i in range(count):
        flags, offset, filesz, memsz = struct.unpack_from("<QQQQ", container, 32 + i * 32)
        require(offset <= len(container) and filesz <= len(container) - offset, "Truncated FSELF segment")
        if flags & 0xF0000:
            continue
        index = (flags >> 20) & 0xFFFF
        require(index < len(target.ph) and index not in payloads and not flags & 0xA,
                "Unexpected encrypted, compressed or duplicate FSELF payload")
        p = target.ph[index]
        require(filesz == memsz == p[5] and container[offset:offset + filesz] == target.slice(p[2], p[5]),
                "FSELF payload differs from inspected SCE module")
        payloads.add(index)
    require(payloads == {i for i, p in enumerate(target.ph) if p[0] in (1, 0x61000010, 0x61000000)},
            "FSELF is missing a required SCE segment")
    return {"format": "PS4 SCE dynamic library / GoldHEN FSELF", "machine": "x86-64", "configuration": configuration,
            "sprx_sha256": hashlib.sha256(container).hexdigest(), "exports": list(exports),
            "import_libraries": sorted(libraries.values()), "init": tags[12][0], "fini": tags[13][0],
            "payload_segments_verified": len(payloads), "console_validated": False, "sony_signed": False}
