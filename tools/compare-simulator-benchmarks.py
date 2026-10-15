#!/usr/bin/env python3
"""Validate matched workload/state evidence before reporting three-backend medians."""
import argparse
import hashlib
import json
import math
import platform
from pathlib import Path
import statistics
import subprocess

CONTRACT = ("schema_version", "workload", "edges_per_trial", "trials", "warmup_edges",
            "reset_period", "qualification_edges", "qualification_hash", "final_state")
OUTPUTS = {"tx_valid", "tx_data", "rx_ready", "pc", "ir", "phase", "halted", "fault",
           "retired", "last_pc", "leds", *(f"r{i}" for i in range(8))}


def qualify(managed, native):
    """Reject mismatched work, missing state, invalid measurements and failed traces."""
    for key in CONTRACT:
        if key not in managed or key not in native or managed[key] != native[key]:
            raise ValueError(f"Benchmark contract or hardware state differs: {key}")
    if managed["schema_version"] != 1 or managed["workload"] != "tinyrisc-hello-reset128-v1":
        raise ValueError("Unsupported benchmark schema/workload")
    trials, edges = managed["trials"], managed["edges_per_trial"]
    if type(trials) is not int or not 3 <= trials <= 15 or type(edges) is not int or not 1 <= edges <= 10000000:
        raise ValueError("Invalid trial or edge counts")
    if managed["warmup_edges"] != 100 or managed["reset_period"] != 128 or managed["qualification_edges"] != 256:
        raise ValueError("Unexpected clock schedule")
    state = managed["final_state"]
    if set(state) != OUTPUTS or state["fault"] != 0 or any(type(v) is not int or v < 0 for v in state.values()):
        raise ValueError("Incomplete or invalid CPU final state")
    durations = {
        "interpreter": managed["engines"]["interpreter"]["seconds"],
        "compiled_dotnet": managed["engines"]["compiled_dotnet"]["seconds"],
        "verilator": native["seconds"]
    }
    summaries = {}
    for name, values in durations.items():
        if len(values) != trials or any(type(x) not in (int, float) or not math.isfinite(x) or x <= 0 for x in values):
            raise ValueError(f"Invalid elapsed samples: {name}")
        median = statistics.median(values)
        summaries[name] = {"seconds": values, "median_seconds": median,
                           "median_edges_per_second": edges / median,
                           "minimum_seconds": min(values), "maximum_seconds": max(values)}
    return summaries


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    args = parser.parse_args()
    directory = args.directory
    managed = json.loads((directory / "dotnet.json").read_text())
    native = json.loads((directory / "verilator.json").read_text())
    engines = qualify(managed, native)
    rtl_hash = hashlib.sha256((directory / "BenchmarkTinyRisc.v").read_bytes()).hexdigest()
    if rtl_hash != managed["rtl_sha256"]:
        raise ValueError("Timed Verilator source does not match exported .NET hardware")
    root = Path(__file__).resolve().parents[1]
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    managed_speed = engines["compiled_dotnet"]["median_edges_per_second"]
    reference_speed = engines["interpreter"]["median_edges_per_second"]
    native_speed = engines["verilator"]["median_edges_per_second"]
    report = {
        "schema_version": 1, "qualified": True, "commit": commit,
        "workload": managed["workload"], "edges_per_trial": managed["edges_per_trial"],
        "trials": managed["trials"], "warmup_edges": managed["warmup_edges"],
        "reset_period": managed["reset_period"], "qualification_edges": managed["qualification_edges"],
        "qualification_hash": managed["qualification_hash"], "final_state": managed["final_state"],
        "rom_sha256": managed["rom_sha256"], "rtl_sha256": rtl_hash,
        "managed_runtime": managed["runtime"], "managed_architecture": managed["architecture"],
        "tiered_compilation": managed["tiered_compilation"], "jit_construction_ms": managed["compile_ms"],
        "machine": platform.platform(), "tool_versions": (directory / "tool-versions.txt").read_text(),
        "engines": engines,
        "compiled_over_interpreter": managed_speed / reference_speed,
        "verilator_over_compiled": native_speed / managed_speed,
        "scope": "Single-clock two-state Hello World CPU; reset every 128 edges; includes reset and halted cycles. Timed clock loops exclude construction, warm-up, output reads, checks and process startup. No wave tracing. Native loop includes clock settling eval calls. Single CI machine; not an RHDL comparison."
    }
    (directory / "results.json").write_text(json.dumps(report, indent=2) + "\n")
    print(f"PASS matched trace, all CPU output ports, and {managed['trials']} timing repetitions")
    for name, values in engines.items():
        print(f"{name}: {values['median_edges_per_second']:,.0f} edges/sec median; raw seconds {values['seconds']}")
    print(f"Compiled / interpreter: {report['compiled_over_interpreter']:.3f}x")
    print(f"Verilator / compiled: {report['verilator_over_compiled']:.3f}x")


if __name__ == "__main__":
    main()
