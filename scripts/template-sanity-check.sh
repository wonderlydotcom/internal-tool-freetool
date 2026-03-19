#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

FAIL=0

echo "Running template sanity checks..."

declare -a BLOCKED_PATHS=(
  "infra/foundation/opentofu/backend.hcl"
  "infra/foundation/opentofu/terraform.tfvars"
  "infra/foundation/opentofu/terraform.tfstate"
  "infra/foundation/opentofu/terraform.tfstate.backup"
  "infra/opentofu/backend.gcs.hcl"
  "infra/opentofu/terraform.tfvars"
  "infra/opentofu/terraform.tfstate"
  "infra/opentofu/terraform.tfstate.backup"
)

for path in "${BLOCKED_PATHS[@]}"; do
  if [ -e "$path" ]; then
    echo "[FAIL] Local artifact should not exist in template: $path"
    FAIL=1
  fi
done

if [ "$FAIL" -ne 0 ]; then
  echo "Template sanity checks failed."
  exit 1
fi

echo "Template sanity checks passed."
