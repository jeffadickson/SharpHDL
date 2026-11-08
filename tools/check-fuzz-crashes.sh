#!/usr/bin/env bash
set -euo pipefail
if [[ $# != 1 || ! -d "$1" ]]; then
  printf 'Expected an existing AFL crash evidence directory.\n' >&2
  exit 2
fi

# Capture the scan result separately so a failed scan cannot look empty.
if ! crash=$(find "$1" -type f ! -name README.txt -print -quit); then
  printf 'Could not scan AFL crash evidence in %s.\n' "$1" >&2
  exit 2
fi
if [[ -n "$crash" ]]; then
  printf 'AFL retained a crash at %s.\n' "$crash" >&2
  exit 1
fi
