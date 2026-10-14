.PHONY: help doctor setup build test test-hdl simulate benchmark browser vscode verify clean
help:
	@echo "SharpHDL uses just for developer orchestration. Run: just --list"
	@echo "Install just: https://just.systems"
doctor setup build test test-hdl simulate benchmark browser vscode verify clean:
	@just $@
