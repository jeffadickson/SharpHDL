# SharpHDL make compatibility entry point.
# Docker + Python 3 are the only host prerequisites for automatic HDL/.NET provisioning.
# Toolchain itself is installed in tools/Dockerfile.dev, never silently on host.
.PHONY: help doctor setup setup-tools setup-and-hello tinyrisc-hello benchmark-simulator benchmark-tinyrisc container-verify container-build container-test container-shell build test test-hdl simulate benchmark browser vscode vscode-test vscode-provenance multiplier-rtl multiplier-compare dsp-verify core-ir-verify core-hls-verilator core-rtl-native cil-core-verilator verify clean

IMAGE := sharphdl-dev:local
DOCKER_RUN = docker run --rm -v "$(CURDIR):/workspace" -w /workspace $(IMAGE)

help:
	@echo "SharpHDL (Docker + Python 3 required for automatic provisioning)"
	@echo "  make setup           Download/build .NET SDK and HDL toolchain in Docker; restore NuGet projects"
	@echo "  make setup-and-hello Run provisioning and full Hello World verification"
	@echo "  make tinyrisc-hello  Run both legacy/native CPU RTL, UART serial, and Yosys regressions"
	@echo "  make container-verify Run full .NET/Icarus/Yosys smoke suite"
	@echo "  make container-build / container-test / container-shell"
	@echo "  make doctor          Inspect host prerequisites"
	@echo "  make vscode          Test and package SharpHDL VS Code extension as dist/*.vsix (downloads Node 22 Docker image)"
	@echo "  make vscode-test     Run extension JavaScript unit tests in Node 22 container"
	@echo "  make vscode-provenance Generate CIL source-to-RTL map for scheduled pipeline"
	@echo "  make multiplier-compare Generate and synthesize alternative multiplier architectures"
	@echo "  make dsp-verify       Simulate direct and pipelined DSP48E2 wrappers (behavioral mode)"
	@echo "  make core-ir-verify   Check Core/HLS/Simulation layers and run canonical IR parity tests"
	@echo "  make core-hls-verilator  Compile Core IR elastic HLS and run Verilator stall regression"
	@echo "  make core-rtl-native  Verify native Core RTL emitter against same stall/reset testbench"
	@echo "  make cil-core-verilator  Verify real CIL -> Core HLS -> dual RTL backends"
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

core-hls-verilator:
	$(DOCKER_RUN) bash -c 'dotnet run --project tools/SharpHDL.CoreHlsDemo/SharpHDL.CoreHlsDemo.csproj -c Release -- /workspace && mkdir -p build/core-hls/obj && verilator --binary --timing -Wno-fatal --top-module tb_core_hls_elastic --Mdir build/core-hls/obj generated/core-hls/CoreHlsElastic.v tests/rtl/tb_core_hls_elastic.sv && build/core-hls/obj/Vtb_core_hls_elastic'

core-rtl-native:
	$(DOCKER_RUN) bash -c 'dotnet run --project tools/SharpHDL.CoreHlsDemo/SharpHDL.CoreHlsDemo.csproj -c Release -- /workspace && mkdir -p build/core-hls/native && verilator --binary --timing -Wno-fatal -DCORE_HLS_NATIVE --top-module tb_core_hls_elastic --Mdir build/core-hls/native generated/core-hls/CoreHlsElasticNative.v tests/rtl/tb_core_hls_elastic.sv && build/core-hls/native/Vtb_core_hls_elastic'

cil-core-verilator:
	$(DOCKER_RUN) bash -c 'dotnet run --project tools/SharpHDL.CilCoreDemo/SharpHDL.CilCoreDemo.csproj -c Release -- /workspace && mkdir -p build/cil-core/obj && verilator --binary --timing -Wno-fatal --top-module tb_cil_core --Mdir build/cil-core/obj generated/cil-core/CilCoreLegacy.v generated/cil-core/CilCoreNative.v tests/rtl/tb_cil_core.sv && build/cil-core/obj/Vtb_cil_core'

core-ir-verify:
	$(DOCKER_RUN) python3 tests/check-project-layers.py
	$(DOCKER_RUN) dotnet test tests/SharpHDL.UnitTests/SharpHDL.UnitTests.csproj -c Release --filter 'FullyQualifiedName~CoreHardwareGraphTests|FullyQualifiedName~CoreGraphLoweringTests|FullyQualifiedName~CoreIrExtendedParityTests|FullyQualifiedName~TinyRiscCoreIrTests|FullyQualifiedName~HlsGraphAnalyzerTests|FullyQualifiedName~HlsElasticCompilerTests|FullyQualifiedName~CoreVerilogEmitterTests|FullyQualifiedName~CoreCilPipelineTests'

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
vscode-provenance:
	$(DOCKER_RUN) dotnet run --project tools/SharpHDL.Provenance/SharpHDL.Provenance.csproj -c Debug -- /workspace

multiplier-rtl:
	$(DOCKER_RUN) dotnet run --project tools/SharpHDL.MultiplierBench/SharpHDL.MultiplierBench.csproj -c Release -- /workspace

multiplier-compare: multiplier-rtl
	$(DOCKER_RUN) python3 tools/compare_multiplier_synthesis.py

dsp-verify: multiplier-rtl
	$(DOCKER_RUN) bash -c 'mkdir -p build/dsp && \
	iverilog -g2012 -DSHARPH_DL_DSP_BEHAVIORAL -s tb_dsp48e2_mul -o build/dsp/direct rtl/amd/sharphdl_dsp48e2_mul_unsigned.v tests/rtl/tb_dsp48e2_mul.sv && vvp build/dsp/direct && \
	iverilog -g2012 -DSHARPH_DL_DSP_BEHAVIORAL -s tb_dsp48e2_pipeline -o build/dsp/pipelined rtl/amd/sharphdl_dsp48e2_mul_pipeline.v tests/rtl/tb_dsp48e2_pipeline.sv && vvp build/dsp/pipelined && \
	iverilog -g2012 -DSHARPH_DL_DSP_BEHAVIORAL -s tb_generated_direct_dsp -o build/dsp/generated generated/multipliers/DirectDsp.v rtl/amd/sharphdl_dsp48e2_mul_unsigned.v tests/rtl/tb_generated_direct_dsp.sv && vvp build/dsp/generated'

vscode-test:
	docker run --rm -v "$(CURDIR):/workspace" -w /workspace/vscode/sharphdl node:22-bookworm \
		sh -c 'node --check extension.js && node --check projectIntegration.js && node --test *.test.js'

vscode: vscode-test
	@mkdir -p dist
	docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -e npm_config_cache=/tmp/npm-cache \
		-v "$(CURDIR):/workspace" -w /workspace/vscode/sharphdl node:22-bookworm \
		sh -c 'npx --yes @vscode/vsce@3.6.0 package --no-dependencies --out /workspace/dist/sharphdl-0.5.0.vsix'
	@echo "Install in VS Code: code --install-extension dist/sharphdl-0.5.0.vsix"

# Native developer tasks remain owned by just.
build test test-hdl simulate benchmark browser verify clean:
	@command -v just >/dev/null || { echo "Install just or use the container targets above" >&2; exit 1; }
	@just $@
