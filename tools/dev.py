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
    "src/SharpHDL.Core/SharpHDL.Core.csproj",
    "src/SharpHDL.HLS/SharpHDL.HLS.csproj",
    "src/SharpHDL.Simulation/SharpHDL.Simulation.csproj",
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
        if shutil.which("docker"):
            print("Local .NET SDK is optional: 'just setup' provisions it in Docker.")
        else:
            print("Install Docker to provision the toolchain, or install .NET 8 locally.", file=sys.stderr)
            return 1
    return 0

def setup():
    """Provision the toolchain in Docker automatically, then restore .NET packages.

    No root access or unmanaged OS package installation is performed on the host.
    Docker Desktop/Engine is a prerequisite; native developer SDK is optional.
    """
    docker = shutil.which("docker")
    if not docker:
        print("Docker is required for automatic HDL tool provisioning.", file=sys.stderr)
        print("Install Docker Desktop/Engine, start it, and run 'just setup' again.", file=sys.stderr)
        return 1
    status = subprocess.run([docker, "info"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if status.returncode != 0:
        print("Docker daemon is unavailable; start Docker and run setup again.", file=sys.stderr)
        return 1
    run([docker, "build", "-f", "tools/Dockerfile.dev", "-t", "sharphdl-dev:local", "."])
    for project in PROJECTS:
        run([docker, "run", "--rm", "-v", str(ROOT) + ":/workspace", "-w", "/workspace",
             "sharphdl-dev:local", "dotnet", "restore", project])
    run([docker, "run", "--rm", "-v", str(ROOT) + ":/workspace", "-w", "/workspace",
         "sharphdl-dev:local", "bash", "-lc",
         "for t in dotnet iverilog vvp verilator yosys nextpnr-ice40 icepack; do command -v \"$t\" >/dev/null || exit 1; done"])
    print("Automatic container provisioning complete (HDL tools, .NET SDK, Python, Node).")
    print("Use 'just container-build' or 'just container-test' without local .NET installs.")
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
