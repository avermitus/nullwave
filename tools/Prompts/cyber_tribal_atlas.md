# Shared cyber-tribal atlas recipes

These are deterministic art-direction recipes for a future Sogni run; they do not call an API. The bundled maps are authored procedurally and are the integration baseline. Do not bake individual glyph silhouettes into the input: disconnected geometry supplies those shapes.

## Surface recipe

- Canvas: square 1024 × 1024, aligned to the shared atlas, no text, logos or framing.
- Materials: dark obsidian charcoal carbon weave and brushed titanium, weathered bone with bronze/copper panels, restrained fine grain and seams. Palette anchors: obsidian #0D0F14, bone #C8B89D.
- Preserve a continuous usable surface; no directional lighting, cast shadows, ambient occlusion, perspective or painted symbols. Seed: 7319. Model and sampler are intentionally unspecified until the lead chooses the live service configuration; these words alone cannot guarantee identical remote generations.
- Supply the saved local image to the postprocessor below. It uses luminance only, scaled and clipped, as subtle detail in the lower two bands. The neon band and its layout remain generated and full bleed.

## Atlas contract

OBJ UV origin is bottom left; PNG origin is top left. Exact pixel rows (inclusive):

| OBJ V | PNG rows | Material |
| --- | --- | --- |
| 0.75–1.0 | 0–255 | Full-bleed neon: cyan #00F0FF left, amber #FF9E00 right; magenta #FF0055 accent at columns 480–543 |
| 0.5–0.75 | 256–511 | Bone #C8B89D and bronze/copper |
| 0–0.5 | 512–1023 | Charcoal carbon/titanium, obsidian #0D0F14 |

Each band reserves U < 0.5 for player and U > 0.5 for pylon. Use unique islands and gutters within the appropriate region; keep islands and filter footprints away from material boundaries, including U = 0.5. Neon covers every pixel of the band, including its edges and gutters. Emissive is black outside that band. Both neon halves stay luminous; the center accent is also luminous. This is a color atlas, not a mask of glyph shapes.

## Reproduction

Requires Python 3, NumPy and Pillow. From the repository root:

```sh
python tools/generate_cyber_tribal_atlas.py
python tools/generate_cyber_tribal_atlas.py --surface path/to/sogni-surface.png --output path/to/output
```

The first command regenerates the bundled baseline; the second produces a separate variant from a local input. All maps are 1024 × 1024 PNG: RGB albedo and emissive (sRGB color), RGB tangent-space normal (linear, OpenGL +Y), and grayscale roughness (linear, white = rough). The flat neon normal is (128,128,255). Use linear sampling for normal and roughness, sRGB for albedo and emissive. The procedural normal derives from the same height detail used for the non-emissive surfaces. No credentials or network access are needed.

## Bundled surface provenance

The committed maps use local Sogni surface `wf_durable_workflow_0c05e78b-47f8-4765-84e3-3b2171060d20.png` (SHA-256: `766bae5e24466371ab62ce382271a77883f6a8886c3de53a8460035ffe3bfebb`). Reproduce them by supplying that exact image with `--surface`. The no-argument command produces the original procedural baseline, not the committed surface-derived variant. Roughness and emissive are intentionally independent of the source image.
