# Evidence points: first-pass report

Generated 2026-10-18. File: `evidence_points.csv` (4,861 rows: 152 first-pass Pleiades/Wikidata rows plus 4,709 `nasa_urban_pop` rows). Sources used: `pleiades`, `wikidata`, `nasa_urban_pop` (all APPROVED; the owner approved `nasa_urban_pop` on 2026-10-18).

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

## Addendum: NASA SEDAC Historical Urban Population

- Input: `docs/SOURCES/nasa/urbanspatial-hist-urban-pop-3700bc-ad2000-xlsx.xlsx` (SHA-256 `7355dd9d3ce162f98db90b4806cd0cb1f0209dca8e9108a34ee0a336f91f168e`), sheet "Historical Urban Population", 10,352 records, years -3700 to 2000. The data does start at 3700 BC; the catalog's "Temporal Extent 1700-2000" field is wrong or truncated.
- Method: each record falls in one of 21 bins (labels like `NASA500BCE-251BCE`). One row per place (lat/lon rounded to 2 decimals) per bin, keeping the largest population. Weight/radius use the same population tier table as above. Confidence maps the source's geocode Certainty (1 HIGH, 2 MEDIUM, 3 LOW); it rates location accuracy, not population accuracy.
- Counts: 3,747 HIGH, 519 MEDIUM, 443 LOW. Per bin: 3700-3001 BCE 4, 3000-2501 15, 2500-2001 31, 2000-1501 40, 1500-1001 33, 1000-751 17, 750-501 18, 500-251 50, 250-1 52, 1-250 CE 26, 251-500 42, 501-750 75, 751-1000 159, 1001-1250 228, 1251-1500 337, 1501-1650 388, 1651-1800 600, 1801-1875 645, 1876-1925 1,138, 1926-1975 518, 1976-2000 293.
- Regions (NASA rows, rough box): Europe/N. Africa/Near East 2,477; East/SE Asia 824; Persia/Central/South Asia 745; Americas 549; Sub-Saharan Africa 101; Oceania 13.
- Validation: `read_evidence` accepts all 4,861 rows; 4 unit tests OK. I did not run the full generator (no `ne_raster.npz` was located).
- Caveats: pre-1000 CE is thin (under 100 points per bin), so the 1700-2000 era dominates the demand map by count. Pre-1700 values are compiled estimates (Chandler, Modelski). The dataset ends at 2000, so the 2020CE rows still come from Wikidata. Political complexity and corridors after 640 CE remain empty.
- Attribution required: Reba, Reitsma & Seto (2018), doi:10.7927/H4ZG6QBX.
