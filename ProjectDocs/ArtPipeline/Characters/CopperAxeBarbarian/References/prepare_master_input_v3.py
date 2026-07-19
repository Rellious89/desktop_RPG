from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parent
SOURCE = (
    ROOT.parent
    / "Prototypes"
    / "hybrid-c64-04"
    / "hybrid-c64-barbarian-source.png"
)
OUTPUT = ROOT / "CopperAxeBarbarian-master-input-v3-c-hybrid-512.png"

CANVAS_SIZE = (512, 512)
BASELINE_Y = 448


source = Image.open(SOURCE).convert("RGBA")
canvas = Image.new("RGBA", CANVAS_SIZE)

# Preserve the approved concept pixels exactly. Padding is for a clean upload
# asset only; PerfectPixel has already been observed to normalize content size.
x = (CANVAS_SIZE[0] - source.width) // 2
y = BASELINE_Y - source.height
canvas.alpha_composite(source, (x, y))
canvas.save(OUTPUT)

alpha_bounds = canvas.getchannel("A").getbbox()
if canvas.size != CANVAS_SIZE or alpha_bounds is None:
    raise RuntimeError("Invalid PerfectPixel master input export.")

print(f"Wrote: {OUTPUT}")
print(f"Canvas: {canvas.size}")
print(f"Alpha bounds: {alpha_bounds}")
