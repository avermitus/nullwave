"""Packs the fal enemy model textures into the model sheets (3 x 2 tiles of 1024 px each):
    Assets/Textures/Models.png        base colour, alpha = glow mask (bright, saturated neon parts,
                                      which the game tints with each enemy's colour)
    Assets/Textures/ModelsNormal.png  tangent-space normal maps (flat where a model has none)
Tile order matches the Blender converters (row 0 is the bottom row of the image).
Usage: python pack_model_sheet.py
"""
import os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
TEX = os.path.normpath(os.path.join(HERE, "..", "..", "Textures"))
NAMES = ["hunter", "spiraller", "pouncer", "pack", "juggernaut", "choir"]
COLS, ROWS, TILE = 3, 2, 1024

colour = np.zeros((ROWS * TILE, COLS * TILE, 4), np.uint8)
normal = np.zeros((ROWS * TILE, COLS * TILE, 4), np.uint8)
normal[..., 0] = 128; normal[..., 1] = 128; normal[..., 2] = 255; normal[..., 3] = 255
for i, name in enumerate(NAMES):
    tx, ty = i % COLS, i // COLS
    top = (ROWS - 1 - ty) * TILE           # tile row 0 sits at the bottom of the image
    region = (slice(top, top + TILE), slice(tx * TILE, (tx + 1) * TILE))
    img = Image.open(os.path.join(HERE, "model_textures", name + ".png")).convert("RGB").resize((TILE, TILE), Image.LANCZOS)
    rgb = np.asarray(img).astype(np.float32) / 255.0
    mx, mn = rgb.max(axis=2), rgb.min(axis=2)
    sat = (mx - mn) / np.maximum(mx, 1e-3)
    glow = np.clip((sat - 0.30) * 3.0, 0, 1) * np.clip((mx - 0.40) * 2.5, 0, 1)
    colour[region] = (np.clip(np.dstack([rgb, glow]), 0, 1) * 255).astype(np.uint8)
    npath = os.path.join(HERE, "model_textures", name + "_normal.png")
    if os.path.exists(npath):
        n = Image.open(npath).convert("RGB").resize((TILE, TILE), Image.LANCZOS)
        normal[region] = np.dstack([np.asarray(n), np.full((TILE, TILE), 255, np.uint8)])
    print(f"{name}: tile ({tx},{ty}) glow {glow.mean() * 100:.1f}% normal {'yes' if os.path.exists(npath) else 'flat'}")

Image.fromarray(colour, "RGBA").save(os.path.join(TEX, "Models.png"))
Image.fromarray(normal, "RGBA").save(os.path.join(TEX, "ModelsNormal.png"))
print("model sheets written to", TEX)
