#!/usr/bin/env python3
"""Regression for late/recreated LUI native methods in embedded lobby Lua.

Usage: python3 SPRX/tests/lua_native_probe_checks.py [SPRX/t7_lua.cpp]
Requires host liblua5.4; does not validate PS4 HKS or console behavior.
"""
from pathlib import Path
import sys

from lua_lobby_map_checks import embedded_lobby
from lua_tick_checks import Lua


def check(script: str) -> None:
    # Execute the exact production patchLui function extracted from the embedded
    # lobby-parity Lua. Other lobby features are intentionally outside this test.
    start = script.index('local native = {')
    end = script.index('local watched = {}', start)
    native_patch = script[start:end]
    lua = Lua()
    try:
        lua.run("""
            local G = _G
            LUI = { UIImage = { new = function() error('constructor not ready') end } }
        """ + native_patch + """
            patchLui()
            assert(LUI.UIImage.setImage == nil)
            LUI.UIImage.new = function() return nil end
            patchLui()
            assert(LUI.UIImage.setImage == nil)
            LUI.UIImage.new = function()
              return { setImage = function() return 17 end }
            end
            patchLui()
            assert(type(LUI.UIImage.setImage) == 'function', 'failed probe was not retried')
            assert(LUI.UIImage.setImage() == 17)
            local old = LUI.UIImage
            LUI.UIImage = { new = function()
              return { setImage = function() return 29 end }
            end }
            patchLui()
            assert(LUI.UIImage.setImage() == 29, 'recreated class was not patched')
            local cls = LUI.UIImage
            cls.new = function()
              return { setImage = function() return 31 end,
                       setAlpha = function() return 32 end }
            end
            cls.setImage = nil
            patchLui()
            assert(cls.setImage() == 31, 'replaced constructor was not patched')
            assert(cls.setAlpha() == 32)
            local image = cls.setImage
            patchLui()
            assert(cls.setImage == image, 'idempotent patch changed existing method')
        """)
        print('PASS: failed/nil probe retry, class recreation, constructor replacement, idempotency')
    finally:
        lua.close()


if __name__ == '__main__':
    source = Path(sys.argv[1]) if len(sys.argv) > 1 else Path('SPRX/t7_lua.cpp')
    check(embedded_lobby(source.read_text(encoding='utf-8')))
