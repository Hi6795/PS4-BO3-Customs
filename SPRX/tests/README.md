# SPRX host-side checks

The `mod_load_context_tests.cpp` regression covers per-worker attribution of
active mod zones, nested loads, contention between simultaneous workers, and
safe behavior if every context slot is occupied.

Run from the repository root on a machine with a C++20 compiler:

```sh
g++ -std=c++20 -Wall -Wextra -Wpedantic -Werror -O2 -pthread \
  SPRX/tests/mod_load_context_tests.cpp -o /tmp/bo3_mod_load_context_tests
/tmp/bo3_mod_load_context_tests
```

These tests exercise the native context component; they do not replace building
the complete SPRX with the PS4 SDK or checking custom-map transitions on a
PS4 running BO3 1.33. In particular, an actual multi-mod console run is needed
to confirm that the observed crash has this race as its only cause.
