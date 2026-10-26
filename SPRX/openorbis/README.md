# Hosted OpenOrbis build

The Linux builder compiles every translation unit in the original ORBIS
project with LLVM 18, C++20, the OpenOrbis target libraries/linker script and
the GoldHEN plugin CRT. It links a PS4 ELF and runs `create-fself --lib` to
produce `BO3-Customs.sprx`. The original Windows ORBIS project is retained.

CI downloads the public OpenOrbis 0.5.4 release and the GoldHEN CRT at commit
`bcea3c7ef01dac6d7f9f49ebf9e90fe66d86f5f7` using the SHA-256 values in
[the dependency lock](../../.github/openorbis-lock.json). No uploaded user
toolchain archive is redistributed. The initial local build used the tools
supplied by the user; the hosted build separately verifies the public tools.
They are distinct build inputs and their identities appear in each manifest.

`include/` supplies the Sony-style names used by BO3 over OpenOrbis types.
It contains real target ABI declarations and constants, with static checks
for record sizes/offsets. It provides no host implementation of console APIs.
Keyboard, mouse, networking, Lua and Debug diagnostics remain included.

Declaration references:

- OpenOrbis `orbis/libkernel.h`, `_types/kernel.h`, `Pad.h`, `UserService.h`,
  `Sysmodule.h`, `Net.h` and its BSD headers.
- [shadPS4 mouse ABI](https://github.com/shadps4-emu/shadPS4/blob/main/src/core/libraries/mouse/mouse.h),
  [IME ABI](https://github.com/shadps4-emu/shadPS4/blob/main/src/core/libraries/ime/ime_common.h),
  [network options](https://github.com/shadps4-emu/shadPS4/blob/main/src/core/libraries/net/net_types.h),
  [network errors](https://github.com/shadps4-emu/shadPS4/blob/main/src/core/libraries/net/net_error.h)
  and [libretro mouse constants](https://github.com/libretro/libretro-common/blob/master/include/defines/ps4_defines.h).

The watchdog needs separate exception-handler imports from libraries
`libkernel` and `libkernel_unity`, both within module `libkernel`.
`unity_imports.c` describes the same two real NIDs as the original ORBIS
archive using [OpenOrbis explicit NID syntax](https://github.com/OpenOrbis/create-fself/blob/master/pkg/oelf/OELFGenDynlibData.go).
This derived import library's function bodies never enter the plugin. The
verifier requires both library identities and both NIDs in the final module.

Every build starts with empty native output. It saves OpenOrbis
`readoelf --all` output and independently checks SCE metadata, GoldHEN module
parameters, 16 KB load alignment, relocations, entrypoints and plugin exports.
All loadable FSELF payloads must match the inspected OELF byte-for-byte.
The release manifest binds the report to the SPRX SHA-256 and includes the
downloaded OpenOrbis and GoldHEN license notices. New constructors or TLS
fail until their startup requirements are explicitly supported.

The output is a homebrew GoldHEN FSELF, not a Sony production signature.
Build success does not establish execution on PS4 BO3 1.33. Automatic
releases remain experimental with hardware validation pending.

Local build on Ubuntu 24.04 with Python 3.11+:

```sh
sudo apt-get install clang-18 lld-18
python3 .github/scripts/build_sprx.py
```

Outputs are in `build/openorbis`. `CC`, `CXX` and `LD` can select another
installed LLVM 18. `--package` requires the actual GitHub run identity.
