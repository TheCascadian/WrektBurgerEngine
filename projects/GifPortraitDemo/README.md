# GifPortraitDemo

A tiny scenario for testing animated and conditional character portraits. It was split out of Earth1930 so that
the real scenario carries no test media.

## What is in it

| File | Purpose |
|------|---------|
| `game/gfx/portraits/ah.gif` | 216 x 272 (twice the 108 x 136 dossier portrait), 20 frames at 0.1 s. Black bars top and bottom keep the picture inside the portrait box. |
| `game/history/characters/characters.txt` | Two characters. Alice shows the gif as her `portrait`. Bob has no default portrait; a `portraits` rule gives him the gif when his health falls below 40. |

## How it works

- `portrait = "path"` is the default image. A `.gif` animates while the character is selected in the Characters panel;
  the roster list shows the still first frame. `.mp4` works too (needs `ffmpeg` and `ffprobe`; see `docs/MODDING.md`, Portraits).
- `portraits = { name = { image = "path" trigger = { ... } } }` is checked top to bottom each day; the first rule
  whose trigger holds supplies the image, otherwise `portrait` is used.
- The `set_portrait` effect changes the default image from script.

## Running it

```
./burger.sh run GifPortraitDemo
```

Open the Characters panel and select Alice. To see the rule, lower Bob's health (for example with a console
command) and check that his portrait changes.

## Notes

- Frames are decoded at their file size, so keep portrait gifs small.
- The gif is decoded by raylib (`LoadImageAnim`); frame delays are read from the file's graphic control extensions.
