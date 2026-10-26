#!/usr/bin/env python3
"""Guard against replacing the live mod stack when zone records are exhausted."""
from pathlib import Path
import re
import sys

source = Path(sys.argv[1]).read_text(encoding="utf-8")
start = source.index("static int AddModZones(")
end = source.index("static bool FreesModZones(", start)
body = source[start:end]
capacity = re.search(
    r"const int recordsAvailable\s*=\s*ZoneRecordsFree\(\)\s*-\s*2\s*\*\s*count\s*;"
    r"\s*if\s*\(recordsAvailable\s*<=\s*0\)\s*\{[^{}]*return 0;\s*\}",
    body, re.S)
assert capacity, "No early capacity rejection before stack mutation"
assert capacity.end() < body.index("SetStack(names, wanted);"), "Stack changed before capacity check"
assert "int records = recordsAvailable;" in body
assert body.index("if (added > 0)") < body.index("ResetScriptClaims();", body.index("int records = recordsAvailable;"))
print("PASS: exhausted zone capacity leaves active stack, loaded names and script claims untouched")
