"""Generate assets/icon.png, the NuGet package icon. Run: python3 assets/make-icon.py"""
from PIL import Image, ImageDraw, ImageFont
import os

HERE = os.path.dirname(os.path.abspath(__file__))
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
S = 1024                      # render large, downscale for clean edges
BG, FG, ACCENT = (11, 60, 93), (255, 255, 255), (111, 207, 151)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((0, 0, S - 1, S - 1), radius=S // 5, fill=BG)

font = ImageFont.truetype(FONT, 330)
for text, y in (("TC", 190), ("MF", 470)):
    box = d.textbbox((0, 0), text, font=font)
    w = box[2] - box[0]
    d.text(((S - w) / 2 - box[0], y - box[1]), text, font=font, fill=FG)

# timeline: a line with three stops, the sequence of events a TCMF message stream describes
y = 860
d.line((150, y, S - 150, y), fill=ACCENT, width=22)
for x in (150, S // 2, S - 150):
    d.ellipse((x - 42, y - 42, x + 42, y + 42), fill=BG, outline=ACCENT, width=22)
d.ellipse((S // 2 - 20, y - 20, S // 2 + 20, y + 20), fill=ACCENT)

img.resize((128, 128), Image.LANCZOS).save(os.path.join(HERE, "icon.png"), optimize=True)
img.resize((512, 512), Image.LANCZOS).save(os.path.join(HERE, "icon-512.png"), optimize=True)
