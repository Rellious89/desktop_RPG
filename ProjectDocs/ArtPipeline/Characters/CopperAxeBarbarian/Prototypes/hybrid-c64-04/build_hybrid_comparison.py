from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent
PREVIOUS = ROOT.parent / "class-lineup-03"
SOURCE = ROOT / "hybrid-c64-lineup-transparent.png"

CHARACTERS = (
    ("Barbarian", "barbarian"),
    ("Cat Mage", "cat-mage"),
    ("Cleric", "cleric"),
    ("Police", "police"),
    ("Maid", "maid"),
    ("Tiger", "tiger"),
)

# Empty horizontal gaps in the generated transparent lineup.
X_RANGES = (
    (60, 433),
    (486, 812),
    (877, 1102),
    (1211, 1434),
    (1538, 1741),
    (1786, 2082),
)


def hard_alpha(image: Image.Image) -> Image.Image:
    image = image.convert("RGBA")
    red, green, blue, alpha = image.split()
    alpha = alpha.point(lambda value: 255 if value >= 128 else 0)
    return Image.merge("RGBA", (red, green, blue, alpha))


def trim(image: Image.Image) -> Image.Image:
    bounds = image.getchannel("A").getbbox()
    if bounds is None:
        raise RuntimeError("Sprite contains no visible pixels.")
    return image.crop(bounds)


def fit_height(image: Image.Image, height: int) -> Image.Image:
    width = max(1, round(image.width * height / image.height))
    return image.resize((width, height), Image.Resampling.NEAREST)


def logical_nearest(image: Image.Image, canvas_height: int = 64) -> Image.Image:
    content_height = canvas_height - 4
    content_width = max(1, round(image.width * content_height / image.height))
    reduced = image.resize(
        (content_width, content_height), Image.Resampling.NEAREST
    )
    reduced = hard_alpha(reduced)
    canvas = Image.new("RGBA", (content_width + 4, canvas_height))
    canvas.alpha_composite(reduced, (2, 2))
    return canvas


def logical_clean(image: Image.Image, canvas_height: int = 64) -> Image.Image:
    """Downsample to a real 64px grid, then remove blended micro-colors.

    This is a conversion test rather than a hand-authored final sprite. BOX
    retains more small facial and equipment information than arbitrary nearest
    sampling; palette quantization and a hard alpha edge restore pixel clarity.
    """

    content_height = canvas_height - 4
    content_width = max(1, round(image.width * content_height / image.height))
    reduced = image.resize((content_width, content_height), Image.Resampling.BOX)
    alpha = reduced.getchannel("A").point(lambda value: 255 if value >= 104 else 0)
    reduced = reduced.quantize(
        colors=24,
        method=Image.Quantize.FASTOCTREE,
        dither=Image.Dither.NONE,
    ).convert("RGBA")
    reduced.putalpha(alpha)
    canvas = Image.new("RGBA", (content_width + 4, canvas_height))
    canvas.alpha_composite(reduced, (2, 2))
    return canvas


def load_font(size: int, bold: bool = False) -> ImageFont.ImageFont:
    filename = "Arial Bold.ttf" if bold else "Arial.ttf"
    try:
        return ImageFont.truetype(
            f"/System/Library/Fonts/Supplemental/{filename}", size
        )
    except OSError:
        return ImageFont.load_default()


source = hard_alpha(Image.open(SOURCE))
sprites: dict[str, Image.Image] = {}
nearest: dict[str, Image.Image] = {}
clean: dict[str, Image.Image] = {}

for (name, slug), (left, right) in zip(CHARACTERS, X_RANGES):
    sprite = trim(source.crop((left, 0, right, source.height)))
    sprites[slug] = sprite
    sprite.save(ROOT / f"hybrid-c64-{slug}-source.png")

    nearest_sprite = logical_nearest(sprite)
    nearest[slug] = nearest_sprite
    nearest_sprite.save(ROOT / f"hybrid-c64-{slug}-logical-nearest-64h.png")
    nearest_sprite.resize(
        (nearest_sprite.width * 6, nearest_sprite.height * 6),
        Image.Resampling.NEAREST,
    ).save(ROOT / f"hybrid-c64-{slug}-logical-nearest-64h-preview.png")

    clean_sprite = logical_clean(sprite)
    clean[slug] = clean_sprite
    clean_sprite.save(ROOT / f"hybrid-c64-{slug}-logical-clean-64h.png")
    clean_sprite.resize(
        (clean_sprite.width * 6, clean_sprite.height * 6),
        Image.Resampling.NEAREST,
    ).save(ROOT / f"hybrid-c64-{slug}-logical-clean-64h-preview.png")


def draw_source_vs_logical() -> None:
    width = 1920
    height = 820
    board = Image.new("RGBA", (width, height), (24, 24, 24, 255))
    draw = ImageDraw.Draw(board)
    title_font = load_font(24, bold=True)
    label_font = load_font(20)
    note_font = load_font(17)
    column_width = width // len(CHARACTERS)
    top_baseline = 365
    bottom_baseline = 735

    for column, (name, slug) in enumerate(CHARACTERS):
        box = draw.textbbox((0, 0), name, font=title_font)
        text_width = box[2] - box[0]
        draw.text(
            (column * column_width + (column_width - text_width) // 2, 25),
            name,
            font=title_font,
            fill=(238, 238, 238, 255),
        )

        original = fit_height(sprites[slug], 220)
        original_x = column * column_width + (column_width - original.width) // 2
        board.alpha_composite(original, (original_x, top_baseline - original.height))

        logical = clean[slug].resize(
            (clean[slug].width * 5, clean[slug].height * 5),
            Image.Resampling.NEAREST,
        )
        logical_x = column * column_width + (column_width - logical.width) // 2
        board.alpha_composite(logical, (logical_x, bottom_baseline - logical.height))

    draw.text((20, 83), "Generated concept source", font=label_font, fill=(180, 210, 255, 255))
    draw.text((20, 405), "Actual 64px logical grid · clean conversion", font=label_font, fill=(255, 200, 115, 255))
    draw.line((20, top_baseline, width - 20, top_baseline), fill=(80, 80, 80, 255))
    draw.line((20, bottom_baseline, width - 20, bottom_baseline), fill=(80, 80, 80, 255))
    note = "The lower row is 64px source data enlarged 5x with nearest-neighbor display."
    box = draw.textbbox((0, 0), note, font=note_font)
    draw.text(((width - (box[2] - box[0])) // 2, 785), note, font=note_font, fill=(165, 165, 165, 255))
    board.save(ROOT / "comparison-hybrid-c64-source-vs-logical.png")


def draw_c48_vs_c64() -> None:
    width = 1920
    height = 800
    board = Image.new("RGBA", (width, height), (24, 24, 24, 255))
    draw = ImageDraw.Draw(board)
    title_font = load_font(24, bold=True)
    label_font = load_font(20)
    note_font = load_font(17)
    column_width = width // len(CHARACTERS)
    top_baseline = 350
    bottom_baseline = 715

    for column, (name, slug) in enumerate(CHARACTERS):
        box = draw.textbbox((0, 0), name, font=title_font)
        text_width = box[2] - box[0]
        draw.text(
            (column * column_width + (column_width - text_width) // 2, 25),
            name,
            font=title_font,
            fill=(238, 238, 238, 255),
        )

        old = Image.open(PREVIOUS / f"low-c-{slug}-logical-48h.png").convert("RGBA")
        old_display = old.resize(
            (old.width * 6, old.height * 6), Image.Resampling.NEAREST
        )
        old_x = column * column_width + (column_width - old_display.width) // 2
        board.alpha_composite(old_display, (old_x, top_baseline - old_display.height))

        new_display = clean[slug].resize(
            (clean[slug].width * 5, clean[slug].height * 5),
            Image.Resampling.NEAREST,
        )
        new_x = column * column_width + (column_width - new_display.width) // 2
        board.alpha_composite(new_display, (new_x, bottom_baseline - new_display.height))

    draw.text((20, 82), "Previous LOW-C · 48px", font=label_font, fill=(255, 180, 105, 255))
    draw.text((20, 392), "Hybrid C proportions · 64px", font=label_font, fill=(120, 205, 255, 255))
    draw.line((20, top_baseline, width - 20, top_baseline), fill=(80, 80, 80, 255))
    draw.line((20, bottom_baseline, width - 20, bottom_baseline), fill=(80, 80, 80, 255))
    note = "Both rows show their true logical pixels with integer nearest-neighbor enlargement."
    box = draw.textbbox((0, 0), note, font=note_font)
    draw.text(((width - (box[2] - box[0])) // 2, 765), note, font=note_font, fill=(165, 165, 165, 255))
    board.save(ROOT / "comparison-c48-vs-hybrid-c64-logical.png")


draw_source_vs_logical()
draw_c48_vs_c64()
