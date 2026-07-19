from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent

SHEETS = {
    "LOW · simplified": (
        ROOT / "low-simple-transparent-raw.png",
        1,
        "low-simple",
    ),
    "MID · 3px cluster": (ROOT / "mid-transparent-raw.png", 3, "mid"),
    "HIGH · source density": (
        ROOT / "high-transparent-raw.png",
        1,
        "high",
    ),
}


def hard_alpha(image: Image.Image) -> Image.Image:
    image = image.convert("RGBA")
    red, green, blue, alpha = image.split()
    alpha = alpha.point(lambda value: 255 if value >= 128 else 0)
    return Image.merge("RGBA", (red, green, blue, alpha))


def enforce_cluster(image: Image.Image, factor: int) -> Image.Image:
    image = hard_alpha(image)
    if factor == 1:
        return image

    width, height = image.size
    padded_width = ((width + factor - 1) // factor) * factor
    padded_height = ((height + factor - 1) // factor) * factor
    padded = Image.new("RGBA", (padded_width, padded_height))
    padded.alpha_composite(image)
    logical = padded.resize(
        (padded_width // factor, padded_height // factor),
        Image.Resampling.NEAREST,
    )
    return logical.resize(
        (padded_width, padded_height), Image.Resampling.NEAREST
    ).crop((0, 0, width, height))


def first_pose(sheet: Image.Image) -> Image.Image:
    width, height = sheet.size
    pose = sheet.crop((0, 0, round(width / 3), height))
    bounds = pose.getchannel("A").getbbox()
    if bounds is None:
        raise RuntimeError("The first pose has no visible pixels.")
    return pose.crop(bounds)


def fit_height(image: Image.Image, height: int) -> Image.Image:
    width = max(1, round(image.width * height / image.height))
    return image.resize((width, height), Image.Resampling.NEAREST)


def load_font(size: int) -> ImageFont.ImageFont:
    candidates = (
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
    )
    for candidate in candidates:
        try:
            return ImageFont.truetype(candidate, size)
        except OSError:
            pass
    return ImageFont.load_default()


processed: dict[str, Image.Image] = {}
for label, (source, factor, slug) in SHEETS.items():
    sheet = enforce_cluster(Image.open(source), factor)
    sheet.save(ROOT / f"{slug}-prototype-sheet.png")
    processed[label] = sheet


cat_source = (
    Path(__file__).resolve().parents[6]
    / "Assets/Art/Character/CatKnight/idle/Cat-knight-idle-00.png"
)
cat = hard_alpha(Image.open(cat_source))
cat_bounds = cat.getchannel("A").getbbox()
if cat_bounds is None:
    raise RuntimeError("CatKnight reference has no visible pixels.")
cat = cat.crop(cat_bounds)

entries = [("CURRENT · CatKnight", cat)]
entries.extend((label, first_pose(sheet)) for label, sheet in processed.items())

canvas = Image.new("RGBA", (1440, 420), (24, 24, 24, 255))
draw = ImageDraw.Draw(canvas)
font = load_font(24)
small_font = load_font(18)
baseline = 340
column_width = canvas.width // len(entries)

for index, (label, image) in enumerate(entries):
    display = fit_height(image, 210)
    x = index * column_width + (column_width - display.width) // 2
    y = baseline - display.height
    canvas.alpha_composite(display, (x, y))

    text_bounds = draw.textbbox((0, 0), label, font=font)
    text_width = text_bounds[2] - text_bounds[0]
    draw.text(
        (index * column_width + (column_width - text_width) // 2, 44),
        label,
        font=font,
        fill=(238, 238, 238, 255),
    )

draw.line((40, baseline, canvas.width - 40, baseline), fill=(90, 90, 90, 255), width=1)
caption = "All characters normalized to the same 210 px body height for on-screen readability comparison"
caption_bounds = draw.textbbox((0, 0), caption, font=small_font)
caption_width = caption_bounds[2] - caption_bounds[0]
draw.text(
    ((canvas.width - caption_width) // 2, 375),
    caption,
    font=small_font,
    fill=(170, 170, 170, 255),
)
canvas.save(ROOT / "comparison-at-210px.png")

zoom = canvas.resize((canvas.width * 2, canvas.height * 2), Image.Resampling.NEAREST)
zoom.save(ROOT / "comparison-at-210px-2x.png")
