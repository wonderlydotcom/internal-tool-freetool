#!/usr/bin/env bash
set -euo pipefail

log() {
  printf '[migration-gate] %s\n' "$*"
}

fail() {
  printf '[migration-gate] ERROR: %s\n' "$*" >&2
  exit 1
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || fail "Missing required command: $1"
}

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

require_cmd docker
require_cmd curl
require_cmd python3
require_cmd internal-tools

repo_name="$(basename "${repo_root}")"
default_app_id="${repo_name}"
case "${default_app_id}" in
  internal-tools-control-plane) default_app_id="it" ;;
  internal-tool-*) default_app_id="${default_app_id#internal-tool-}" ;;
  internal-tools-*) default_app_id="${default_app_id#internal-tools-}" ;;
esac

app_id="${MIGRATION_GATE_APP_ID:-${default_app_id}}"

if [[ "${repo_name}" == "internal-tools-starter" || "${repo_name}" == "internal-tools-starter-durable-workflows-plan" ]]; then
  log "Template repository detected; migration prod snapshot gate is installed for generated apps and skipped here."
  exit 0
fi

health_path="${MIGRATION_GATE_HEALTH_PATH:-/healthy}"
health_timeout_seconds="${MIGRATION_GATE_HEALTH_TIMEOUT_SECONDS:-180}"
backup_retry_attempts="${MIGRATION_GATE_BACKUP_RETRY_ATTEMPTS:-6}"
backup_retry_sleep_seconds="${MIGRATION_GATE_BACKUP_RETRY_SLEEP_SECONDS:-20}"
image_tag="${MIGRATION_GATE_IMAGE_TAG:-${repo_name}:migration-gate-${GITHUB_SHA:-local}}"
container_name="migration-gate-${repo_name}-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}-$$"
work_root="${RUNNER_TEMP:-${TMPDIR:-/tmp}}/migration-gate-${repo_name}-$$"
archive_path="${work_root}/backup.tar.gz"
extract_dir="${work_root}/extract"
data_dir="${work_root}/data"
logs_dir="${work_root}/logs"
container_log="${logs_dir}/container.log"

remove_work_root() {
  [[ -e "${work_root}" ]] || return 0

  if rm -rf "${work_root}" 2>/dev/null; then
    return 0
  fi

  log "Standard cleanup could not remove ${work_root}; retrying with sudo if available."
  if command -v sudo >/dev/null 2>&1 && sudo -n true 2>/dev/null; then
    if sudo rm -rf "${work_root}"; then
      return 0
    fi
  fi

  log "WARNING: Could not remove temporary migration gate files at ${work_root}"
  return 0
}

# shellcheck disable=SC2317 # cleanup is invoked by trap.
cleanup() {
  local status=$?
  set +e

  docker rm -f "${container_name}" >/dev/null 2>&1
  if [[ "${MIGRATION_GATE_KEEP_TEMP:-false}" != "true" ]]; then
    remove_work_root
  else
    log "Keeping temporary files at ${work_root} because MIGRATION_GATE_KEEP_TEMP=true"
  fi

  return "${status}"
}
trap cleanup EXIT

mkdir -p "${extract_dir}" "${data_dir}" "${logs_dir}"

appsettings_path="$(find src -path '*/appsettings.json' -not -path '*/bin/*' -not -path '*/obj/*' -print 2>/dev/null | sort | head -n 1 || true)"
[[ -n "${appsettings_path}" ]] || fail "Could not find src/*/appsettings.json to infer SQLite DB name. Set MIGRATION_GATE_DB_BASENAME."

db_basename="${MIGRATION_GATE_DB_BASENAME:-$(python3 - "${appsettings_path}" <<'PY'
import json, re, sys
from pathlib import PurePosixPath
payload=json.load(open(sys.argv[1], encoding='utf-8'))
conn=(payload.get('ConnectionStrings') or {}).get('DefaultConnection') or ''
match=re.search(r'Data Source=([^;]+)', conn, re.I)
if not match:
    raise SystemExit('')
print(PurePosixPath(match.group(1).strip()).name)
PY
)}"
[[ -n "${db_basename}" ]] || fail "Could not infer SQLite DB basename. Set MIGRATION_GATE_DB_BASENAME."

dockerfile_path="${MIGRATION_GATE_DOCKERFILE:-$(find src -path '*/Dockerfile' -not -path '*/bin/*' -not -path '*/obj/*' -print 2>/dev/null | sort | head -n 1 || true)}"
[[ -n "${dockerfile_path}" ]] || fail "Could not find API Dockerfile. Set MIGRATION_GATE_DOCKERFILE."

log "app_id=${app_id}"
log "db_basename=${db_basename}"
log "dockerfile=${dockerfile_path}"
log "image=${image_tag}"

for attempt in $(seq 1 "${backup_retry_attempts}"); do
  log "Downloading fresh SQLite snapshot with internal-tools (attempt ${attempt}/${backup_retry_attempts})"
  rm -f "${archive_path}" "${archive_path%.tar.gz}.metadata.json" "${archive_path%.tgz}.metadata.json"

  if internal-tools sqlite download "${app_id}" --fresh --output "${archive_path}" --github-oidc --timeout-seconds 900; then
    break
  fi

  if [[ "${attempt}" == "${backup_retry_attempts}" ]]; then
    fail "Could not download a fresh SQLite snapshot after ${backup_retry_attempts} attempts."
  fi

  log "Snapshot download failed; retrying in ${backup_retry_sleep_seconds}s."
  sleep "${backup_retry_sleep_seconds}"
done

[[ -s "${archive_path}" ]] || fail "SQLite backup archive was not created: ${archive_path}"

log "Extracting SQLite archive into ephemeral temp data directory"
python3 - "${archive_path}" "${extract_dir}" "${db_basename}" "${data_dir}" <<'PY'
import os, shutil, sys, tarfile
from pathlib import Path, PurePosixPath
archive=Path(sys.argv[1])
dest=Path(sys.argv[2])
expected=sys.argv[3]
data_dir=Path(sys.argv[4])
seen=[]
with tarfile.open(archive, 'r:gz') as tar:
    for member in tar.getmembers():
        name=member.name
        posix=PurePosixPath(name)
        if member.isdir():
            continue
        if member.issym() or member.islnk():
            raise SystemExit(f'Backup archive contains a link entry, refusing to extract: {name}')
        if member.isfile() is False:
            raise SystemExit(f'Backup archive contains unsupported entry, refusing to extract: {name}')
        if posix.is_absolute() or '..' in posix.parts:
            raise SystemExit(f'Backup archive contains unsafe path, refusing to extract: {name}')
        if len(posix.parts) < 2 or posix.parts[0] != 'sqlite':
            raise SystemExit(f'Backup archive entry is outside sqlite/, refusing to extract: {name}')
        target=dest.joinpath(*posix.parts[1:])
        target.parent.mkdir(parents=True, exist_ok=True)
        src=tar.extractfile(member)
        if src is None:
            raise SystemExit(f'Could not read archive member: {name}')
        with target.open('wb') as out:
            shutil.copyfileobj(src, out)
        seen.append(target)

matches=[p for p in seen if p.name == expected]
if not matches:
    available=', '.join(str(p.relative_to(dest)) for p in seen) or '(none)'
    raise SystemExit(f'Backup does not contain expected database {expected}. Available: {available}')
if len(matches) > 1:
    raise SystemExit(f'Backup contains multiple databases named {expected}: {matches}')

data_dir.mkdir(parents=True, exist_ok=True)
shutil.copy2(matches[0], data_dir / expected)
print(data_dir / expected)
PY

[[ -s "${data_dir}/${db_basename}" ]] || fail "Extracted DB is missing or empty: ${data_dir}/${db_basename}"

log "Building candidate image"
docker build -t "${image_tag}" -f "${dockerfile_path}" .

log "Starting candidate container against prod-shaped SQLite copy"
docker run -d \
  --name "${container_name}" \
  -p 127.0.0.1::8080 \
  -v "${data_dir}:/app/data" \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e Auth__DataProtection__KeysPath=/tmp/migration-gate-data-protection-keys \
  -e "ConnectionStrings__DefaultConnection=Data Source=/app/data/${db_basename}" \
  -e AdAgent__WorkerEnabled=false \
  -e SnowflakeSync__Enabled=false \
  -e DOTNET_PRINT_TELEMETRY_MESSAGE=false \
  "${image_tag}" >/dev/null

host_port="$(docker port "${container_name}" 8080/tcp | sed -E 's/.*:([0-9]+)$/\1/' | head -n 1)"
[[ -n "${host_port}" ]] || fail "Could not resolve mapped health-check port."

log "Polling http://127.0.0.1:${host_port}${health_path} for up to ${health_timeout_seconds}s"
deadline=$(( $(date +%s) + health_timeout_seconds ))
last_status=""
while [[ "$(date +%s)" -le "${deadline}" ]]; do
  if ! docker inspect "${container_name}" --format '{{.State.Running}}' 2>/dev/null | grep -q '^true$'; then
    docker logs "${container_name}" >"${container_log}" 2>&1 || true
    tail -200 "${container_log}" >&2 || true
    fail "Candidate container exited before becoming healthy."
  fi

  if body="$(curl -fsS --max-time 5 "http://127.0.0.1:${host_port}${health_path}" 2>/dev/null)"; then
    if [[ "${body}" == "OK" || -n "${body}" ]]; then
      log "Migration gate passed: candidate container returned healthy response: ${body}"
      docker logs "${container_name}" >"${container_log}" 2>&1 || true
      exit 0
    fi
  else
    last_status="curl failed"
  fi

  sleep 3
done

docker logs "${container_name}" >"${container_log}" 2>&1 || true
tail -200 "${container_log}" >&2 || true
fail "Timed out waiting for /healthy (${last_status})."
