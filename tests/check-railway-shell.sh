#!/usr/bin/env bash
# Run the configured benchmark step with an isolated failing dotnet executable.
set -euo pipefail
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
test -d /tmp
scratch=$(mktemp -d /tmp/sharphdl-shell-check-XXXXXX)
printf 'Shell regression evidence at %s\n' "$scratch"
test -d "$scratch"
mkdir "$scratch/bin" "$scratch/generated" "$scratch/crashes"
test -d "$scratch/generated"
mkdir "$scratch/generated/tinyrisc"
printf '#!/usr/bin/env bash\nprintf "benchmark evidence\\n"\nexit 23\n' > "$scratch/bin/dotnet"
chmod +x "$scratch/bin/dotnet"
workflow="$repo/.github/workflows/compiled-simulator.yml"
shell=$(awk '{sub(/\r$/,"")} /name: Simple arithmetic steady-state throughput/{step=1;next} step && /^[ ]+shell:/{print $2;exit} step && /^[ ]+run:/{exit}' "$workflow")
command=$(awk '{sub(/\r$/,"")} /name: Simple arithmetic steady-state throughput/{step=1;next} step && /^[ ]+run:/{sub(/^[ ]+run: /,"");print;exit}' "$workflow")
test -n "$command"
options=(-e)
if [[ "$shell" == bash ]]; then options=(--noprofile --norc -eo pipefail); fi
status=0
(test -d "$scratch" && cd -- "$scratch" && PATH="$scratch/bin:$PATH" bash "${options[@]}" -c "$command") || status=$?
test "$status" = 23
grep -Fxq 'benchmark evidence' "$scratch/generated/tinyrisc/arithmetic-benchmark.log"

scan="$repo/tools/check-fuzz-crashes.sh"
touch "$scratch/crashes/README.txt"
bash "$scan" "$scratch/crashes"
status=0
bash "$scan" "$scratch/missing" || status=$?
test "$status" = 2
printf '#!/usr/bin/env bash\nexit 75\n' > "$scratch/bin/find"
chmod +x "$scratch/bin/find"
status=0
PATH="$scratch/bin:$PATH" bash "$scan" "$scratch/crashes" || status=$?
test "$status" = 2
touch "$scratch/crashes/id:000000"
status=0
bash "$scan" "$scratch/crashes" || status=$?
test "$status" = 1
printf 'PASS benchmark failure, clean scan, scan failure and retained crash\n'
