#!/usr/bin/env python3
"""Fail when SharpHDL subsystem project references violate layering or form cycles."""
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PROJECTS = {
    "SharpHDL.Core": ROOT / "src/SharpHDL.Core/SharpHDL.Core.csproj",
    "SharpHDL.HLS": ROOT / "src/SharpHDL.HLS/SharpHDL.HLS.csproj",
    "SharpHDL.Simulation": ROOT / "src/SharpHDL.Simulation/SharpHDL.Simulation.csproj",
}
ALLOWED = {
    "SharpHDL.Core": set(),
    "SharpHDL.HLS": {"SharpHDL.Core"},
    "SharpHDL.Simulation": {"SharpHDL.Core"},
}
graph = {}
for name, path in PROJECTS.items():
    doc = ET.parse(path)
    refs = set()
    for item in doc.findall(".//ProjectReference"):
        target = (path.parent / item.attrib["Include"].replace("\\", "/")).resolve()
        matches = [n for n, p in PROJECTS.items() if p.resolve() == target]
        if not matches:
            raise SystemExit(f"{name}: unexpected external project reference: {target}")
        refs.add(matches[0])
    if not refs <= ALLOWED[name]:
        raise SystemExit(f"{name}: forbidden dependency {refs - ALLOWED[name]}")
    graph[name] = refs

def visit(name, active, visited):
    if name in active:
        raise SystemExit(f"Project reference cycle: {' -> '.join([*active, name])}")
    if name in visited:
        return
    active.append(name)
    for dep in graph[name]:
        visit(dep, active, visited)
    active.pop()
    visited.add(name)

visited = set()
for n in graph:
    visit(n, [], visited)
print("PASS: Core <- HLS and Core <- Simulation; no project dependency cycles")
