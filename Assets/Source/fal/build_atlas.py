"""Builds Assets/Textures/{Albedo,Normal,Roughness,Emissive}.png from the fal material swatches.

Theme: cybersigilism x Renaissance surrealism x liquid chrome. Layout follows model_manifest.json
(uv origin bottom-left, so image rows run top = v 1):
    v 0.75..1.0  rows    0..255   neon sigils   (emissive linework)
    v 0.50..0.75 rows  256..511   marble        (Renaissance marble with liquid-gold veins)
    v 0.00..0.50 rows  512..1023  liquid chrome (engraved with sigil linework)
Each swatch is seamless, so tiling it across its band keeps the surface continuous.
Usage: python build_atlas.py   (run from this folder)
"""
import os
import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Textures"))
SIZE = 1024


def load(name, tile):
    im = Image.open(os.path.join(HERE, name)).convert("RGB").resize((tile, tile), Image.LANCZOS)
    return np.asarray(im).astype(np.float32) / 255.0


def tiled(img, rows, cols):
    return np.tile(img, (rows, cols, 1))


def luminance(rgb):
    return rgb[..., 0] * 0.2126 + rgb[..., 1] * 0.7152 + rgb[..., 2] * 0.0722


def normal_from_height(h, strength):
    """Tangent-space normal map (+Y up in image space flipped to OBJ convention by the shader)."""
    hx = np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)
    hy = np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)
    n = np.stack([-hx * strength, hy * strength, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def blur(h, radius):
    im = Image.fromarray(np.clip(h * 255, 0, 255).astype(np.uint8))
    return np.asarray(im.filter(ImageFilter.GaussianBlur(radius))).astype(np.float32) / 255.0


chrome = tiled(load("tex_chrome.png", 512), 1, 2)     # 512 x 1024
marble = tiled(load("tex_marble.png", 256), 1, 4)     # 256 x 1024
sigil = tiled(load("tex_sigil.png", 256), 1, 4)       # 256 x 1024

albedo = np.concatenate([sigil, marble, chrome], axis=0)

# heights: engraved grooves read darker in the chrome; marble relief is faint; sigils are flat glass
h_chrome = blur(luminance(chrome), 1.2)
h_marble = blur(luminance(marble), 1.5)
h_sigil = np.zeros(sigil.shape[:2], np.float32)
normal = np.concatenate([
    normal_from_height(h_sigil, 0.0),
    normal_from_height(h_marble, 2.5),
    normal_from_height(h_chrome, 9.0),
], axis=0)

# roughness: mirror chrome (rough only in the cut grooves), polished marble, satin sigil glass
r_chrome = np.clip(0.08 + (1.0 - luminance(chrome)) * 0.45, 0.05, 0.6)
r_marble = np.full(marble.shape[:2], 0.32, np.float32) - (luminance(marble) - 0.8) * 0.2
r_sigil = np.full(sigil.shape[:2], 0.45, np.float32)
roughness = np.concatenate([r_sigil, np.clip(r_marble, 0.2, 0.5), r_chrome], axis=0)

# emission: the sigil linework glows; the liquid-gold veins in the marble shimmer faintly
gold = np.clip((marble[..., 0] - marble[..., 2]) * 3.0 - 0.15, 0.0, 1.0)[..., None] * np.array([1.0, 0.72, 0.28]) * 0.35
lines = np.clip((luminance(sigil) - 0.08) * 1.4, 0.0, 1.0)[..., None] * sigil
emissive = np.concatenate([lines, gold, np.zeros_like(chrome)], axis=0)

os.makedirs(OUT, exist_ok=True)
Image.fromarray((np.clip(albedo, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, "Albedo.png"))
Image.fromarray((np.clip(normal, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, "Normal.png"))
Image.fromarray((np.clip(roughness, 0, 1) * 255).astype(np.uint8), mode="L").save(os.path.join(OUT, "Roughness.png"))
Image.fromarray((np.clip(emissive, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, "Emissive.png"))
print("atlas written to", OUT, albedo.shape)
