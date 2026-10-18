#!/usr/bin/env python3
"""Small synthetic tests for build_detail_demand.py; uses MAP_W=128 in subprocesses."""
from __future__ import annotations

import csv
import hashlib
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

import numpy as np

SCRIPT = Path(__file__).with_name("build_detail_demand.py")


class DetailDemandTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        h, w = 64, 128
        land = np.zeros((h, w), dtype=np.uint8)
        land[8:56, 8:120] = 1
        lake = np.zeros_like(land)
        lake[30:34, 40:45] = 1
        river = np.zeros_like(land)
        river[25:41, 55] = 1
        np.savez_compressed(self.root / "base.npz", land=land, lake=lake, river=river)
        elevation = np.zeros((h, w), dtype=np.float32)
        elevation[10:25, 80:95] = 1500
        elevation[15:18, 83:92] = 2600
        np.savez_compressed(self.root / "elevation.npz", mean=elevation)
        with (self.root / "sources.csv").open("w", newline="", encoding="utf-8") as stream:
            writer = csv.writer(stream)
            writer.writerow(["id", "status"])
            writer.writerow(["pleiades", "APPROVED"])
            writer.writerow(["unapproved", "PENDING OWNER"])
        self.evidence_path = self.root / "evidence.csv"
        self.write_evidence([ 
            ["site_a_500bce", "settlement", -20, 10, 1.0, 80, "500BCE", "HIGH", "pleiades", "record A", "synthetic test record"],
            ["site_b_1000ce", "settlement", -21, 11, 0.6, 80, "1000CE", "MEDIUM", "pleiades", "record B", "synthetic test record"],
            ["polity_zone_500bce", "political_complexity", -18, 10, 0.9, 150, "500BCE", "HIGH", "pleiades", "record C", "synthetic test record"],
            ["route_shared", "strategic_corridor", -22, 9, 0.8, 100, "SHARED", "LOW", "pleiades", "record D", "synthetic test record"],
        ])

    def tearDown(self) -> None:
        self.temp.cleanup()

    def write_evidence(self, rows: list[list[object]]) -> None:
        fields = ["id", "kind", "lon", "lat", "weight", "radius_km", "period", "confidence", "source_ids", "source_locator", "notes"]
        with self.evidence_path.open("w", newline="", encoding="utf-8") as stream:
            writer = csv.writer(stream)
            writer.writerow(fields)
            writer.writerows(rows)

    def run_tool(self, output_name: str, evidence: bool = True, require_evidence: bool = False) -> subprocess.CompletedProcess[str]:
        output = self.root / output_name
        command = [
            sys.executable, str(SCRIPT),
            "--base-raster", str(self.root / "base.npz"),
            "--elevation", str(self.root / "elevation.npz"),
            "--sources", str(self.root / "sources.csv"),
            "--output", str(output),
            "--downsample", "4",
        ]
        if evidence:
            command.extend(["--evidence", str(self.evidence_path)])
        if require_evidence:
            command.append("--require-evidence")
        env = os.environ.copy()
        env["MAP_W"] = "128"
        return subprocess.run(command, env=env, capture_output=True, text=True, check=False)

    def test_evidence_layers_and_land_mask(self) -> None:
        result = self.run_tool("evidence.npz")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        output = self.root / "evidence.npz"
        with np.load(output) as data:
            self.assertGreater(float(data["settlement_demand"].max()), 0.0)
            self.assertGreater(float(data["political_complexity_demand"].max()), 0.0)
            self.assertGreater(float(data["strategic_corridor_demand"].max()), 0.0)
            self.assertGreater(float(data["evidence_coverage"].max()), 0.0)
            self.assertTrue(np.isfinite(data["demand"]).all())
            self.assertTrue(np.all(data["demand"][data["land"] == 0] == 0.0))
        report = json.loads((self.root / "evidence.report.json").read_text(encoding="utf-8"))
        self.assertEqual(report["mode"], "evidence_plus_physical_geography")

    def test_no_evidence_is_explicit_physical_baseline(self) -> None:
        result = self.run_tool("physical.npz", evidence=False)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        report = json.loads((self.root / "physical.report.json").read_text(encoding="utf-8"))
        self.assertEqual(report["mode"], "physical_geography_only")
        self.assertIn("not a historically validated", result.stdout)

    def test_unapproved_source_is_rejected(self) -> None:
        rows = [["bad_source", "settlement", -20, 10, 1.0, 80, "500BCE", "HIGH", "unapproved", "record X", "must fail"]]
        self.write_evidence(rows)
        result = self.run_tool("bad.npz")
        self.assertEqual(result.returncode, 2)
        self.assertIn("not APPROVED", result.stderr)

    def test_npz_output_is_deterministic(self) -> None:
        first = self.run_tool("first.npz")
        second = self.run_tool("second.npz")
        self.assertEqual(first.returncode, 0, first.stdout + first.stderr)
        self.assertEqual(second.returncode, 0, second.stdout + second.stderr)
        a = hashlib.sha256((self.root / "first.npz").read_bytes()).hexdigest()
        b = hashlib.sha256((self.root / "second.npz").read_bytes()).hexdigest()
        self.assertEqual(a, b)


if __name__ == "__main__":
    unittest.main()
