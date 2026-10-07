"""
Regenerates src/HAMMOR.App/Assets/Fonts/AlexandriaLight.ttf, the Living
Home's display face (the approved canvas sets its greeting in Alexandria
Light).

Optional, developer-only tooling: the generated font is committed, so the app
never needs this script to build. Requires Python 3.9+ and fontTools:

    pip install fonttools
    python scripts/subset-display-font.py Alexandria[wght].ttf

The input is Google Fonts' variable Alexandria (SIL Open Font License 1.1):
https://github.com/google/fonts/tree/main/ofl/alexandria

The script pins the weight axis at 300 (Light), names the family
"Alexandria Light" so WPF can address it as
pack://application:,,,/Assets/Fonts/#Alexandria Light, and keeps Basic Latin,
Latin-1, general punctuation, the euro sign and Arabic with its presentation
forms, together with every layout feature, so Arabic still shapes.
"""

import os
import sys

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

FAMILY = "Alexandria Light"
OUTPUT = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "src", "HAMMOR.App", "Assets", "Fonts", "AlexandriaLight.ttf")

UNICODES = (
    list(range(0x20, 0x7F))         # Basic Latin
    + list(range(0xA0, 0x100))      # Latin-1
    + list(range(0x2000, 0x2070))   # general punctuation
    + [0x20AC]                      # euro sign
    + list(range(0x600, 0x700))     # Arabic
    + list(range(0x750, 0x780))     # Arabic supplement
    + list(range(0xFB50, 0xFE00))   # Arabic presentation forms A
    + list(range(0xFE70, 0xFF00)))  # Arabic presentation forms B


def main(source):
    font = instancer.instantiateVariableFont(
        TTFont(source), {"wght": 300}, updateFontNames=False)
    font["OS/2"].usWeightClass = 300

    names = font["name"]
    for record in names.names:
        if record.nameID in (1, 4):
            record.string = FAMILY
        elif record.nameID == 2:
            record.string = "Regular"
        elif record.nameID == 3:
            record.string = "HAMMOR-AlexandriaLight-subset"
        elif record.nameID == 6:
            record.string = "AlexandriaLight-Regular"
    # Typographic family names would make WPF see "Alexandria" again.
    names.names = [r for r in names.names if r.nameID not in (16, 17, 25)]

    options = subset.Options()
    options.layout_features = ["*"]
    options.name_IDs = ["*"]
    options.name_languages = ["*"]
    options.notdef_outline = True
    options.glyph_names = False
    options.hinting = False

    subsetter = subset.Subsetter(options)
    subsetter.populate(unicodes=UNICODES)
    subsetter.subset(font)

    font.save(OUTPUT)
    print(f"wrote {os.path.normpath(OUTPUT)} ({os.path.getsize(OUTPUT):,} bytes)")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit("usage: python scripts/subset-display-font.py Alexandria[wght].ttf")
    main(sys.argv[1])
