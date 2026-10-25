#!/usr/bin/env python3
"""Host regression for embedded k_lobbyParityLua's map wrappers (Lua 5.4).

Usage: python3 SPRX/tests/lua_lobby_map_checks.py [SPRX/t7_lua.cpp]
Requires liblua5.4. This is not a PS4/HKS runtime test.
"""
import ast
from pathlib import Path
import re
import sys

from lua_tick_checks import Lua


def embedded_lobby(source: str) -> str:
    match = re.search(
        r'static const char k_lobbyParityLua\[\]\s*=\s*((?:\s*"(?:\\.|[^"\\])*"\s*)+);',
        source,
    )
    if not match:
        raise AssertionError('k_lobbyParityLua C++ literal was not found')
    strings = re.findall(r'"(?:\\.|[^"\\])*"', match.group(1))
    if not strings:
        raise AssertionError('k_lobbyParityLua has no string literals')
    return ''.join(ast.literal_eval(item) for item in strings)


def check(script: str) -> None:
    lua = Lua()
    try:
        lua.run("""
            require = function(name) return name end
            Engine = {}
            BO3CustomMaps = { { ugc = '12345', id = 'zm_custom' } }
        """)
        lua.run(script)
        lua.run("require('early')")
        # Engine exports may appear after the first module load.
        lua.run("""
            Engine.GameLobbySetMap = function(map, flag) return map .. ':' .. flag end
            Engine.IsMapValid = function(map) return map == 'zm_custom' end
            require('late')
            assert(Engine.GameLobbySetMap('12345', 'ok') == 'zm_custom:ok', 'late map setter was not wrapped')
            assert(Engine.IsMapValid('12345'), 'late map validator was not wrapped')
            assert(Engine.GameLobbySetMap('zm_other', 'ok') == 'zm_other:ok', 'non-UGC map changed')
        """)
        # Repeated require must not accumulate nested wrappers.
        lua.run("""
            local first = Engine.GameLobbySetMap
            require('again')
            assert(Engine.GameLobbySetMap == first, 'duplicate map wrapper')
        """)
        # Reinitialized methods must be wrapped on the next require.
        lua.run("""
            Engine.GameLobbySetMap = function(map) return 'new:' .. map end
            require('replaced')
            assert(Engine.GameLobbySetMap('12345') == 'new:zm_custom', 'replaced setter not wrapped')
        """)
        # A recreated Engine table must also receive the compatibility wrappers.
        lua.run("""
            Engine = { GameLobbySetMap = function(map) return 'swapped:' .. map end,
                       IsMapValid = function(map) return map == 'zm_custom' end }
            require('new-state')
            assert(Engine.GameLobbySetMap('12345') == 'swapped:zm_custom', 'new Engine table not wrapped')
            assert(Engine.IsMapValid('12345'), 'new Engine validator not wrapped')
        """)
        print('PASS: late exports, UGC mapping, passthrough, idempotency, replacement, new Engine table')
    finally:
        lua.close()


if __name__ == '__main__':
    source = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('SPRX/t7_lua.cpp')
    check(embedded_lobby(source.read_text(encoding='utf-8')))
