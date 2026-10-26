#pragma once

// SPRX-only development version. The separately checksum-pinned Console.zip
// remains on its existing upstream release version.
#define BO3_CUSTOMS_SPRX_VERSION "4.00.1-dev.9"
#define BO3_CUSTOMS_DIAGNOSTIC_SCHEMA 3

// The hosted OpenOrbis builder injects the source commit (and a dirty marker).
// Manual Visual Studio builds still identify themselves by version and date.
#ifndef BO3_CUSTOMS_SOURCE_REV
#define BO3_CUSTOMS_SOURCE_REV "manual-build"
#endif
