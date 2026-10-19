#!/usr/bin/env python3
"""Build a deterministic, multi-era province-detail demand raster.

Inputs are frozen raster products and optional sourced point evidence. This script
never downloads data and never reads historical boundary geometries. Its output
is a design-demand field for later province allocation, not a historical border map.

Usage:
  python3 build_detail_demand.py --base-raster ne_raster.npz --output detail_demand.npz
  python3 build_detail_demand.py --base-raster ne_raster.npz --elevation gebco_grid.npz \
      --evidence evidence_points.csv --sources provenance/sources.csv --output detail_demand.npz

Dependencies: numpy, scipy, Pillow. Uses grid.py from the same directory when installed
in tools/map500bce/ so the input grid matches the existing map pipeline.
"""
from __future__ import annotations

import argparse
import csv
import json
import math
import os
import sys
from collections import defaultdict
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image
from scipy import ndimage

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from grid import H, W  # noqa: E402

EARTH_RADIUS_KM = 6371.0088
ANTARCTIC_LAT = -60.0  # Antarctica is excluded from the map; matches make_locations.py's polar rule
CONFIDENCE_FACTOR = {"HIGH": 1.0, "MEDIUM": 0.65, "LOW": 0.35, "DISPUTED": 0.0}
KIND_WEIGHT = {"settlement": 0.55, "political_complexity": 0.35, "strategic_corridor": 0.10}
EVIDENCE_FIELDS = {
    "id", "kind", "lon", "lat", "weight", "radius_km", "period",
    "confidence", "source_ids", "source_locator", "notes",
}


def log(*parts: object) -> None:
    print("[detail-demand]", *parts, flush=True)


def coarse_any(array: np.ndarray, factor: int) -> np.ndarray:
    h, w = array.shape
    if h % factor or w % factor:
        raise ValueError(f"array shape {array.shape} is not divisible by downsample factor {factor}")
    return array.reshape(h // factor, factor, w // factor, factor).any(axis=(1, 3))


def coarse_mean(array: np.ndarray, factor: int) -> np.ndarray:
    h, w = array.shape
    if h % factor or w % factor:
        raise ValueError(f"array shape {array.shape} is not divisible by downsample factor {factor}")
    return array.reshape(h // factor, factor, w // factor, factor).mean(axis=(1, 3), dtype=np.float32)


def read_raster(path: Path, key: str, expected_shape: tuple[int, int]) -> np.ndarray:
    with np.load(path, allow_pickle=False) as data:
        if key not in data.files:
            raise ValueError(f"{path.name} has no '{key}' array; contains: {', '.join(data.files)}")
        array = data[key]
    if array.ndim != 2 or array.shape != expected_shape:
        raise ValueError(f"{path.name}:{key} shape {array.shape}; expected {expected_shape}")
    return array


def point_row_col(lon: float, lat: float, width: int, height: int) -> tuple[int, int]:
    col = int(math.floor((lon + 180.0) * width / 360.0)) % width
    row = int(math.floor((90.0 - lat) * height / 180.0))
    return min(max(row, 0), height - 1), col


def distance_to_feature_km(feature: np.ndarray) -> np.ndarray:
    """Approximate great-circle distance to the nearest feature pixel.

    The nearest feature pixel is selected by the raster's equirectangular geometry;
    its actual distance is then measured on a sphere, with longitude wrap handled.
    Row chunks bound temporary memory use.
    """
    h, w = feature.shape
    if not feature.any():
        return np.full((h, w), np.inf, dtype=np.float32)
    nearest = ndimage.distance_transform_edt(~feature, return_distances=False, return_indices=True)
    nearest_rows, nearest_cols = nearest[0], nearest[1]
    del nearest
    row_lat = 90.0 - (np.arange(h, dtype=np.float64) + 0.5) * (180.0 / h)
    col_lon = (np.arange(w, dtype=np.float64) + 0.5) * (360.0 / w) - 180.0
    out = np.empty((h, w), dtype=np.float32)
    for r0 in range(0, h, 128):
        r1 = min(r0 + 128, h)
        here_lat = row_lat[r0:r1, None]
        near_lat = row_lat[nearest_rows[r0:r1]]
        dlat = np.radians(here_lat - near_lat)
        dlon_deg = (col_lon[None, :] - col_lon[nearest_cols[r0:r1]] + 180.0) % 360.0 - 180.0
        dlon = np.radians(dlon_deg)
        lat1 = np.radians(here_lat)
        lat2 = np.radians(near_lat)
        hav = np.sin(dlat / 2.0) ** 2 + np.cos(lat1) * np.cos(lat2) * np.sin(dlon / 2.0) ** 2
        out[r0:r1] = (2.0 * EARTH_RADIUS_KM * np.arcsin(np.sqrt(np.clip(hav, 0.0, 1.0)))).astype(np.float32)
    return out


def read_sources(path: Path) -> dict[str, str]:
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        if not reader.fieldnames or "id" not in reader.fieldnames or "status" not in reader.fieldnames:
            raise ValueError(f"{path.name} must have 'id' and 'status' columns")
        sources: dict[str, str] = {}
        for row in reader:
            source_id = (row.get("id") or "").strip()
            status = (row.get("status") or "").strip().upper()
            if source_id:
                sources[source_id] = status
    return sources


def read_evidence(path: Path, source_path: Path) -> tuple[list[dict[str, Any]], dict[str, int]]:
    statuses = read_sources(source_path)
    rows: list[dict[str, Any]] = []
    counts: dict[str, int] = defaultdict(int)
    used_ids: set[str] = set()
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        if not reader.fieldnames:
            raise ValueError(f"{path.name} has no header")
        missing = sorted(EVIDENCE_FIELDS - set(reader.fieldnames))
        if missing:
            raise ValueError(f"{path.name} is missing required columns: {', '.join(missing)}")
        for line_no, raw in enumerate(reader, start=2):
            if not any((value or "").strip() for value in raw.values()):
                continue
            row: dict[str, Any] = {key: (value or "").strip() for key, value in raw.items()}
            if not row["id"]:
                raise ValueError(f"{path.name}:{line_no}: id is required")
            if row["id"] in used_ids:
                raise ValueError(f"{path.name}:{line_no}: duplicate id '{row['id']}'")
            used_ids.add(row["id"])
            if row["kind"] not in KIND_WEIGHT:
                raise ValueError(f"{path.name}:{line_no}: kind must be one of {', '.join(KIND_WEIGHT)}")
            try:
                row["lon"] = float(row["lon"])
                row["lat"] = float(row["lat"])
                row["weight"] = float(row["weight"])
                row["radius_km"] = float(row["radius_km"])
            except ValueError as exc:
                raise ValueError(f"{path.name}:{line_no}: lon, lat, weight and radius_km must be numeric") from exc
            if not -180.0 <= row["lon"] <= 180.0:
                raise ValueError(f"{path.name}:{line_no}: lon must be between -180 and 180")
            if not -90.0 <= row["lat"] <= 90.0:
                raise ValueError(f"{path.name}:{line_no}: lat must be between -90 and 90")
            if not 0.0 <= row["weight"] <= 1.0:
                raise ValueError(f"{path.name}:{line_no}: weight must be between 0 and 1")
            if not 1.0 <= row["radius_km"] <= 2500.0:
                raise ValueError(f"{path.name}:{line_no}: radius_km must be between 1 and 2500")
            if not row["period"]:
                raise ValueError(f"{path.name}:{line_no}: period is required; use SHARED only for evidence intended to apply across the timeline")
            row["confidence"] = row["confidence"].upper()
            if row["confidence"] not in CONFIDENCE_FACTOR:
                raise ValueError(f"{path.name}:{line_no}: confidence must be HIGH, MEDIUM, LOW or DISPUTED")
            if not row["source_locator"]:
                raise ValueError(f"{path.name}:{line_no}: source_locator is required")
            if not row["notes"]:
                raise ValueError(f"{path.name}:{line_no}: notes must justify the point weight, radius and period")
            source_ids = [item.strip() for item in row["source_ids"].replace("|", ";").split(";") if item.strip()]
            if not source_ids:
                raise ValueError(f"{path.name}:{line_no}: source_ids must cite at least one source-ledger id")
            for source_id in source_ids:
                if statuses.get(source_id) != "APPROVED":
                    status = statuses.get(source_id, "NOT IN LEDGER")
                    raise ValueError(f"{path.name}:{line_no}: source '{source_id}' is {status}, not APPROVED")
            row["source_ids_list"] = source_ids
            rows.append(row)
            counts[row["kind"]] += 1
    return rows, dict(counts)


def rasterize_points(
    points: list[dict[str, Any]],
    shape: tuple[int, int],
    land_mask: np.ndarray,
) -> tuple[np.ndarray, np.ndarray, dict[str, int]]:
    """Accumulate sourced point influence using spherical distance kernels.

    radius_km is the cutoff radius; Gaussian sigma is radius_km / 3. Weights and
    confidence factors scale the point before overlapping observations are combined.
    """
    h, w = shape
    influence = np.zeros(shape, dtype=np.float32)
    coverage = np.zeros(shape, dtype=np.float32)
    stats = {"points": len(points), "disputed_skipped": 0, "point_centers_on_water": 0}
    for point in points:
        confidence = CONFIDENCE_FACTOR[point["confidence"]]
        if confidence == 0.0:
            stats["disputed_skipped"] += 1
            continue
        lon, lat = point["lon"], point["lat"]
        radius = point["radius_km"]
        row, col = point_row_col(lon, lat, w, h)
        if not land_mask[row, col]:
            stats["point_centers_on_water"] += 1
        lat_span = math.degrees(radius / EARTH_RADIUS_KM)
        row_radius = int(math.ceil(lat_span * h / 180.0)) + 1
        r0, r1 = max(0, row - row_radius), min(h, row + row_radius + 1)
        rows = np.arange(r0, r1, dtype=np.int32)
        lat_values = 90.0 - (rows.astype(np.float64) + 0.5) * (180.0 / h)
        cos_lat = max(abs(math.cos(math.radians(lat))), 1e-5)
        lon_span = min(180.0, math.degrees(radius / (EARTH_RADIUS_KM * cos_lat)))
        if lon_span >= 179.999:
            cols = np.arange(w, dtype=np.int32)
        else:
            col_radius = int(math.ceil(lon_span * w / 360.0)) + 1
            cols = (col + np.arange(-col_radius, col_radius + 1, dtype=np.int32)) % w
        lon_values = (cols.astype(np.float64) + 0.5) * (360.0 / w) - 180.0
        dlat = np.radians(lat_values[:, None] - lat)
        dlon_deg = (lon_values[None, :] - lon + 180.0) % 360.0 - 180.0
        dlon = np.radians(dlon_deg)
        lat1 = np.radians(lat_values[:, None])
        lat2 = math.radians(lat)
        hav = np.sin(dlat / 2.0) ** 2 + np.cos(lat1) * math.cos(lat2) * np.sin(dlon / 2.0) ** 2
        distance = 2.0 * EARTH_RADIUS_KM * np.arcsin(np.sqrt(np.clip(hav, 0.0, 1.0)))
        sigma = radius / 3.0
        kernel = (point["weight"] * confidence * np.exp(-0.5 * (distance / sigma) ** 2)).astype(np.float32)
        kernel[distance > radius] = 0.0
        # Influence should only allocate extra detail to land. Keep a separate
        # coverage signal so sparse source coverage is visible during review.
        local_land = land_mask[np.ix_(rows, cols)]
        kernel *= local_land
        # A real point must affect at least its containing land cell even when
        # its requested radius is smaller than a coarse pixel. Without this,
        # valid narrow-radius evidence can vanish during rasterization.
        center_col_match = np.flatnonzero(cols == col)
        center_r = row - r0
        if local_land[center_r, center_col_match[0]]:
            kernel[center_r, center_col_match[0]] = max(
                float(kernel[center_r, center_col_match[0]]),
                point["weight"] * confidence,
            )
        local_inf = influence[np.ix_(rows, cols)]
        local_inf += kernel
        influence[np.ix_(rows, cols)] = local_inf
        local_cov = coverage[np.ix_(rows, cols)]
        local_cov += (kernel > 0.025).astype(np.float32)
        coverage[np.ix_(rows, cols)] = local_cov
    return influence, coverage, stats


def saturate(raw: np.ndarray) -> np.ndarray:
    """Map accumulated evidence to [0, 1] without percentile-normalizing away weights."""
    return (1.0 - np.exp(-np.maximum(raw, 0.0))).astype(np.float32)


def build_evidence_layers(
    rows: list[dict[str, Any]], shape: tuple[int, int], land_mask: np.ndarray,
) -> tuple[dict[str, np.ndarray], np.ndarray, dict[str, Any]]:
    by_kind_period: dict[str, dict[str, list[dict[str, Any]]]] = defaultdict(lambda: defaultdict(list))
    by_kind_shared: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        if row["confidence"] == "DISPUTED":
            continue
        if row["period"].strip().upper() == "SHARED":
            by_kind_shared[row["kind"]].append(row)
        else:
            by_kind_period[row["kind"]][row["period"]].append(row)

    layers = {kind: np.zeros(shape, dtype=np.float32) for kind in KIND_WEIGHT}
    coverage_all = np.zeros(shape, dtype=np.float32)
    report: dict[str, Any] = {"periods": {}, "kind_point_counts": {}, "point_warnings": {}}
    for kind in KIND_WEIGHT:
        period_maps: list[np.ndarray] = []
        report["kind_point_counts"][kind] = sum(len(points) for points in by_kind_period[kind].values()) + len(by_kind_shared[kind])
        for period in sorted(by_kind_period[kind]):
            points = by_kind_period[kind][period]
            raw, coverage, stats = rasterize_points(points, shape, land_mask)
            score = saturate(raw)
            period_maps.append(score)
            coverage_all = np.maximum(coverage_all, saturate(coverage))
            report["periods"][f"{kind}:{period}"] = {"points": len(points), **stats}
        if period_maps:
            # A strong, sourced requirement in any represented era must be retained.
            # Fold incrementally to avoid stacking all period rasters in memory.
            combined = np.zeros(shape, dtype=np.float32)
            for period_map in period_maps:
                np.maximum(combined, period_map, out=combined)
            layers[kind] = combined
        shared = by_kind_shared[kind]
        if shared:
            raw, coverage, stats = rasterize_points(shared, shape, land_mask)
            layers[kind] = np.maximum(layers[kind], saturate(raw))
            coverage_all = np.maximum(coverage_all, saturate(coverage))
            report["periods"][f"{kind}:SHARED"] = {"points": len(shared), **stats}
    for kind in KIND_WEIGHT:
        report["point_warnings"][kind] = int(report["kind_point_counts"][kind] > 0 and not np.any(layers[kind] > 0))
    return layers, coverage_all, report


def make_preview(array: np.ndarray, land_mask: np.ndarray, path: Path, floor: float = 0.0) -> None:
    scaled = np.clip((array - floor) / max(1.0 - floor, 1e-8), 0.0, 1.0)
    pixels = np.zeros(array.shape, dtype=np.uint8)
    pixels[land_mask] = (24 + scaled[land_mask] * 231).astype(np.uint8)
    Image.fromarray(pixels, mode="L").save(path, format="PNG", optimize=False, compress_level=9)


def parse_args() -> argparse.Namespace:
    here = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--base-raster", type=Path, required=True, help="NPZ from rasterize_ne.py with land, lake and river arrays")
    parser.add_argument("--elevation", type=Path, help="Optional GEBCO grid NPZ with a mean elevation array")
    parser.add_argument("--evidence", type=Path, help="Optional sourced evidence point CSV; see evidence_points.template.csv")
    parser.add_argument("--sources", type=Path, default=here / "provenance" / "sources.csv", help="Source ledger used to reject unapproved source IDs")
    parser.add_argument("--output", type=Path, default=Path("detail_demand.npz"), help="Output compressed NPZ")
    parser.add_argument("--downsample", type=int, default=4, help="Coarse raster factor; default 4 matches make_locations.py")
    parser.add_argument("--coast-range-km", type=float, default=180.0, help="Exponential coastal influence range in km")
    parser.add_argument("--river-range-km", type=float, default=70.0, help="Exponential river influence range in km")
    parser.add_argument("--river-boundary-range-km", type=float, default=18.0, help="Range for river boundary affinity in km")
    parser.add_argument("--ridge-tpi-m", type=float, default=250.0, help="Positive topographic-position scale for ridge affinity")
    parser.add_argument("--ridge-window", type=int, default=31, help="Odd coarse-grid window for local relief")
    parser.add_argument("--evidence-share", type=float, default=0.80, help="Weight of evidence vs physical geography when evidence exists")
    parser.add_argument("--minimum-demand", type=float, default=0.08, help="Nonzero floor for retained land cells")
    parser.add_argument("--require-evidence", action="store_true", help="Fail unless at least one non-disputed evidence point is used")
    args = parser.parse_args()
    if args.downsample < 1:
        parser.error("--downsample must be at least 1")
    if args.coast_range_km <= 0 or args.river_range_km <= 0 or args.river_boundary_range_km <= 0:
        parser.error("distance ranges must be positive")
    if args.ridge_tpi_m <= 0 or args.ridge_window < 3 or args.ridge_window % 2 == 0:
        parser.error("--ridge-tpi-m must be positive and --ridge-window must be an odd integer >= 3")
    if not 0.0 <= args.evidence_share <= 1.0 or not 0.0 <= args.minimum_demand < 1.0:
        parser.error("--evidence-share and --minimum-demand must be within their documented ranges")
    return args


def main() -> int:
    args = parse_args()
    expected = (H, W)
    factor = args.downsample
    if H % factor or W % factor:
        raise SystemExit(f"downsample factor {factor} must divide configured grid {W}x{H}")
    shape = (H // factor, W // factor)
    log(f"configured grid {W}x{H}; demand grid {shape[1]}x{shape[0]} (factor {factor})")

    with np.load(args.base_raster, allow_pickle=False) as data:
        required = {"land", "lake", "river"}
        missing = sorted(required - set(data.files))
        if missing:
            raise SystemExit(f"{args.base_raster.name} is missing arrays: {', '.join(missing)}")
        full_masks: dict[str, np.ndarray] = {}
        for key in ("land", "lake", "river"):
            array = data[key]
            if array.shape != expected or array.ndim != 2:
                raise SystemExit(f"base raster '{key}' has shape {array.shape}; expected {expected}")
            full_masks[key] = coarse_any(array.astype(bool), factor)
            del array
    antarctic_rows = (90.0 - (np.arange(shape[0]) + 0.5) * 180.0 / shape[0]) < ANTARCTIC_LAT
    for key in full_masks:
        full_masks[key][antarctic_rows, :] = False
    log(f"excluded {int(antarctic_rows.sum())} coarse rows south of {ANTARCTIC_LAT:g} latitude (Antarctica)")
    land = full_masks["land"]
    lakes = full_masks["lake"]
    river = full_masks["river"] & land & ~lakes
    dry_land = land & ~lakes
    solid = land | lakes
    if not dry_land.any():
        raise SystemExit("base raster contains no land pixels at the selected resolution")
    log(f"coarse land cells {int(dry_land.sum()):,}; lake cells {int(lakes.sum()):,}; river cells {int(river.sum()):,}")

    # Geographic influence is computed from real physical masks, not hand-drawn borders.
    structure = np.ones((3, 3), dtype=bool)
    coast_edge = solid & ndimage.binary_dilation(~solid, structure=structure)
    log("measuring coast distance")
    coast_distance = distance_to_feature_km(coast_edge)
    coast = np.exp(-coast_distance / args.coast_range_km).astype(np.float32)
    coast[~dry_land] = 0.0
    log("measuring river distance")
    river_distance = distance_to_feature_km(river)
    river_demand = np.exp(-river_distance / args.river_range_km).astype(np.float32)
    river_demand[~dry_land] = 0.0
    river_affinity = np.exp(-river_distance / args.river_boundary_range_km).astype(np.float32)
    river_affinity[~dry_land] = 0.0
    del coast_distance, river_distance

    ridge_affinity = np.zeros(shape, dtype=np.float32)
    elevation = None
    if args.elevation:
        elevation_full = read_raster(args.elevation, "mean", expected)
        elevation = coarse_mean(elevation_full.astype(np.float32, copy=False), factor)
        del elevation_full
        log("calculating terrain-relative ridge affinity")
        # Latitude edges are clamped; longitude wraps around the world seam.
        tpi = elevation - ndimage.uniform_filter(elevation, size=args.ridge_window, mode=("nearest", "wrap"))
        ridge_affinity = np.clip(tpi / args.ridge_tpi_m, 0.0, 1.0).astype(np.float32)
        ridge_affinity[~dry_land] = 0.0
        del tpi

    evidence_layers = {kind: np.zeros(shape, dtype=np.float32) for kind in KIND_WEIGHT}
    evidence_coverage = np.zeros(shape, dtype=np.float32)
    evidence_report: dict[str, Any] = {"periods": {}, "kind_point_counts": {}, "point_warnings": {}}
    if args.evidence:
        if not args.evidence.is_file():
            raise SystemExit(f"evidence file does not exist: {args.evidence}")
        if not args.sources.is_file():
            raise SystemExit(f"source ledger does not exist: {args.sources}")
        evidence_rows, source_counts = read_evidence(args.evidence, args.sources)
        evidence_layers, evidence_coverage, evidence_report = build_evidence_layers(evidence_rows, shape, dry_land)
        evidence_report["records"] = [
            {
                "id": row["id"],
                "kind": row["kind"],
                "period": row["period"],
                "confidence": row["confidence"],
                "source_ids": row["source_ids_list"],
                "source_locator": row["source_locator"],
                "used": row["confidence"] != "DISPUTED",
            }
            for row in evidence_rows
        ]
        used_rows = sum(1 for row in evidence_rows if row["confidence"] != "DISPUTED")
        log(f"evidence records {len(evidence_rows)}; non-disputed records {used_rows}; kinds {source_counts}")
        if args.require_evidence and used_rows == 0:
            raise SystemExit("--require-evidence was set but no non-disputed evidence records were usable")
    elif args.require_evidence:
        raise SystemExit("--require-evidence needs --evidence")

    active_kinds = [kind for kind, layer in evidence_layers.items() if np.any(layer > 0)]
    if args.require_evidence and not active_kinds:
        raise SystemExit("--require-evidence was set, but no evidence affected land cells; check coordinates, grid resolution and radii")
    if args.evidence and evidence_report.get("kind_point_counts") and not active_kinds:
        log("WARNING: evidence rows were read but produced no nonzero land influence; check coordinates, grid resolution and radii")
    if active_kinds:
        total_kind_weight = sum(KIND_WEIGHT[kind] for kind in active_kinds)
        evidence = np.zeros(shape, dtype=np.float32)
        for kind in active_kinds:
            evidence += evidence_layers[kind] * (KIND_WEIGHT[kind] / total_kind_weight)
        geography = 0.65 * coast + 0.35 * river_demand
        score = args.evidence_share * evidence + (1.0 - args.evidence_share) * geography
        mode = "evidence_plus_physical_geography"
    else:
        score = 0.65 * coast + 0.35 * river_demand
        mode = "physical_geography_only"
        log("WARNING: no usable historical evidence points; output is a physical-geography baseline, not a historically validated demand map")

    demand = args.minimum_demand + (1.0 - args.minimum_demand) * np.clip(score, 0.0, 1.0)
    demand[~dry_land] = 0.0
    boundary_affinity = np.maximum(river_affinity, ridge_affinity)
    boundary_affinity[~dry_land] = 0.0
    # Report only basenames so artifacts never capture a user's absolute filesystem path.
    report = {
        "format_version": 1,
        "purpose": "relative province-detail demand; not historical borders or an ownership map",
        "mode": mode,
        "grid": {"full_width": W, "full_height": H, "downsample": factor, "width": shape[1], "height": shape[0]},
        "inputs": {
            "base_raster": args.base_raster.name,
            "elevation": args.elevation.name if args.elevation else None,
            "evidence": args.evidence.name if args.evidence else None,
            "sources_ledger": args.sources.name if args.evidence else None,
        },
        "parameters": {
            "coast_range_km": args.coast_range_km,
            "river_range_km": args.river_range_km,
            "river_boundary_range_km": args.river_boundary_range_km,
            "ridge_tpi_m": args.ridge_tpi_m if args.elevation else None,
            "ridge_window": args.ridge_window if args.elevation else None,
            "evidence_share": args.evidence_share,
            "minimum_demand": args.minimum_demand,
            "kind_weights": KIND_WEIGHT,
            "confidence_factors": CONFIDENCE_FACTOR,
            "radius_semantics": "radius_km is a hard cutoff; Gaussian sigma is radius_km / 3",
        },
        "counts": {
            "dry_land_cells": int(dry_land.sum()),
            "lake_cells": int(lakes.sum()),
            "river_cells": int(river.sum()),
            "coastal_cells": int(coast_edge.sum()),
            "active_evidence_kinds": active_kinds,
            "evidence_records_by_kind": evidence_report.get("kind_point_counts", {}),
        },
        "evidence": evidence_report,
        "score_statistics_on_land": {
            "minimum": float(np.min(demand[dry_land])),
            "median": float(np.median(demand[dry_land])),
            "p90": float(np.percentile(demand[dry_land], 90)),
            "p99": float(np.percentile(demand[dry_land], 99)),
            "maximum": float(np.max(demand[dry_land])),
        },
        "limitations": [
            "No evidence point means no source-backed demand contribution; it does not mean the region lacked settlements or political complexity.",
            "Input points must be curated from approved, cited sources. Weight and radius are modelling parameters and must be documented by the curator.",
            "Historical border lines are not copied, traced, or rasterized by this tool.",
            "The map is not historically validated until evidence coverage is reviewed across multiple eras and regions.",
        ],
    }

    output = args.output
    output.parent.mkdir(parents=True, exist_ok=True)
    arrays: dict[str, np.ndarray] = {
        "demand": demand.astype(np.float32),
        "land": dry_land.astype(np.uint8),
        "lake": lakes.astype(np.uint8),
        "river": river.astype(np.uint8),
        "settlement_demand": evidence_layers["settlement"].astype(np.float32),
        "political_complexity_demand": evidence_layers["political_complexity"].astype(np.float32),
        "strategic_corridor_demand": evidence_layers["strategic_corridor"].astype(np.float32),
        "coast_demand": coast.astype(np.float32),
        "river_demand": river_demand.astype(np.float32),
        "evidence_coverage": evidence_coverage.astype(np.float32),
        "river_boundary_affinity": river_affinity.astype(np.float32),
        "ridge_boundary_affinity": ridge_affinity.astype(np.float32),
        "boundary_affinity": boundary_affinity.astype(np.float32),
    }
    if elevation is not None:
        arrays["elevation_mean_m"] = elevation.astype(np.float32)
    np.savez_compressed(output, **arrays)
    report_path = output.with_name(output.stem + ".report.json")
    report_path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    make_preview(demand, dry_land, output.with_name(output.stem + ".png"), args.minimum_demand)
    make_preview(boundary_affinity, dry_land, output.with_name(output.stem + ".boundary.png"), 0.0)
    make_preview(evidence_coverage, dry_land, output.with_name(output.stem + ".coverage.png"), 0.0)
    log(f"wrote {output.name}, {output.stem}.png, {output.stem}.boundary.png, {output.stem}.coverage.png and {report_path.name}")
    log(f"mode={mode}; active evidence kinds={active_kinds or 'none'}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, KeyError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        raise SystemExit(2)
