"""Packs the fal enemy model textures into Assets/Textures/Models.png (1536 x 1024, 3 x 2 tiles of 512).

Tile order matches blender_convert.py (row 0 is the bottom row of the image). Alpha holds the glow
mask: the bright, saturated neon parts of each texture, which the game tints with each enemy's colour.
Usage: python pack_model_sheet.py
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Textures", "Models.png"))
NAMES = ["hunter", "spiraller", "pouncer", "pack", "juggernaut", "choir"]
COLS, ROWS, TILE = 3, 2, 512

sheet = np.zeros((ROWS * TILE, COLS * TILE, 4), np.uint8)
for i, name in enumerate(NAMES):
    img = Image.open(os.path.join(HERE, "model_textures", name + ".png")).convert("RGB").resize((TILE, TILE), Image.LANCZOS)
    rgb = np.asarray(img).astype(np.float32) / 255.0
    mx, mn = rgb.max(axis=2), rgb.min(axis=2)
    sat = (mx - mn) / np.maximum(mx, 1e-3)
    glow = np.clip((sat - 0.30) * 3.0, 0, 1) * np.clip((mx - 0.40) * 2.5, 0, 1)
    rgba = np.dstack([rgb, glow])
    tx, ty = i % COLS, i // COLS
    top = (ROWS - 1 - ty) * TILE           # tile row 0 sits at the bottom of the image
    sheet[top:top + TILE, tx * TILE:(tx + 1) * TILE] = (np.clip(rgba, 0, 1) * 255).astype(np.uint8)
    print(f"{name}: tile ({tx},{ty}) glow coverage {glow.mean() * 100:.1f}%")

Image.fromarray(sheet, "RGBA").save(OUT)
print("model sheet written to", OUT)
