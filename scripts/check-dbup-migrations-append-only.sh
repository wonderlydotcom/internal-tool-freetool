#!/usr/bin/env bash
set -euo pipefail

log() {
  printf '[dbup-append-only] %s\n' "$*"
}

fail() {
  printf '[dbup-append-only] ERROR: %s\n' "$*" >&2
  exit 1
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

is_dbup_migration_path() {
  local path="$1"

  case "${path}" in
    src/**/Database/Migrations/*.sql|*DBUP*.sql|*DbUp*.sql|*dbup*.sql|*Migration*.sql|*migration*.sql)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

event_name="${EVENT_NAME:-}"
if [[ -n "${event_name}" && "${event_name}" != "pull_request" ]]; then
  log "Not a pull_request event; skipping append-only migration guard."
  exit 0
fi

base_ref="${BASE_SHA:-}"
head_ref="${HEAD_SHA:-HEAD}"

if [[ -z "${base_ref}" ]]; then
  if git rev-parse --verify origin/main >/dev/null 2>&1; then
    base_ref="$(git merge-base origin/main HEAD)"
  else
    fail "BASE_SHA is required when origin/main is unavailable."
  fi
fi

git rev-parse --verify "${base_ref}^{commit}" >/dev/null 2>&1 || fail "BASE_SHA is not a valid commit: ${base_ref}"
git rev-parse --verify "${head_ref}^{commit}" >/dev/null 2>&1 || fail "HEAD_SHA is not a valid commit: ${head_ref}"

issues=()

while IFS= read -r -d '' status; do
  code="${status:0:1}"

  case "${code}" in
    R|C)
      IFS= read -r -d '' old_path || fail "Unexpected end of git diff output after ${status}."
      IFS= read -r -d '' new_path || fail "Unexpected end of git diff output after ${status} ${old_path}."

      if is_dbup_migration_path "${old_path}" || is_dbup_migration_path "${new_path}"; then
        issues+=("${status}: ${old_path} -> ${new_path}")
      fi
      ;;
    *)
      IFS= read -r -d '' path || fail "Unexpected end of git diff output after ${status}."

      if is_dbup_migration_path "${path}" && [[ "${code}" != "A" ]]; then
        issues+=("${status}: ${path}")
      fi
      ;;
  esac
done < <(git diff --name-status --find-renames -z "${base_ref}...${head_ref}")

if (( ${#issues[@]} > 0 )); then
  cat >&2 <<'EOF'
[dbup-append-only] ERROR: DBUp migrations are append-only.
[dbup-append-only] ERROR: Do not modify, delete, rename, or copy existing DBUp SQL scripts after they may have been applied.
[dbup-append-only] ERROR: Add a new DBUp migration instead so existing databases can advance from their journaled state.
[dbup-append-only] ERROR: Offending migration changes:
EOF

  for issue in "${issues[@]}"; do
    printf '[dbup-append-only] ERROR: - %s\n' "${issue}" >&2
  done

  exit 1
fi

log "DBUp migration changes are append-only."
