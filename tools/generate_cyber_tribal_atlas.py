#!/usr/bin/env python3
"""Build the shared cyber-tribal atlas, optionally incorporating a local surface image."""
import argparse
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
SIZE = 1024


def save(path, data):
    Image.fromarray(np.uint8(np.clip(np.rint(data), 0, 255))).save(path, compress_level=9)


def build(surface=None, output=None):
    out = Path(output) if output else ROOT / 'Assets' / 'Textures'
    out.mkdir(parents=True, exist_ok=True)
    y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float64)
    # Analytic, periodic patterns: no random state, platform-specific font, or network.
    weave = np.sin(2*np.pi*(x+y)/12)*np.sin(2*np.pi*(x-y)/12)
    grain = np.sin(x*.067 + np.sin(y*.023)*2)*np.sin(y*.11)
    brushed = np.sin(x*.73 + 2*np.sin(y*.012))
    seams = np.maximum(0, np.cos(2*np.pi*(x/128 + y/192))-.97)/.03
    h = np.zeros((SIZE, SIZE), dtype=np.float64)
    lower = y >= 512
    middle = (y >= 256) & (y < 512)
    h[lower] = (0.7*weave + .25*grain - 1.0*seams)[lower]
    h[middle] = (.45*grain + .15*brushed - .55*seams)[middle]
    color = np.zeros((SIZE, SIZE, 3), dtype=np.float64)
    carbon = np.array([13, 15, 20]) + (3*weave + 3*grain - 3*seams)[..., None]
    # Alternating panels preserve the intended bone/bronze material reading.
    panel = ((x.astype(int)//128 + (y.astype(int)-256)//128) % 2 == 0)
    bone = np.array([200, 184, 157]) + (5*grain + 2*brushed - 5*seams)[..., None]
    bronze = np.array([130, 79, 43]) + (6*grain + 2*brushed - 5*seams)[..., None]
    color[lower] = carbon[lower]
    color[middle] = np.where(panel[..., None], bone, bronze)[middle]
    if surface:
        image = Image.open(surface).convert('RGB').resize((SIZE, SIZE), Image.Resampling.LANCZOS)
        source = np.asarray(image, dtype=np.float64)
        # Local source supplies restrained surface detail, never atlas placement or sigils.
        luminance = source @ np.array([.2126, .7152, .0722])
        detail = np.clip((luminance - 127.5)*.10, -10, 10)
        color[lower | middle] += detail[lower | middle, None]
        h[lower | middle] += (detail*.04)[lower | middle]
    # Full-bleed emission: the geometry, not painted glyphs, defines every stroke.
    left = x < 512
    color[(y < 256) & left] = [0, 240, 255]
    color[(y < 256) & ~left] = [255, 158, 0]
    # A narrow magenta accent stays bright, with no dark gaps in either half.
    color[(y < 256) & (x >= 480) & (x < 544)] = [255, 0, 85]
    rough = np.zeros((SIZE, SIZE), dtype=np.float64)
    rough[lower] = (180 + 14*weave + 8*grain)[lower]
    rough[middle] = np.where(panel, 170 + 8*grain, 105 + 8*grain)[middle]
    rough[y < 256] = 65
    # OpenGL tangent-space normal convention: +Y is green; flat = (128,128,255).
    dy, dx = np.gradient(h)
    nx, ny = -dx*.65, dy*.65  # image Y points down; UV V points up
    length = np.sqrt(nx*nx + ny*ny + 1)
    normal = np.stack((nx/length, ny/length, 1/length), axis=-1)*127.5 + 127.5
    emissive = np.zeros_like(color)
    emissive[y < 256] = color[y < 256]
    save(out/'Albedo.png', color)
    save(out/'Normal.png', normal)
    save(out/'Roughness.png', rough)
    save(out/'Emissive.png', emissive)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--surface', type=Path, help='optional local Sogni surface image; adds restrained luminance detail to non-emissive bands')
    parser.add_argument('--output', type=Path, help='output directory (default: Assets/Textures)')
    args = parser.parse_args()
    build(args.surface, args.output)
