"""Генерация иконки приложения ApkToPhone (assets/app.ico)."""

from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parents[1] / "assets" / "app.ico"

BLUE = (37, 99, 235, 255)
DARK = (30, 64, 175, 255)
WHITE = (255, 255, 255, 255)

SIZE = 512


def draw_icon() -> Image.Image:
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # корпус телефона
    d.rounded_rectangle((28, 28, SIZE - 28, SIZE - 28), radius=112, fill=BLUE)
    # динамик
    d.rounded_rectangle((SIZE // 2 - 46, 58, SIZE // 2 + 46, 76), radius=9, fill=DARK)

    # экран
    d.rounded_rectangle((78, 104, SIZE - 78, SIZE - 78), radius=44, fill=WHITE)

    # стрелка установки
    cx = SIZE // 2
    d.rounded_rectangle((cx - 17, 150, cx + 17, 290), radius=17, fill=BLUE)
    d.polygon([(cx - 62, 268), (cx + 62, 268), (cx, 352)], fill=BLUE)

    # «полка» под стрелкой
    d.rounded_rectangle((cx - 92, 380, cx + 92, 404), radius=12, fill=BLUE)

    return img


def main() -> None:
    OUT.parent.mkdir(parents=True, exist_ok=True)
    icon = draw_icon()
    icon.save(
        OUT,
        format="ICO",
        sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)],
    )
    print(f"written {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
