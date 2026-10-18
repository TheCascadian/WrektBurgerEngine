# Province detail-demand map

`build_detail_demand.py` creates a deterministic, coarse raster that describes where the province generator should concentrate geographical detail. It does not draw historical borders, assign ownership, or generate the province network itself. It performs no network access.

## Inputs

- `ne_raster.npz`: output from `rasterize_ne.py`, containing `land`, `lake`, and `river` arrays at the configured world-grid size.
- `gebco_grid.npz` (optional): output from `gebco_to_grid.py`, containing `mean` elevation in meters.
- `evidence_points.csv` (optional): point evidence curated from approved sources. Start with `evidence_points.template.csv`.
- `provenance/sources.csv`: each `source_ids` value must refer to a row whose status is `APPROVED`.

The source collection in `docs/SOURCES` is a research ledger, not a ready-to-use global point dataset. Until actual point records are curated, the tool can only make a physical-geography baseline from coast and river proximity. That output is not historically validated.

## Run

```bash
python3 tools/map500bce/build_detail_demand.py \
  --base-raster /path/to/ne_raster.npz \
  --elevation /path/to/gebco_grid.npz \
  --evidence tools/map500bce/evidence_points.csv \
  --sources tools/map500bce/provenance/sources.csv \
  --output work/detail_demand.npz
```

Omit `--elevation` or `--evidence` when unavailable. The `--downsample` default is 4, matching `make_locations.py`'s default coarse grid. The inputs must match the grid selected by `MAP_W` in `grid.py`.

## Evidence CSV

| Column | Meaning |
|---|---|
| `id` | Unique ID for this evidence-point row. Use a different ID for a different period. |
| `kind` | `settlement`, `political_complexity`, or `strategic_corridor`. |
| `lon`, `lat` | Longitude and latitude in decimal degrees. |
| `weight` | Curator-assigned relevance from 0 to 1, justified in `notes`; it is not automatically inferred by the script. |
| `radius_km` | Model influence radius, 1 to 2500 km. The Gaussian influence is cut off at this radius. This is a tuning parameter, not a copied boundary. |
| `period` | A period label such as `500BCE`, `300BCE`, `1000CE`, `1900CE`; use `SHARED` only when the evidence is valid across the timeline. Labels are grouping keys, not date parsers. |
| `confidence` | `HIGH`, `MEDIUM`, `LOW`, or `DISPUTED`. Disputed rows are excluded. |
| `source_ids` | One or more source-ledger IDs separated by semicolons. Every referenced source must have status `APPROVED`. |
| `source_locator` | Exact record ID, page, section, or URL fragment in the source. Required for auditability. |
| `notes` | Explain why the point, weight, radius, and period are appropriate. |

Consolidate duplicate records for the same place and period before running this tool. Otherwise duplicate source records can artificially increase the local score. The script rejects unapproved sources, empty locators, duplicate row IDs, invalid coordinates, invalid weights, and malformed rows.

## Outputs

- `detail_demand.npz`: machine-readable layers. `demand` is the main 0 to 1 score; `boundary_affinity` is a separate 0 to 1 signal for rivers and ridges; `evidence_coverage` shows where sourced evidence actually contributes. Other arrays preserve individual layers for inspection.
- `detail_demand.png`: grayscale demand preview, with water black and land values brighter as demand increases.
- `detail_demand.boundary.png`: grayscale boundary-affinity preview.
- `detail_demand.coverage.png`: grayscale preview of where the evidence points contribute, not a claim that the area had low historical importance where the image is dark.
- `detail_demand.report.json`: parameters, source counts, evidence periods, score statistics, input basenames, and limitations.

The score includes a nonzero floor on land. Where evidence exists, the evidence component receives 80% of the score by default and physical coast/river proximity 20%. When no usable evidence is provided, only the physical geography baseline is used. Settlement, complexity, and strategic evidence are balanced using their active configured weights. Demands from different periods are combined by maximum, so a region needing detail in one well-supported period is not averaged away by periods where it was less important.

`boundary_affinity` is intentionally separate from demand: high demand means smaller province targets, while boundary affinity can later guide the placement of province edges toward rivers and positive terrain-relative ridges. It is not yet wired into `make_locations.py`.

## Important limitation

The present source folder contains references, license notes, and research leads, but it does not provide the comprehensive, curated point layer needed to claim a historically balanced world. Missing evidence is not evidence that a region was unimportant. Review the coverage layer and report, populate sourced points across several eras and regions, and only then use the raster to tune the province network.
