# Evidence points: first-pass report

Generated 2026-10-18. File: `evidence_points.csv` (152 rows). Sources used: `pleiades` and `wikidata` only (both APPROVED, license files present and hash-matched).

## Counts

| Kind | Rows |
|---|---|
| settlement | 145 |
| strategic_corridor | 7 |
| political_complexity | 0 |

| Period | Rows |
|---|---|
| 500BCE | 31 |
| 300BCE | 3 |
| 1CE | 41 |
| 500CE | 35 |
| 800CE | 1 |
| 1000CE | 1 |
| 1500CE | 4 |
| 1700CE | 6 |
| 1900CE | 9 |
| 2020CE | 21 |

| Region (by longitude/latitude box) | Rows |
|---|---|
| Europe / N. Africa / Near East | 106 |
| Persia / Central / South Asia | 22 |
| East / Southeast Asia / Oceania | 12 |
| Americas | 8 |
| Sub-Saharan Africa | 4 |

## Method

- Pleiades (JSON dump `pleiades-places-latest.json.gz`, retrieved 2026-10-18, SHA-256 `edbe14e63f19d11ca59e4def9c3404061a12981f0746eee8dad56657262f4f58`, not committed). Settlement rows use the place's `reprPoint` and a bin only when a name attestation range covers it (500BCE = -550..-330, 1CE = -30..300, 500CE = 300..640). Pleiades gives existence, not size, so all such rows use weight 0.4, radius 60 km, MEDIUM.
- Wikidata (`P625` coordinates, `P1082` population with `P585` point in time). Weight and radius come from a population tier table (in each row's notes). HIGH when the statement carries a reference, MEDIUM when it does not.
- Corridors: Pleiades place descriptions that cite a historical role (Thermopylae, Issus passes) are HIGH; those with only geography or name attestation are MEDIUM or LOW.
- Rome 500BCE is one merged row (Pleiades + Wikidata). No place appears twice in one period.

## Validation

- Generator's own `read_evidence` accepts all 152 rows (approved sources, locators, ranges, unique IDs).
- `python3 -m unittest test_build_detail_demand`: 4 tests OK.
- No duplicate coordinate/period pairs. No DISPUTED rows.
- Generator, map, and other project files unchanged.

## Gaps (not filled with guesses)

- political_complexity: no approved source states political fragmentation as data. Pleiades has no polity type; Seshat/Cliopatria are rejected/unverified; the `ref_*` maps are unverified-license. Zero rows.
- Americas, sub-Saharan Africa, East and Southeast Asia, Oceania before 1500CE: Pleiades does not cover them and Wikidata has no dated facts for e.g. Teotihuacan, Tikal, Cusco, Great Zimbabwe, Angkor, Chang'an in those eras.
- 300BCE, 1300CE, 1100-1400CE and 1900CE outside Europe/Americas are thin. 800CE and 1000CE have one row each.
- Strategic corridors after 640 CE: none. Wikidata has no dated relevance statements for passes, straits, or canals that I verified.
- The Wikidata population queries (SPARQL, 1 request per minute) timed out or were rate-limited for several windows; the 2000-2021 and 450-550 windows were not completed via SPARQL, so 2020CE rows were chosen by named entity lookups. Selection of cities is curated, not exhaustive, and skews to large modern cities.
- Period labels for rows whose data year differs from the label (Kyoto 1650 as 1700CE, Delhi 2011 as 2020CE, etc.) say so in notes.

## Unverified / needs owner approval

- `nasa_urban_pop` (UNVERIFIED): would give systematic city points 3700 BC-2000 AD; license text unread.
- `cliopatria` / `seshat`: would address political complexity; license NC or unresolved.
- Barrington Atlas / DARE, Wikidata SPARQL bulk pulls, and a Wikidata `instance of city-state` pull would help but need an owner decision.
- Unreferenced Wikidata population values (MEDIUM rows) should be checked against a primary publication before relying on them.
- Kyiv year-1000 statements conflict (10000 and 50000); the larger was used and noted.
