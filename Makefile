# SharpHDL make compatibility entry point.
# Docker + Python 3 are the only host prerequisites for automatic HDL/.NET provisioning.
# Toolchain itself is installed in tools/Dockerfile.dev, never silently on host.
.PHONY: help doctor setup setup-tools tinyrisc-hello container-verify container-build container-test container-shell build test test-hdl simulate benchmark browser vscode verify clean

IMAGE := sharphdl-dev:local
DOCKER_RUN = docker run --rm -v "$(CURDIR):/workspace" -w /workspace $(IMAGE)

help:
	@echo "SharpHDL (Docker + Python 3 required for automatic provisioning)"
	@echo "  make setup           Download/build .NET SDK and HDL toolchain in Docker; restore NuGet projects"
	@echo "  make tinyrisc-hello  Run actual CPU/RTL, UART serial, and Yosys regression"
	@echo "  make container-verify Run full .NET/Icarus/Yosys smoke suite"
	@echo "  make container-build / container-test / container-shell"
	@echo "  make doctor          Inspect host prerequisites"
	@echo "  just --list          Optional extra native developer tasks"

doctor:
	python3 tools/dev.py doctor

setup setup-tools:
	python3 tools/dev.py setup

tinyrisc-hello:
	$(DOCKER_RUN) bash tools/run-tinyrisc-container.sh

container-verify:
	$(DOCKER_RUN) bash tools/container-smoke.sh

container-build:
	$(DOCKER_RUN) dotnet build tests/Tests.csproj -c Release

container-test:
	$(DOCKER_RUN) dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release

container-shell:
	docker run --rm -it -v "$(CURDIR):/workspace" -w /workspace $(IMAGE) bash

# Native developer tasks remain owned by just.
build test test-hdl simulate benchmark browser vscode verify clean:
	@command -v just >/dev/null || { echo "Install just or use the container targets above" >&2; exit 1; }
	@just $@
