#!/usr/bin/env bash
# Negative setup checks run in children so sourcing cannot leak environment state.
set -euo pipefail
script_directory=$(dirname -- "${BASH_SOURCE[0]}")
test -d "$script_directory" && test -d /tmp
repo=$(cd -- "$script_directory/.." && pwd)
evidence=$(mktemp -d /tmp/sharphdl-z3-paths-XXXXXX)
check_rejection() {
  local selected=$1 expected=$2 label=$3 status=0
  env SHARPHDL_Z3_NATIVE_DIR="$selected" bash "$repo/tools/setup-z3-linux.sh" > "$evidence/$label.log" 2>&1 || status=$?
  [[ "$status" == 2 ]] || { cat "$evidence/$label.log"; echo "Unexpected status: $status" >&2; exit 1; }
  grep -F -- "$expected" "$evidence/$label.log"
}
test ! -e "$evidence/missing"
check_rejection "$evidence/missing" 'Pinned Z3 asset directory does not exist:' missing
check_rejection 'relative-cache' 'Native asset directory must be absolute.' relative
test ! -e "$evidence/missing"
printf 'PASS missing and relative native-asset diagnostics; retained at %s\n' "$evidence"
