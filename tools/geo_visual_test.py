#!/usr/bin/env python3
"""Automated visual test of the historical geography layer in the real game.

Makes a COPY of projects/World500BCE, generates synthetic geography into it, launches the game
for every check, takes screenshots and compares pixels. Prints PASS/FAIL per check, writes a
contact sheet and exits non-zero on any failure. Nothing in projects/ is modified.

Run through wrun (it opens windows and must not overlap other timing work):
  ~/bin/wrun --name geovis --mode generic --exclusive -- python3 tools/geo_visual_test.py [--keep] [--zoom N]
Needs: python3 with Pillow; Release builds of Burger.Game and Burger.Tools; DOTNET_ROLL_FORWARD=Major
when only .NET 9 is installed (set below).
"""
import argparse, os, shutil, subprocess, sys, tempfile
from PIL import Image, ImageChops

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
GAME = os.path.join(ROOT, "src/Burger.Game/bin/Release/net8.0/Burger.Game")
TOOL = os.path.join(ROOT, "src/Burger.Tools/bin/Release/net8.0/burger")
ENV = dict(os.environ, DOTNET_ROLL_FORWARD="Major")
SIZE = (1600, 900)

ap = argparse.ArgumentParser()
ap.add_argument("--keep", action="store_true", help="keep the work folder")
ap.add_argument("--zoom", type=float, default=40, help="camera distance for close views (default 40)")
ap.add_argument("--wide", type=float, default=400, help="camera distance for wide views")
ap.add_argument("--work", default=None)
args = ap.parse_args()

work = args.work or tempfile.mkdtemp(prefix="geovis_")
os.makedirs(work, exist_ok=True)
copy = os.path.join(work, "W500")
plain = os.path.join(work, "W500_plain")      # copy WITHOUT geography: the regression baseline
shots = os.path.join(work, "shots")
os.makedirs(shots, exist_ok=True)
for d in (copy, plain):
    if os.path.exists(d): shutil.rmtree(d)
    shutil.copytree(os.path.join(ROOT, "projects/World500BCE"), d)

def run(cmd, **kw):
    return subprocess.run(cmd, env=ENV, text=True, capture_output=True, **kw)

for exe in (GAME, TOOL):
    if not os.path.exists(exe): sys.exit(f"missing build: {exe}")
r = run([TOOL, "geo", "generate", copy, "--synthetic", "--force"])
print(r.stdout.strip().splitlines()[-1] if r.stdout.strip() else r.stderr[-300:])
if r.returncode != 0: sys.exit("geo generate failed:\n" + r.stdout[-1500:] + r.stderr[-1500:])

n = 0
def shot(project, mode, date, lon, lat, zoom, tag):
    """one game launch -> one screenshot; returns the PIL image"""
    global n
    n += 1
    out = os.path.join(shots, f"{n:02d}_{tag}.png")
    cmd = [GAME, "--observe", "--game", os.path.join(project, "game"), "--size", f"{SIZE[0]}x{SIZE[1]}",
           "--frames", "150", "--mapmode", mode, "--lookat", f"{lon},{lat}", "--zoom", str(zoom), "--screenshot", out]
    if date: cmd += ["--exec", f"geodate {date}"]
    run(cmd, timeout=180)
    if not os.path.exists(out): sys.exit(f"no screenshot produced for {tag}")
    return Image.open(out).convert("RGB")

def changed(a, b, thr=40):
    """number of pixels whose largest channel difference exceeds thr, ignoring the HUD bar and map-mode label"""
    d = ImageChops.difference(a, b).convert("L").point(lambda v: 255 if v > thr else 0)
    d.paste(0, (0, 0, SIZE[0], 48)); d.paste(0, (SIZE[0] - 220, SIZE[1] - 60, SIZE[0], SIZE[1]))
    return sum(1 for v in d.getdata() if v)

def mean_rgb(img, box):
    c = img.crop(box).resize((1, 1), Image.BOX).getpixel((0, 0))
    return c

results = []
def check(name, ok, detail):
    results.append((name, ok, detail))
    print(("PASS  " if ok else "FAIL  ") + name + "  -  " + detail, flush=True)

# 1. noise floor: the same view twice must be (nearly) identical
V = (60, 35)
a = shot(copy, "Provinces", "300BCE.1.1", *V, args.wide, "noise_a")
b = shot(copy, "Provinces", "300BCE.1.1", *V, args.wide, "noise_b")
noise = changed(a, b)
check("repeatability: same date twice", noise < 2000, f"{noise} px differ (noise floor)")
floor = max(noise * 3, 200)

# 2. the mode is real: Provinces differs from Political on the same copy and has many more borders
pol = shot(copy, "Political", None, *V, args.wide, "political")
pv = shot(copy, "Provinces", "500BCE.1.1", *V, args.wide, "prov_500bce")
d = changed(pol, pv)
check("Provinces mode differs from Political", d > floor, f"{d} px differ (floor {floor})")

# 3. regression: a project WITHOUT geography renders Political exactly like the copy WITH geography
base = shot(plain, "Political", None, *V, args.wide, "plain_political")
d = changed(pol, base)
check("regression: Political unchanged by the layer", d <= floor, f"{d} px differ (floor {floor})")
base2 = shot(plain, "Provinces", None, *V, args.wide, "plain_provinces")
d = changed(base, base2)
check("no geography: Provinces request falls back to Political", d <= floor, f"{d} px differ (floor {floor})")

# 4. time sweep over the whole synthetic timeline: change exactly where an event is dated
sweep = [("400BCE.1.1", None), ("100BCE.1.1", True), ("50BCE.1.1", False), ("10BCE.1.1", True),
         ("10CE.1.1", True), ("100CE.1.1", False), ("600CE.1.1", True), ("700CE.1.1", False),
         ("900CE.1.1", True), ("1100.1.1", False), ("1300.1.1", True), ("1400.1.1", False), ("1600.1.1", True)]
# (date, expected change versus the previous date): events at 200BCE split, 20BCE controller, 1CE split+owner,
# 500CE split, 800CE merge, 1200 split, 1500 redraw. Drawn at world scale so all events are in view.
prev = None
for date, expect in sweep:
    img = shot(copy, "Provinces", date, 20, 35, args.wide * 2.5, "sweep_" + date.replace(".", "_"))
    if prev is not None:
        d = changed(prev[1], img, thr=25)
        ok = d > floor if expect else d <= floor
        check(f"sweep {prev[0]} -> {date}: {'change' if expect else 'no change'} expected", ok, f"{d} px differ (floor {floor})")
    prev = (date, img)

# 5. close views: a province split and an owner change must be visible where they happen
cases = [("split Bactria 200BCE", 68, 38, "300BCE.1.1", "100BCE.1.1"),
         ("owner change Anatolia 1CE", 33, 39, "10BCE.1.1", "10CE.1.1"),
         ("split South Arabia 500CE", 47, 16, "400CE.1.1", "600CE.1.1")]
sheet = []
for name, lon, lat, d1, d2 in cases:
    t = name.split()[1].lower()
    a = shot(copy, "Provinces", d1, lon, lat, args.zoom * 3, "close_" + t + "_before")
    b = shot(copy, "Provinces", d2, lon, lat, args.zoom * 3, "close_" + t + "_after")
    k = shot(copy, "Provinces", d1, lon, lat, args.zoom * 3, "close_" + t + "_control")
    d, dc = changed(a, b), changed(a, k)
    check(name, d > max(dc * 3, 150), f"{d} px differ across the event, {dc} in the repeat control")
    sheet += [a, b]

# 6. contact sheet
w, h = 480, 270
cs = Image.new("RGB", (w * 2, h * (len(sheet) // 2)), (20, 20, 20))
for i, im in enumerate(sheet):
    cs.paste(im.resize((w, h)), ((i % 2) * w, (i // 2) * h))
cs.save(os.path.join(work, "contact_sheet.png"))

fails = [r for r in results if not r[1]]
print(f"\n{len(results) - len(fails)}/{len(results)} checks passed")
print(f"screenshots: {shots}\ncontact sheet (before | after per row): {os.path.join(work, 'contact_sheet.png')}")
if not args.keep and not fails and not args.work:
    print("(use --keep to keep the work folder)")
sys.exit(1 if fails else 0)
