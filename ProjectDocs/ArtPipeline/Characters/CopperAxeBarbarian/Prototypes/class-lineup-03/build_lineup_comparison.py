from pathlib import Path
from collections import deque

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent

CHARACTERS = (
    ("Barbarian", "barbarian"),
    ("Cat Mage", "cat-mage"),
    ("Cleric", "cleric"),
    ("Police", "police"),
    ("Maid", "maid"),
    ("Tiger", "tiger"),
)

# The generated sheets use the same 2172px-wide lineup, but long weapons make
# some silhouettes overlap on the horizontal projection. These boundaries are
# the clear visual gaps between neighboring characters in each source sheet.
BOUNDARIES = {
    "b": (0, 452, 840, 1180, 1510, 1770, 2172),
    "c": (0, 445, 850, 1165, 1505, 1785, 2172),
}

SOURCES = {
    "b": ROOT / "low-b-lineup-transparent.png",
    "c": ROOT / "low-c-lineup-transparent.png",
}

LOGICAL_HEIGHTS = {"b": 64, "c": 48}


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


def keep_largest_component(image: Image.Image) -> Image.Image:
    """Remove detached fragments from neighboring lineup characters."""
    image = image.convert("RGBA")
    alpha = image.getchannel("A")
    width, height = image.size
    visible = alpha.load()
    visited = bytearray(width * height)
    components: list[list[tuple[int, int]]] = []

    for y in range(height):
        for x in range(width):
            index = y * width + x
            if visited[index] or visible[x, y] == 0:
                continue
            visited[index] = 1
            queue = deque([(x, y)])
            component: list[tuple[int, int]] = []
            while queue:
                current_x, current_y = queue.popleft()
                component.append((current_x, current_y))
                for offset_x, offset_y in (
                    (-1, -1),
                    (0, -1),
                    (1, -1),
                    (-1, 0),
                    (1, 0),
                    (-1, 1),
                    (0, 1),
                    (1, 1),
                ):
                    next_x = current_x + offset_x
                    next_y = current_y + offset_y
                    if not (0 <= next_x < width and 0 <= next_y < height):
                        continue
                    next_index = next_y * width + next_x
                    if visited[next_index] or visible[next_x, next_y] == 0:
                        continue
                    visited[next_index] = 1
                    queue.append((next_x, next_y))
            components.append(component)

    if not components:
        raise RuntimeError("Sprite contains no connected opaque pixels.")
    largest = max(components, key=len)
    mask = Image.new("L", image.size)
    mask_pixels = mask.load()
    for x, y in largest:
        mask_pixels[x, y] = 255
    image.putalpha(mask)
    return trim(image)


def fit_height(image: Image.Image, height: int) -> Image.Image:
    width = max(1, round(image.width * height / image.height))
    return image.resize((width, height), Image.Resampling.NEAREST)


def logical_version(image: Image.Image, height: int) -> Image.Image:
    content_height = height - 4
    content_width = max(1, round(image.width * content_height / image.height))
    reduced = image.resize(
        (content_width, content_height), Image.Resampling.NEAREST
    )
    logical = Image.new("RGBA", (content_width + 4, height))
    logical.alpha_composite(reduced, (2, 2))
    return logical


def load_font(size: int, bold: bool = False) -> ImageFont.ImageFont:
    filename = "Arial Bold.ttf" if bold else "Arial.ttf"
    try:
        return ImageFont.truetype(
            f"/System/Library/Fonts/Supplemental/{filename}", size
        )
    except OSError:
        return ImageFont.load_default()


sprites: dict[str, dict[str, Image.Image]] = {"b": {}, "c": {}}
logical: dict[str, dict[str, Image.Image]] = {"b": {}, "c": {}}

for grade in ("b", "c"):
    source = hard_alpha(Image.open(SOURCES[grade]))
    boundaries = BOUNDARIES[grade]
    if source.width != boundaries[-1]:
        raise RuntimeError(
            f"Unexpected {grade.upper()} sheet width: {source.width}"
        )

    for index, (_, slug) in enumerate(CHARACTERS):
        sprite = keep_largest_component(
            trim(
                source.crop(
                    (
                        boundaries[index],
                        0,
                        boundaries[index + 1],
                        source.height,
                    )
                )
            )
        )
        sprites[grade][slug] = sprite
        sprite.save(ROOT / f"low-{grade}-{slug}-source.png")

        reduced = logical_version(sprite, LOGICAL_HEIGHTS[grade])
        logical[grade][slug] = reduced
        reduced.save(
            ROOT
            / f"low-{grade}-{slug}-logical-{LOGICAL_HEIGHTS[grade]}h.png"
        )

        inspection_scale = 6 if grade == "b" else 8
        reduced.resize(
            (
                reduced.width * inspection_scale,
                reduced.height * inspection_scale,
            ),
            Image.Resampling.NEAREST,
        ).save(
            ROOT
            / f"low-{grade}-{slug}-logical-{LOGICAL_HEIGHTS[grade]}h-preview.png"
        )


def draw_board(use_logical: bool, output: Path) -> None:
    width = 1920
    height = 760
    board = Image.new("RGBA", (width, height), (24, 24, 24, 255))
    draw = ImageDraw.Draw(board)
    title_font = load_font(25, bold=True)
    label_font = load_font(20)
    note_font = load_font(17)
    column_width = width // 6
    top_baseline = 355
    bottom_baseline = 680

    for column, (name, slug) in enumerate(CHARACTERS):
        name_box = draw.textbbox((0, 0), name, font=title_font)
        name_width = name_box[2] - name_box[0]
        draw.text(
            (column * column_width + (column_width - name_width) // 2, 25),
            name,
            font=title_font,
            fill=(238, 238, 238, 255),
        )

        for row, grade in enumerate(("b", "c")):
            if use_logical:
                image = logical[grade][slug]
                scale = 4 if grade == "b" else 5
                display = image.resize(
                    (image.width * scale, image.height * scale),
                    Image.Resampling.NEAREST,
                )
            else:
                display = fit_height(sprites[grade][slug], 210)

            baseline = top_baseline if row == 0 else bottom_baseline
            x = column * column_width + (column_width - display.width) // 2
            board.alpha_composite(display, (x, baseline - display.height))

    row_label_x = 22
    draw.text(
        (row_label_x, 82),
        "LOW-B · 64px",
        font=label_font,
        fill=(120, 200, 255, 255),
    )
    draw.text(
        (row_label_x, 405),
        "LOW-C · 48px",
        font=label_font,
        fill=(255, 190, 110, 255),
    )
    draw.line((20, top_baseline, width - 20, top_baseline), fill=(80, 80, 80, 255))
    draw.line((20, bottom_baseline, width - 20, bottom_baseline), fill=(80, 80, 80, 255))

    note = (
        "Logical-grid stress test with integer nearest-neighbor enlargement"
        if use_logical
        else "Generated source art normalized to the same 210px visible height"
    )
    note_box = draw.textbbox((0, 0), note, font=note_font)
    note_width = note_box[2] - note_box[0]
    draw.text(
        ((width - note_width) // 2, 720),
        note,
        font=note_font,
        fill=(165, 165, 165, 255),
    )
    board.save(output)


draw_board(False, ROOT / "comparison-low-b-vs-c-source-210px.png")
draw_board(True, ROOT / "comparison-low-b-vs-c-logical.png")
