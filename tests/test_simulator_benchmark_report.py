#!/usr/bin/env python3
"""Regression for the benchmark evidence gate; synthetic records test parsing only."""
import copy
import importlib.util
from pathlib import Path
import unittest

path = Path(__file__).resolve().parents[1] / "tools/compare-simulator-benchmarks.py"
spec = importlib.util.spec_from_file_location("benchmark_report", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class ReportTests(unittest.TestCase):
    def setUp(self):
        state = {name: 0 for name in module.OUTPUTS}
        common = dict(schema_version=1, workload="tinyrisc-hello-reset128-v1",
                      edges_per_trial=100, trials=3, warmup_edges=100, reset_period=128,
                      qualification_edges=256, qualification_hash="0000000000000000", final_state=state)
        self.managed = copy.deepcopy(common)
        self.native = copy.deepcopy(common)
        self.managed["engines"] = {"interpreter": {"seconds": [3, 1, 2]},
                                   "compiled_dotnet": {"seconds": [0.3, 0.1, 0.2]}}
        self.native["seconds"] = [0.03, 0.01, 0.02]

    def test_medians_are_computed_only_for_matching_evidence(self):
        result = module.qualify(self.managed, self.native)
        self.assertEqual(50, result["interpreter"]["median_edges_per_second"])
        self.assertEqual(500, result["compiled_dotnet"]["median_edges_per_second"])

    def test_rejects_mismatched_schedule_trace_or_state(self):
        for key in ("edges_per_trial", "warmup_edges", "qualification_hash", "final_state"):
            with self.subTest(key=key):
                record = copy.deepcopy(self.native)
                record[key] = "different"
                with self.assertRaises(ValueError):
                    module.qualify(self.managed, record)

    def test_rejects_unmeasured_or_invalid_durations(self):
        for samples in ([0, 0, 0], [1, float("nan"), 2], [-1, 2, 3], [1, 2]):
            self.native["seconds"] = samples
            with self.assertRaises(ValueError):
                module.qualify(self.managed, self.native)


if __name__ == "__main__":
    unittest.main()
