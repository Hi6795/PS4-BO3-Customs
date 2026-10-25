#!/usr/bin/env python3
"""Host regression for SPRX's embedded k_tickLua (Lua 5.4 via liblua).

Usage: python3 SPRX/tests/lua_tick_checks.py [SPRX/t7_lua.cpp]
Requires a host liblua5.4 shared library; this is not a PS4/HKS test.
"""
import ast
import ctypes
import ctypes.util
from pathlib import Path
import re
import sys


def embedded_tick(source: str) -> str:
    match = re.search(
        r'static const char k_tickLua\[\]\s*=\s*((?:\s*"(?:\\.|[^"\\])*"\s*)+);',
        source,
    )
    if not match:
        raise AssertionError('k_tickLua C++ literal was not found')
    strings = re.findall(r'"(?:\\.|[^"\\])*"', match.group(1))
    if not strings:
        raise AssertionError('k_tickLua has no string literals')
    return ''.join(ast.literal_eval(item) for item in strings)


class Lua:
    def __init__(self):
        libname = ctypes.util.find_library('lua5.4')
        if not libname:
            raise RuntimeError('liblua5.4 is required for this host regression')
        self.lib = ctypes.CDLL(libname)
        self.lib.luaL_newstate.restype = ctypes.c_void_p
        self.lib.luaL_openlibs.argtypes = [ctypes.c_void_p]
        self.lib.luaL_loadstring.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
        self.lib.luaL_loadstring.restype = ctypes.c_int
        self.lib.lua_pcallk.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_longlong, ctypes.c_void_p]
        self.lib.lua_pcallk.restype = ctypes.c_int
        self.lib.lua_tolstring.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p]
        self.lib.lua_tolstring.restype = ctypes.c_char_p
        self.lib.lua_close.argtypes = [ctypes.c_void_p]
        self.state = self.lib.luaL_newstate()
        if not self.state:
            raise RuntimeError('luaL_newstate failed')
        self.lib.luaL_openlibs(self.state)

    def run(self, code: str) -> None:
        status = self.lib.luaL_loadstring(self.state, code.encode('utf-8'))
        if status == 0:
            status = self.lib.lua_pcallk(self.state, 0, 0, 0, 0, None)
        if status:
            message = self.lib.lua_tolstring(self.state, -1, None)
            raise AssertionError((message or b'<unknown Lua error>').decode('utf-8', 'replace'))

    def close(self):
        self.lib.lua_close(self.state)


def check(script: str) -> None:
    lua = Lua()
    try:
        lua.run("""
            BO3CustomsMapTabs = { logs = {} }
            lobbyCount = 0
            BO3CustomsLobbyMaps = { tick = function() lobbyCount = lobbyCount + 1 end }
            BO3CustomsTick = function() error('intentional tick failure') end
        """)
        lua.run(script)
        lua.run('assert(lobbyCount == 1, "lobby tick must run after menu tick failure")')
        lua.run('assert(#BO3CustomsMapTabs.logs == 1, "first failure must be logged once")')
        lua.run('assert(string.find(BO3CustomsMapTabs.logs[1], "intentional tick failure", 1, true))')
        lua.run(script)
        lua.run('assert(lobbyCount == 2 and #BO3CustomsMapTabs.logs == 1, "repeated failure must not flood logs")')
        lua.run('BO3CustomsTick = function() end')
        lua.run(script)
        lua.run('assert(lobbyCount == 3, "recovered menu tick must preserve lobby tick")')
        lua.run('BO3CustomsTick = function() error("second failure") end')
        lua.run(script)
        lua.run('assert(lobbyCount == 4 and #BO3CustomsMapTabs.logs == 2, "new failure after recovery must be logged")')
        lua.run('BO3CustomsTick = nil; BO3CustomsLobbyMaps = {}')
        lua.run(script)
        lua.run('assert(lobbyCount == 4, "missing tick functions must be tolerated")')
        lua.run('BO3CustomsMapTabs = nil; BO3CustomsTick = function() error("no log sink") end')
        lua.run(script)
        print('PASS: failure isolation, lobby continuity, one-shot diagnostics, recovery, absent callbacks/sink')
    finally:
        lua.close()


if __name__ == '__main__':
    source = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('SPRX/t7_lua.cpp')
    check(embedded_tick(source.read_text(encoding='utf-8')))
