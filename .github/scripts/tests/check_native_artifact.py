"""Negative checks against an actual newly built PS4 artifact."""
from pathlib import Path
import struct
import sys
import tempfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from verify_sprx import Elf, verify


def check(directory, configuration):
    elf, oelf, sprx = [directory / ("BO3-Customs." + ext) for ext in ("elf", "oelf", "sprx")]
    verify(elf, oelf, sprx, configuration)
    original_oelf, original_sprx = oelf.read_bytes(), sprx.read_bytes()
    target = Elf(original_oelf)
    broken_oelf = bytearray(original_oelf)
    struct.pack_into("<H", broken_oelf, 16, 3)
    wrong_param = bytearray(original_oelf)
    wrong_param[target.segment(0x61000002)[2] + 8] ^= 1
    wrong_header = bytearray(original_sprx)
    embedded = 32 + struct.unpack_from("<H", original_sprx, 24)[0] * 32
    wrong_header[embedded + 24] ^= 1
    wrong_payload = bytearray(original_sprx)
    payload_offset = struct.unpack_from("<Q", original_sprx, 32 + 32 + 8)[0]
    wrong_payload[payload_offset + 64] ^= 1
    cases = [(bytes(broken_oelf), original_sprx), (bytes(wrong_param), original_sprx),
             (original_oelf, original_sprx[:-1]), (original_oelf, bytes(wrong_header)),
             (original_oelf, bytes(wrong_payload)), (original_oelf, elf.read_bytes())]
    with tempfile.TemporaryDirectory() as temp:
        candidate_oelf, candidate_sprx = Path(temp) / "candidate.oelf", Path(temp) / "candidate.sprx"
        for i, (oelf_data, sprx_data) in enumerate(cases):
            candidate_oelf.write_bytes(oelf_data)
            candidate_sprx.write_bytes(sprx_data)
            try:
                verify(elf, candidate_oelf, candidate_sprx, configuration)
            except ValueError:
                continue
            raise AssertionError(f"Invalid native artifact accepted, case {i}")
    print(f"Actual PS4 target accepted; all {len(cases)} invalid module/container variants rejected.")


if __name__ == "__main__":
    check(Path(sys.argv[1]), sys.argv[2] if len(sys.argv) > 2 else "Debug")
