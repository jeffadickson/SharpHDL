# SharpHDL make compatibility entry point.
# Docker + Python 3 are the only host prerequisites for automatic HDL/.NET provisioning.
# Toolchain itself is installed in tools/Dockerfile.dev, never silently on host.
.PHONY: help doctor setup setup-tools setup-and-hello tinyrisc-hello benchmark-simulator benchmark-tinyrisc container-verify container-build container-test container-shell build test test-hdl simulate benchmark browser vscode vscode-test verify clean

IMAGE := sharphdl-dev:local
DOCKER_RUN = docker run --rm -v "$(CURDIR):/workspace" -w /workspace $(IMAGE)

help:
	@echo "SharpHDL (Docker + Python 3 required for automatic provisioning)"
	@echo "  make setup           Download/build .NET SDK and HDL toolchain in Docker; restore NuGet projects"
	@echo "  make setup-and-hello Run provisioning and full Hello World verification"
	@echo "  make tinyrisc-hello  Run actual CPU/RTL, UART serial, and Yosys regression"
	@echo "  make container-verify Run full .NET/Icarus/Yosys smoke suite"
	@echo "  make container-build / container-test / container-shell"
	@echo "  make doctor          Inspect host prerequisites"
	@echo "  make vscode          Test and package SharpHDL VS Code extension as dist/*.vsix (downloads Node 22 Docker image)"
	@echo "  make vscode-test     Run extension JavaScript unit tests in Node 22 container"
	@echo "  just --list          Optional extra native developer tasks"

doctor:
	python3 tools/dev.py doctor

setup setup-tools:
	python3 tools/dev.py setup

setup-and-hello: setup tinyrisc-hello

tinyrisc-hello:
	$(DOCKER_RUN) bash tools/run-tinyrisc-container.sh

benchmark-tinyrisc:
	$(DOCKER_RUN) bash tests/benchmark-tinyrisc-backends.sh

benchmark-simulator:
	$(DOCKER_RUN) dotnet run --project tools/SharpHDL.SimulatorBench/SharpHDL.SimulatorBench.csproj -c Release -- 200000

container-verify:
	$(DOCKER_RUN) bash tools/container-smoke.sh

container-build:
	$(DOCKER_RUN) dotnet build tests/Tests.csproj -c Release

container-test:
	$(DOCKER_RUN) dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release

container-shell:
	docker run --rm -it -v "$(CURDIR):/workspace" -w /workspace $(IMAGE) bash

# VS Code extension packaging uses a pinned Node 22 container. No host Node/npm
# installation required; Docker pulls the image on first use.
vscode-test:
	docker run --rm -v "$(CURDIR):/workspace" -w /workspace/vscode/sharphdl node:22-bookworm \
		sh -c 'node --check extension.js && node --check projectIntegration.js && node --test *.test.js'

vscode: vscode-test
	@mkdir -p dist
	docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -e npm_config_cache=/tmp/npm-cache \
		-v "$(CURDIR):/workspace" -w /workspace/vscode/sharphdl node:22-bookworm \
		sh -c 'npx --yes @vscode/vsce@3.6.0 package --no-dependencies --out /workspace/dist/sharphdl-0.3.0.vsix'
	@echo "Install in VS Code: code --install-extension dist/sharphdl-0.3.0.vsix"

# Native developer tasks remain owned by just.
build test test-hdl simulate benchmark browser verify clean:
	@command -v just >/dev/null || { echo "Install just or use the container targets above" >&2; exit 1; }
	@just $@
