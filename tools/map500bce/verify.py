"""Provenance check for the 500 BCE map. Run: python3 -I tools/map500bce/verify.py [scenario_dir]

Fails when: a source row is incomplete or an APPROVED row lacks license text or hash, a feature cites a source that is not
APPROVED, a denylisted term appears in the scenario or generator files (docs are exempt), or an absolute path or email appears.
"""
import csv, hashlib, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROV = os.path.join(HERE, "provenance")
STATUSES = {"APPROVED", "PENDING OWNER", "UNVERIFIED", "REJECTED", "UNEXAMINED"}
errors = []

def fail(msg): errors.append(msg)

def rows(name):
    with open(os.path.join(PROV, name), newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))

sources = {r["id"]: r for r in rows("sources.csv")}
for sid, r in sources.items():
    if r["status"] not in STATUSES: fail(f"{sid}: bad status '{r['status']}'")
    if r["status"] == "APPROVED":
        for k in ("primary_url", "retrieval_date", "license", "license_url", "license_text_path", "sha256", "approved_by"):
            if not r[k]: fail(f"{sid}: APPROVED but {k} is empty")
        lp = os.path.join(PROV, r["license_text_path"] or "_missing_")
        if not os.path.isfile(lp): fail(f"{sid}: license text file missing")

for i, r in enumerate(rows("features.csv"), start=2):
    ids = [x for x in re.split(r"[;|]", r["source_ids"]) if x]
    if not ids: fail(f"features.csv line {i}: no source id")
    for sid in ids:
        if sid not in sources or sources[sid]["status"] != "APPROVED":
            fail(f"features.csv line {i}: source '{sid}' is not APPROVED")
    for k in ("source_locator", "fact_taken", "confidence"):
        if not r[k]: fail(f"features.csv line {i}: {k} is empty")

with open(os.path.join(PROV, "denylist.txt"), encoding="utf-8") as f:
    deny = [x.strip().lower() for x in f if x.strip()]
scan = [HERE] + [os.path.abspath(a) for a in sys.argv[1:]]
exempt = {os.path.join(PROV, "denylist.txt"), os.path.abspath(__file__)}
path_re = re.compile(r"(/home/|/Users/|[A-Za-z]:\\\\Users)|[\w.+-]+@[\w-]+\.[\w.]+")
for base in scan:
    for dp, _, fns in os.walk(base):
        for fn in fns:
            p = os.path.join(dp, fn)
            if p in exempt or fn.lower().endswith((".png", ".glb", ".npy", ".gz")): continue
            try: text = open(p, encoding="utf-8").read()
            except (UnicodeDecodeError, OSError): continue
            low = text.lower()
            for d in deny:
                if d in low and not p.startswith(PROV): fail(f"{os.path.relpath(p)}: contains denylisted term '{d}'")
            if path_re.search(text) and p != os.path.abspath(__file__): fail(f"{os.path.relpath(p)}: absolute path or email")

if errors:
    print("\n".join(errors)); sys.exit(1)
print(f"OK: {len(sources)} sources, no violations")
