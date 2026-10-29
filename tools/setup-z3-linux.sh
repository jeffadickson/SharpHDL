#!/usr/bin/env bash
# Source this file to select the exact native version matching Microsoft.Z3 4.12.2.
# This is a private cache restore, never a system installation or latest-version download.
set -euo pipefail
[[ $(uname -m) == x86_64 ]] || { echo 'Only Linux x64 Z3 assets are qualified.' >&2; return 2 2>/dev/null || exit 2; }
if [[ -z ${SHARPHDL_Z3_NATIVE_DIR:-} ]]; then
  test -d "$HOME"
  for _sharphdl_dir in "$HOME/.cache" "$HOME/.cache/sharphdl" "$HOME/.cache/sharphdl/z3-4.12.2"; do
    if [[ ! -d "$_sharphdl_dir" ]]; then test -d "$(dirname -- "$_sharphdl_dir")"; mkdir -- "$_sharphdl_dir"; fi
  done
  SHARPHDL_Z3_NATIVE_DIR="$HOME/.cache/sharphdl/z3-4.12.2"
fi
[[ "$SHARPHDL_Z3_NATIVE_DIR" == /* ]] || { echo 'Native asset directory must be absolute.' >&2; return 2 2>/dev/null || exit 2; }
if [[ ! -d "$SHARPHDL_Z3_NATIVE_DIR" ]]; then
  printf 'Pinned Z3 asset directory does not exist: %s\nUse the default verified cache or select an existing absolute directory.\n' "$SHARPHDL_Z3_NATIVE_DIR" >&2
  return 2 2>/dev/null || exit 2
fi
if [[ ! -f "$SHARPHDL_Z3_NATIVE_DIR/libz3.so" ]]; then
  if [[ ! -f "$SHARPHDL_Z3_NATIVE_DIR/z3.zip" ]]; then
    curl -fL --retry 2 https://github.com/Z3Prover/z3/releases/download/z3-4.12.2/z3-4.12.2-x64-glibc-2.31.zip -o "$SHARPHDL_Z3_NATIVE_DIR/z3.zip.pending"
    mv -- "$SHARPHDL_Z3_NATIVE_DIR/z3.zip.pending" "$SHARPHDL_Z3_NATIVE_DIR/z3.zip"
  fi
  printf '%s  %s\n' a198851a7403d8b25bab920bd4a4792efe1af5c28a5a932d840af0472d3a83eb "$SHARPHDL_Z3_NATIVE_DIR/z3.zip" | sha256sum -c -
  unzip -p "$SHARPHDL_Z3_NATIVE_DIR/z3.zip" z3-4.12.2-x64-glibc-2.31/bin/libz3.so > "$SHARPHDL_Z3_NATIVE_DIR/libz3.so.pending"
  mv -- "$SHARPHDL_Z3_NATIVE_DIR/libz3.so.pending" "$SHARPHDL_Z3_NATIVE_DIR/libz3.so"
  unzip -p "$SHARPHDL_Z3_NATIVE_DIR/z3.zip" z3-4.12.2-x64-glibc-2.31/LICENSE.txt > "$SHARPHDL_Z3_NATIVE_DIR/LICENSE.txt"
fi
printf '%s  %s\n' b6cc142a234129de3e8aab68439d71751f16885e27aad4af962836b32d8b2980 "$SHARPHDL_Z3_NATIVE_DIR/libz3.so" | sha256sum -c -
export SHARPHDL_Z3_NATIVE_DIR
export LD_LIBRARY_PATH="$SHARPHDL_Z3_NATIVE_DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
