#!/usr/bin/env bash
# Exercise the real compiled command boundaries on Linux with isolated evidence.
set -euo pipefail
[[ $# == 1 && "$1" == /* && -d "$1" ]] || { echo 'Expected an absolute managed artifacts directory.' >&2; exit 2; }
test -d /tmp
managed=$1
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
out=$(mktemp -d /tmp/sharphdl-command-check-XXXXXX)
printf 'Command boundary evidence at %s\n' "$out"
dotnet=$(readlink -f -- "$(command -v dotnet)")
doc="$managed/bin/SharpHDL.DocBuilder/release/SharpHDL.DocBuilder.dll"
equiv="$managed/bin/SharpHDL.EquivalenceCli/release/SharpHDL.EquivalenceCli.dll"
test -f "$doc" && test -f "$equiv"

expect_exit() {
  local expected=$1 label=$2 status=0
  shift 2
  "$@" > "$out/$label.stdout" 2> "$out/$label.stderr" || status=$?
  if [[ "$status" != "$expected" ]]; then
    cat "$out/$label.stdout" "$out/$label.stderr" >&2
    printf 'Expected exit %s for %s, got %s.\n' "$expected" "$label" "$status" >&2
    exit 1
  fi
}

expect_exit 2 doc-usage "$dotnet" "$doc"
expect_exit 2 equiv-usage "$dotnet" "$equiv"
grep -Fq '"Status":"InvalidInput"' "$out/doc-usage.stderr"
grep -Fq '"Status":"InvalidInput"' "$out/equiv-usage.stderr"
expect_exit 127 doc-missing "$dotnet" "$doc" "$repo" "$out/missing-doc" /usr/bin/true "$out/absent"
expect_exit 127 equiv-missing env PATH= "$dotnet" "$equiv" "$out/missing-equiv"
test ! -e "$out/missing-doc" && test ! -e "$out/missing-equiv"
expect_exit 1 doc-failed "$dotnet" "$doc" "$repo" "$out/failed" /usr/bin/true /usr/bin/false
grep -Fq '"Stage":"requirements-export"' "$out/failed/campaign-failure.json"
grep -Fq '"DetailCode":"NonzeroExit"' "$out/failed/campaign-failure.json"
test ! -e "$out/failed/campaign-complete.json"
test -f "$out/failed/runs/002/manifest.json"

# Signal only the tool's parent after the fixture has started. This keeps
# the cancellation case independent of CLR startup and machine speed.
cat > "$out/cancel-tool" <<'SCRIPT'
#!/usr/bin/env bash
kill -INT "$PPID"
exec sleep 60
SCRIPT
chmod +x "$out/cancel-tool"
expect_exit 130 doc-cancelled timeout -k 5s 20s "$dotnet" "$doc" "$repo" "$out/cancelled" /usr/bin/true "$out/cancel-tool"
grep -Fq '"Status":"Cancelled"' "$out/cancelled/campaign-failure.json"
grep -Fq '"Stage":"requirements-export"' "$out/cancelled/campaign-failure.json"
test ! -e "$out/cancelled/campaign-complete.json"

source "$repo/tools/setup-z3-linux.sh"
expect_exit 0 equiv-completed "$dotnet" "$equiv" "$out/completed"
grep -Fq '"Status":"Success"' "$out/completed/campaign-complete.json"
test ! -e "$out/completed/campaign-failure.json"
expect_exit 2 equiv-existing "$dotnet" "$equiv" "$out/completed"
grep -Fq '"Status":"Success"' "$out/completed/campaign-complete.json"
printf 'PASS structured validation, failed stage, cancellation and completion contracts\n'
