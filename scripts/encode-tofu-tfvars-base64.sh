#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF' >&2
Usage: scripts/encode-tofu-tfvars-base64.sh <path-to-terraform.tfvars>

Print the base64-encoded contents of a repo-specific OpenTofu var-file so it can
be stored in the GitHub Actions variable TOFU_TFVARS_BASE64.

The top-level image_tag entry is intentionally omitted because CI supplies the
deploy image tag at runtime.
EOF
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  usage
  exit 0
fi

if [[ $# -ne 1 ]]; then
  usage
  exit 1
fi

TFVARS_PATH="$1"

if [[ ! -f "$TFVARS_PATH" ]]; then
  echo "OpenTofu var-file not found: $TFVARS_PATH" >&2
  exit 1
fi

awk '
  /^[[:space:]]*image_tag[[:space:]]*=/ { next }
  { print }
' "$TFVARS_PATH" | base64 | tr -d '\n'
printf '\n'
