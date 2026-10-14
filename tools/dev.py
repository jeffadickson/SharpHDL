#!/usr/bin/env python3
"""Cross-platform prerequisite inspection and explicit SharpHDL setup."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent
CORE = ["dotnet"]
OPTIONAL = ["just", "git", "docker", "node", "iverilog", "vvp", "verilator", "yosys", "nextpnr-ice40", "icepack"]
PROJECTS = [
    "src/SharpHDL/SharpHDL.csproj",
    "tests/Tests.csproj",
    "tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj",
    "tests/SharpHDL.IntegrationTests/SharpHDL.IntegrationTests.csproj",
    "tests/SharpHDL.Benchmarks/SharpHDL.Benchmarks.csproj",
    "tools/SharpHDL.Live/SharpHDL.Live.csproj",
]

def run(args):
    print("+", " ".join(args), flush=True)
    subprocess.run(args, cwd=ROOT, check=True)

def doctor():
    print("SharpHDL development environment")
    print("Python:", sys.version.split()[0])
    for command in CORE + OPTIONAL:
        print(f"{command:17} {'FOUND: ' + shutil.which(command) if shutil.which(command) else 'MISSING'}")
    dotnet = shutil.which("dotnet")
    if dotnet:
        try:
            run(["dotnet", "--version"])
        except subprocess.CalledProcessError:
            print("Configured .NET SDK is not usable.", file=sys.stderr)
            return 1
    else:
        print("Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0", file=sys.stderr)
        return 1
    return 0

def setup():
    if doctor():
        return 1
    for project in PROJECTS:
        run(["dotnet", "restore", project])
    print("Restored .NET projects. HDL/FPGA tools are intentionally not installed on the host.")
    print("For real HDL integration tests, install/start Docker and run: just test-hdl")
    print("Optional front-end requirements: Node/Playwright per existing browser test scripts.")
    return 0

def verify():
    if not shutil.which("dotnet"):
        print("Missing .NET SDK. Run: just doctor", file=sys.stderr)
        return 1
    run(["dotnet", "test", "tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj", "-c", "Release"])
    run(["dotnet", "run", "--project", "tests/Tests.csproj", "-c", "Release"])
    if not shutil.which("docker"):
        print("Docker missing: integration verification was NOT executed; verify is incomplete.", file=sys.stderr)
        return 2
    check = subprocess.run(["docker", "info"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if check.returncode:
        print("Docker daemon unavailable: integration verification was NOT executed.", file=sys.stderr)
        return 2
    run(["dotnet", "test", "tests/SharpHDL.IntegrationTests/SharpHDL.IntegrationTests.csproj", "-c", "Release"])
    print("Core and container integration verification completed.")
    return 0

def main():
    p = argparse.ArgumentParser()
    p.add_argument("command", choices=["doctor", "setup", "verify"])
    command = p.parse_args().command
    try:
        return {"doctor": doctor, "setup": setup, "verify": verify}[command]()
    except subprocess.CalledProcessError as e:
        print(f"Command failed with exit code {e.returncode}", file=sys.stderr)
        return e.returncode or 1

if __name__ == "__main__":
    raise SystemExit(main())
