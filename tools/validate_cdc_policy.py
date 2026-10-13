#!/usr/bin/env python3
"""Mandatory strict orchestration entry point for the vendored CDC adapter validator."""
from pathlib import Path
import runpy
import sys
sys.dont_write_bytecode = True
scripts = Path(__file__).resolve().parents[1] / ".agents/skills/continuous-development-cycle/scripts"
try:
    import yaml  # noqa: F401
except ImportError:
    raise SystemExit("FAIL: PyYAML required; install .agents/skills/continuous-development-cycle/scripts/requirements.txt")
sys.path.insert(0, str(scripts))
if __name__ == "__main__":
    runpy.run_path(str(scripts / "validate_adapter.py"), run_name="__main__")
