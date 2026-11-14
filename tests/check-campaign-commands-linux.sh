#!/usr/bin/env bash
# Run inside a memory-limited systemd scope/service. Outputs stay on native Linux storage.
set -euo pipefail
[[ $# == 1 && "$1" == /tmp/* && -d "$1" ]] || { echo 'Expected an existing /tmp run directory.' >&2; exit 2; }
out=$1
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
cgroup=$(awk -F: '$1 == "0" { print $3 }' /proc/self/cgroup)
limit=$(cat "/sys/fs/cgroup$cgroup/memory.max")
[[ "$limit" =~ ^[0-9]+$ && "$limit" -le 8589934592 ]] || {
  echo 'A process-tree memory limit of at most 8 GiB must be active.' >&2; exit 2;
}
[[ $(cat "/sys/fs/cgroup$cgroup/memory.oom.group") == 1 ]] || {
  echo 'The memory controller must terminate the whole process group on OOM.' >&2; exit 2;
}
df -Pk "$out"
printf 'Active process-tree memory limit %s bytes; artifacts %s\n' "$limit" "$out"
# Read the controller while the workload is still in its cgroup. Some systemd
# versions discard the controller's accounting after the last process exits.
trap 'printf "Memory controller peak and events\n"; cat "/sys/fs/cgroup$cgroup/memory.peak" "/sys/fs/cgroup$cgroup/memory.events"' EXIT
export CODEX_ARTIFACT_ROOT="$out"
cd -- "$repo"
source tools/setup-z3-linux.sh
dotnet test tests/SharpHDL.CommandTests -c Release -m:1 -nr:false -p:UseSharedCompilation=false \
  --artifacts-path "$out/managed" --collect:'XPlat Code Coverage' --results-directory "$out/coverage" --logger trx
bash tests/check-railway-campaigns.sh "$out/managed"
