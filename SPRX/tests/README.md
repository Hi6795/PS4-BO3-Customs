# Detour host regression checks

Run on x86-64 Linux with Python 3 and a C++17 compiler:

```sh
python3 SPRX/tests/run_detour_checks.py
```

The runner compiles the production `Detour.cpp` and `HDE64.c`. Only the four
kernel calls used by the detour are adapted for the host. `mmap`, `mprotect`
and `munmap` use real Linux pages; injected protection failures leave the
target read/execute only. Each check runs in a child process so a write fault
is reported as a failed check. Temporary build outputs are removed on exit.

The checks execute an original function, its replacement and the relocated
trampoline. They cover successful attachment/detachment, duplicate attachment,
denied writes to both trampoline jumps, a denied target write followed by a
successful retry, failed restoration followed by a successful retry, and
invalid inputs. A failed restore must preserve the installed detour and its
trampoline allocation; releasing either would leave the active hook dangling.

Before the protection-result checks were added, the same suite had four
failures out of six: the denied target write and denied restore both received
SIGSEGV, and the two denied trampoline writes were incorrectly accepted.

These checks establish host behavior at the syscall boundary. They do not
build a PS4 plugin or establish console, firmware, game-region or mod
compatibility. The source fix is not a verified resolution of the reported
BO1 Multiplayer image-pool exhaustion or UI/Lua errors. Test the eventual
console candidate on BO3 1.33, including ordinary loading, refused hook writes,
cleanup and retry, before treating it as console validated.
