from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent
PROJECT_ROOT = Path(__file__).resolve().parents[6]
SOURCE = ROOT / "low-abc-transparent.png"

CANDIDATES = (
    ("LOW-A", "low-a", 80, 5),
    ("LOW-B", "low-b", 64, 6),
    ("LOW-C", "low-c", 48, 8),
)


def hard_alpha(image: Image.Image) -> Image.Image:
    image = image.convert("RGBA")
    red, green, blue, alpha = image.split()
    alpha = alpha.point(lambda value: 255 if value >= 128 else 0)
    return Image.merge("RGBA", (red, green, blue, alpha))


def horizontal_spans(image: Image.Image, minimum_gap: int = 24) -> list[tuple[int, int]]:
    alpha = image.getchannel("A")
    occupied = [
        alpha.crop((x, 0, x + 1, image.height)).getbbox() is not None
        for x in range(image.width)
    ]
    raw_spans: list[tuple[int, int]] = []
    start = None
    for x, visible in enumerate(occupied + [False]):
        if visible and start is None:
            start = x
        elif not visible and start is not None:
            raw_spans.append((start, x))
            start = None

    merged: list[tuple[int, int]] = []
    for start, end in raw_spans:
        if merged and start - merged[-1][1] < minimum_gap:
            merged[-1] = (merged[-1][0], end)
        else:
            merged.append((start, end))
    return merged


def crop_candidate(image: Image.Image, span: tuple[int, int]) -> Image.Image:
    start, end = span
    candidate = image.crop((start, 0, end, image.height))
    bounds = candidate.getchannel("A").getbbox()
    if bounds is None:
        raise RuntimeError("Candidate contains no visible pixels.")
    return candidate.crop(bounds)


def logical_version(image: Image.Image, target_height: int) -> Image.Image:
    content_height = target_height - 4
    content_width = max(1, round(image.width * content_height / image.height))
    reduced = image.resize(
        (content_width, content_height), Image.Resampling.NEAREST
    )
    logical = Image.new("RGBA", (content_width + 4, target_height))
    logical.alpha_composite(reduced, (2, 2))
    return logical


def fit_height(image: Image.Image, target_height: int) -> Image.Image:
    width = max(1, round(image.width * target_height / image.height))
    return image.resize((width, target_height), Image.Resampling.NEAREST)


def load_font(size: int, bold: bool = False) -> ImageFont.ImageFont:
    filename = "Arial Bold.ttf" if bold else "Arial.ttf"
    try:
        return ImageFont.truetype(
            f"/System/Library/Fonts/Supplemental/{filename}", size
        )
    except OSError:
        return ImageFont.load_default()


source = hard_alpha(Image.open(SOURCE))
spans = horizontal_spans(source)
if len(spans) != 3:
    raise RuntimeError(f"Expected three candidates, found {len(spans)}: {spans}")

source_candidates: list[tuple[str, Image.Image]] = []
logical_candidates: list[tuple[str, Image.Image, int]] = []

for (label, slug, logical_height, preview_scale), span in zip(CANDIDATES, spans):
    candidate = crop_candidate(source, span)
    candidate.save(ROOT / f"{slug}-candidate-source.png")
    source_candidates.append((label, candidate))

    logical = logical_version(candidate, logical_height)
    logical.save(ROOT / f"{slug}-logical-{logical_height}h.png")
    logical.resize(
        (logical.width * preview_scale, logical.height * preview_scale),
        Image.Resampling.NEAREST,
    ).save(ROOT / f"{slug}-logical-{logical_height}h-preview.png")
    logical_candidates.append((label, logical, preview_scale))


cat = hard_alpha(
    Image.open(
        PROJECT_ROOT
        / "Assets/Art/Character/CatKnight/idle/Cat-knight-idle-00.png"
    )
)
cat_bounds = cat.getchannel("A").getbbox()
if cat_bounds is None:
    raise RuntimeError("CatKnight reference contains no visible pixels.")
cat = cat.crop(cat_bounds)

comparison_entries = [("CURRENT · CatKnight", cat)] + source_candidates
comparison = Image.new("RGBA", (1440, 420), (24, 24, 24, 255))
draw = ImageDraw.Draw(comparison)
label_font = load_font(24)
caption_font = load_font(18)
baseline = 340
column_width = comparison.width // len(comparison_entries)

for index, (label, image) in enumerate(comparison_entries):
    display = fit_height(image, 210)
    x = index * column_width + (column_width - display.width) // 2
    comparison.alpha_composite(display, (x, baseline - display.height))
    text_box = draw.textbbox((0, 0), label, font=label_font)
    text_width = text_box[2] - text_box[0]
    draw.text(
        (index * column_width + (column_width - text_width) // 2, 44),
        label,
        font=label_font,
        fill=(238, 238, 238, 255),
    )

draw.line((40, baseline, 1400, baseline), fill=(90, 90, 90, 255), width=1)
caption = "All four characters normalized to 210 px visible height"
caption_box = draw.textbbox((0, 0), caption, font=caption_font)
draw.text(
    ((comparison.width - (caption_box[2] - caption_box[0])) // 2, 375),
    caption,
    font=caption_font,
    fill=(170, 170, 170, 255),
)
comparison.save(ROOT / "comparison-body-proportion-210px.png")
comparison.resize(
    (comparison.width * 2, comparison.height * 2), Image.Resampling.NEAREST
).save(ROOT / "comparison-body-proportion-210px-2x.png")


logical_board = Image.new("RGBA", (1500, 560), (24, 24, 24, 255))
draw = ImageDraw.Draw(logical_board)
title_font = load_font(26, bold=True)
note_font = load_font(18)
column_width = logical_board.width // 3
baseline = 480

for index, ((label, slug, logical_height, _), (_, logical, scale)) in enumerate(
    zip(CANDIDATES, logical_candidates)
):
    display = logical.resize(
        (logical.width * scale, logical.height * scale), Image.Resampling.NEAREST
    )
    x = index * column_width + (column_width - display.width) // 2
    logical_board.alpha_composite(display, (x, baseline - display.height))
    heading = f"{label} · {logical_height}px body grid"
    text_box = draw.textbbox((0, 0), heading, font=title_font)
    draw.text(
        (index * column_width + (column_width - (text_box[2] - text_box[0])) // 2, 35),
        heading,
        font=title_font,
        fill=(238, 238, 238, 255),
    )

draw.line((40, baseline, 1460, baseline), fill=(90, 90, 90, 255), width=1)
note = "Integer nearest-neighbor enlargement of the proposed logical body heights"
note_box = draw.textbbox((0, 0), note, font=note_font)
draw.text(
    ((logical_board.width - (note_box[2] - note_box[0])) // 2, 515),
    note,
    font=note_font,
    fill=(170, 170, 170, 255),
)
logical_board.save(ROOT / "comparison-logical-density.png")
