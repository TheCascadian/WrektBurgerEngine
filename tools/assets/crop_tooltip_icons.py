#!/usr/bin/env python3
"""Crop the generated 6x6 tooltip icon sheet into 34 transparent PNGs.

Usage:
  python3 crop_tooltip_icons.py [sheet.png] [output-directory]

The script detects transparent row/column gutters from the alpha channel rather
than assuming 320px cell boundaries; this preserves silhouettes that cross the
nominal cell edges.
"""
from __future__ import annotations

import sys
from pathlib import Path

import numpy as np
from PIL import Image

NAMES = [
    "air", "army", "battle", "characters", "combat", "cost",
    "decisions", "desert", "factory", "fleet", "forest", "gold",
    "hills", "information", "losses", "manpower", "marsh", "military",
    "mountains", "navy", "org", "organization", "plains", "politics",
    "population", "production", "recruit", "stockpile", "strength", "supply",
    "technology", "terrain", "war", "war_score",
]


def runs(active: np.ndarray) -> list[tuple[int, int]]:
    """Return inclusive bounds of contiguous True ranges."""
    indices = np.flatnonzero(active)
    if not len(indices):
        return []
    result: list[tuple[int, int]] = []
    start = previous = int(indices[0])
    for raw in indices[1:]:
        i = int(raw)
        if i != previous + 1:
            result.append((start, previous + 1))
            start = i
        previous = i
    result.append((start, previous + 1))
    return result


def main() -> None:
    sheet_path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).with_name("tooltip-icons-34-sheet.png")
    output_dir = Path(sys.argv[2]) if len(sys.argv) > 2 else Path(__file__).with_name("tooltip-icons")
    output_dir.mkdir(parents=True, exist_ok=True)

    sheet = Image.open(sheet_path).convert("RGBA")
    rgba = np.asarray(sheet)
    alpha = rgba[:, :, 3]
    threshold = 16
    columns = runs(np.count_nonzero(alpha > threshold, axis=0) > 0)
    rows = runs(np.count_nonzero(alpha > threshold, axis=1) > 0)
    if len(columns) != 6 or len(rows) != 6:
        raise RuntimeError(f"Expected six separated rows and columns; detected {len(rows)} rows, {len(columns)} columns")
    if len(NAMES) != 34:
        raise RuntimeError(f"Name list has {len(NAMES)} items, expected 34")

    pad = 8
    canvas_size = 64
    max_icon_size = 56
    sheet_w, sheet_h = sheet.size
    emitted = 0
    for index, name in enumerate(NAMES):
        row, col = divmod(index, 6)
        x0, x1 = columns[col]
        y0, y1 = rows[row]
        bounds = (max(0, x0 - pad), max(0, y0 - pad), min(sheet_w, x1 + pad), min(sheet_h, y1 + pad))
        cell = sheet.crop(bounds)
        cell_alpha = cell.getchannel("A")
        visible = cell_alpha.point(lambda value: 255 if value > 8 else 0).getbbox()
        if visible is None:
            raise RuntimeError(f"No visible icon found in row {row + 1}, column {col + 1} ({name})")
        icon = cell.crop(visible)
        scale = min(max_icon_size / icon.width, max_icon_size / icon.height)
        size = (max(1, round(icon.width * scale)), max(1, round(icon.height * scale)))
        icon = icon.resize(size, Image.Resampling.LANCZOS)
        canvas = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
        xy = ((canvas_size - size[0]) // 2, (canvas_size - size[1]) // 2)
        canvas.alpha_composite(icon, xy)
        out = output_dir / f"{name}.png"
        canvas.save(out, optimize=True)
        emitted += 1

    print(f"Exported {emitted} RGBA PNGs to {output_dir.resolve()}")


if __name__ == "__main__":
    main()
