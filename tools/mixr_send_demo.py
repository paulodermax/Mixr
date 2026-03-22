#!/usr/bin/env python3
"""Leitet nach ESP/tools/mixr_send_demo.py weiter (Aufruf vom Repo-Root)."""
import os
import pathlib
import sys

_script = pathlib.Path(__file__).resolve().parent.parent / "ESP" / "tools" / "mixr_send_demo.py"
if not _script.is_file():
    print(f"Fehlt: {_script}", file=sys.stderr)
    sys.exit(1)
os.execv(sys.executable, [sys.executable, str(_script)] + sys.argv[1:])
