#!/usr/bin/env python3
"""Captures the website screenshots from the real app.

Each scene seeds a throwaway data folder (presets, favorites, theme, accent),
launches a Debug build with --capture (muted, separate instance from the user's
Blanky) and writes the PNG into website/public/screenshots.

Run from WSL after `scripts/win.sh build`:  python3 scripts/screenshots.py
"""

import json
import os
import shutil
import subprocess
import sys
import uuid
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
WIN_ROOT = Path(os.environ.get("BLANKY_WIN_DIR", "/mnt/c/Users/kaman/.blanky-build/repo"))
EXE = WIN_ROOT / "src/Blanky/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/Blanky.exe"
SCRATCH = WIN_ROOT.parent / "screenshots"
OUT = REPO / "website/public/screenshots"


def win_path(path: Path) -> str:
    return subprocess.check_output(["wslpath", "-w", str(path)], text=True).strip()


def preset(name, states, accent=None, creator=None):
    return {
        "id": str(uuid.uuid5(uuid.NAMESPACE_URL, f"blanky:{name}")),
        "name": name,
        "soundStates": [{"fileName": f, "isSelected": True, "volume": v} for f, v in states],
        "isDefault": False,
        "createdVersion": "1.0.0",
        "accentColorName": accent,
        "creatorName": creator,
    }


PRESETS = [
    preset("Cozy Night In", [("fireplace", 0.7), ("wind", 0.45), ("warm-piano", 0.55)], "orange"),
    preset("Ocean Focus", [("green-noise", 0.5), ("waves", 0.7), ("lofi-beats", 0.45)], "teal"),
    preset("Heavy Rain Sleep", [("rain", 0.75), ("brown-noise", 0.5), ("fan", 0.4)], "indigo"),
]

ALL_SOUNDS = {
    "id": str(uuid.uuid5(uuid.NAMESPACE_URL, "blanky:default")),
    "name": "All Blanky Sounds",
    "isDefault": True,
    "soundStates": [
        {"fileName": "rain", "isSelected": True, "volume": 0.62},
        {"fileName": "waves", "isSelected": True, "volume": 0.5},
        {"fileName": "fan", "isSelected": True, "volume": 0.7},
        {"fileName": "warm-piano", "isSelected": True, "volume": 0.45},
    ],
}

SCENES = {
    # name: (settings overrides, extra args)
    "mixer-dark": ({"accentColorName": "blue"}, []),
    "preset-dark": ({"accentColorName": "blue"}, ["--demo-preset", "Cozy Night In"]),
    "timer-dark": ({"accentColorName": "blue"}, ["--demo-preset", "Heavy Rain Sleep", "--demo-timer", "45"]),
    "accent-dark": ({"accentColorName": "purple"}, ["--open", "accent"]),
    "preset-light": ({"accentColorName": "blue", "theme": "Light"}, ["--demo-preset", "Ocean Focus"]),
}


def main():
    if not EXE.exists():
        sys.exit(f"Build first (scripts/win.sh build); missing {EXE}")
    OUT.mkdir(parents=True, exist_ok=True)
    for name, (overrides, args) in SCENES.items():
        data = SCRATCH / name
        shutil.rmtree(data, ignore_errors=True)
        data.mkdir(parents=True)
        settings = {
            "autoPlayOnLaunch": True,
            "starredItems": [f"preset:{PRESETS[0]['id']}", "sound:rain"],
            **overrides,
        }
        (data / "settings.json").write_text(json.dumps(settings))
        (data / "presets.json").write_text(json.dumps([ALL_SOUNDS, *PRESETS]))
        shot = SCRATCH / f"{name}.png"
        shot.unlink(missing_ok=True)
        result = subprocess.run(
            [str(EXE), "--data", win_path(data), "--capture", win_path(shot), *args],
            cwd=EXE.parent, timeout=60,
        )
        if result.returncode != 0 or not shot.exists():
            sys.exit(f"{name}: capture failed (exit {result.returncode})")
        shutil.copy(shot, OUT / f"{name}.png")
        print(f"captured {name}")


if __name__ == "__main__":
    main()
