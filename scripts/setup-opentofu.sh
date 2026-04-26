#!/usr/bin/env bash
set -euo pipefail

DEFAULT_OPENTOFU_VERSION="1.8.8"
MAX_ATTEMPTS=4
BACKOFF_SECONDS=(15 30 45)

log() {
  printf '%s\n' "$*"
}

fail() {
  printf 'Error: %s\n' "$*" >&2
  exit 1
}

require_command() {
  local command_name="$1"

  if ! command -v "${command_name}" >/dev/null 2>&1; then
    fail "Missing required command: ${command_name}"
  fi
}

resolve_version() {
  if [[ -n "${OPENTOFU_VERSION:-}" ]]; then
    printf '%s\n' "${OPENTOFU_VERSION}"
    return
  fi

  if [[ -f ".opentofu-version" ]]; then
    tr -d '[:space:]' <".opentofu-version"
    return
  fi

  printf '%s\n' "${DEFAULT_OPENTOFU_VERSION}"
}

detect_os() {
  case "$(uname -s)" in
    Linux)
      printf 'linux\n'
      ;;
    Darwin)
      printf 'darwin\n'
      ;;
    *)
      fail "Unsupported operating system: $(uname -s)"
      ;;
  esac
}

detect_arch() {
  case "$(uname -m)" in
    x86_64 | amd64)
      printf 'amd64\n'
      ;;
    aarch64 | arm64)
      printf 'arm64\n'
      ;;
    *)
      fail "Unsupported CPU architecture: $(uname -m)"
      ;;
  esac
}

download_file() {
  local url="$1"
  local destination="$2"

  curl \
    --fail \
    --location \
    --show-error \
    --silent \
    --connect-timeout 20 \
    --retry 0 \
    "${url}" \
    --output "${destination}"
}

verify_checksum() {
  local checksum_file="$1"
  local artifact_file="$2"
  local artifact_name="$3"
  local checksum_line="${artifact_file}.sha256"
  local expected
  local actual

  if ! awk -v artifact="${artifact_name}" '$2 == artifact { print; found = 1 } END { exit found ? 0 : 1 }' "${checksum_file}" >"${checksum_line}"; then
    printf 'Checksum file does not contain %s\n' "${artifact_name}" >&2
    return 1
  fi

  if command -v sha256sum >/dev/null 2>&1; then
    (
      cd "$(dirname "${artifact_file}")"
      sha256sum -c "$(basename "${checksum_line}")"
    )
    return
  fi

  if command -v shasum >/dev/null 2>&1; then
    expected="$(awk '{ print $1 }' "${checksum_line}")"
    actual="$(shasum -a 256 "${artifact_file}" | awk '{ print $1 }')"

    if [[ "${actual}" != "${expected}" ]]; then
      printf 'Checksum mismatch for %s\n' "${artifact_name}" >&2
      return 1
    fi

    return 0
  fi

  printf 'Neither sha256sum nor shasum is available for checksum verification.\n' >&2
  return 1
}

cleanup_work_dir() {
  local work_dir="$1"

  if [[ -n "${work_dir}" && -d "${work_dir}" ]]; then
    rm -rf "${work_dir}"
  fi
}

install_once() {
  local work_dir
  local extract_dir
  local artifact_path
  local checksum_path
  local tofu_path

  work_dir="$(mktemp -d)"
  extract_dir="${work_dir}/extract"
  artifact_path="${work_dir}/${ARTIFACT_NAME}"
  checksum_path="${work_dir}/${CHECKSUM_NAME}"
  tofu_path=""

  mkdir -p "${extract_dir}"

  if ! download_file "${RELEASE_URL}/${ARTIFACT_NAME}" "${artifact_path}"; then
    printf 'Failed to download %s\n' "${ARTIFACT_NAME}" >&2
    cleanup_work_dir "${work_dir}"
    return 1
  fi

  if ! download_file "${RELEASE_URL}/${CHECKSUM_NAME}" "${checksum_path}"; then
    printf 'Failed to download %s\n' "${CHECKSUM_NAME}" >&2
    cleanup_work_dir "${work_dir}"
    return 1
  fi

  if ! verify_checksum "${checksum_path}" "${artifact_path}" "${ARTIFACT_NAME}"; then
    cleanup_work_dir "${work_dir}"
    return 1
  fi

  if ! unzip -q "${artifact_path}" -d "${extract_dir}"; then
    printf 'Failed to extract %s\n' "${ARTIFACT_NAME}" >&2
    cleanup_work_dir "${work_dir}"
    return 1
  fi

  if [[ -f "${extract_dir}/tofu" ]]; then
    tofu_path="${extract_dir}/tofu"
  else
    tofu_path="$(find "${extract_dir}" -type f -name tofu -print -quit)"
  fi

  if [[ -z "${tofu_path}" || ! -f "${tofu_path}" ]]; then
    printf 'Extracted archive did not contain the tofu binary.\n' >&2
    cleanup_work_dir "${work_dir}"
    return 1
  fi

  mkdir -p "${INSTALL_DIR}"
  install -m 0755 "${tofu_path}" "${INSTALL_DIR}/tofu"
  cleanup_work_dir "${work_dir}"
}

verify_installed_version() {
  local version_output

  if ! version_output="$("${INSTALL_DIR}/tofu" version 2>&1)"; then
    printf '%s\n' "${version_output}" >&2
    return 1
  fi

  printf '%s\n' "${version_output}"

  if [[ "${version_output}" != *"OpenTofu v${OPENTOFU_VERSION}"* ]]; then
    printf 'Expected OpenTofu v%s, but installed version output was different.\n' "${OPENTOFU_VERSION}" >&2
    return 1
  fi
}

require_command awk
require_command curl
require_command find
require_command install
require_command mktemp
require_command unzip

OPENTOFU_VERSION="$(resolve_version)"
OPENTOFU_VERSION="${OPENTOFU_VERSION#v}"

if [[ -z "${OPENTOFU_VERSION}" ]]; then
  fail "OpenTofu version is empty."
fi

if [[ "${OPENTOFU_VERSION}" == "latest" ]]; then
  fail "Refusing to install OpenTofu 'latest'; use a pinned version."
fi

if [[ ! "${OPENTOFU_VERSION}" =~ ^[0-9]+[.][0-9]+[.][0-9]+(-[A-Za-z0-9._-]+)?$ ]]; then
  fail "OpenTofu version must be an exact pinned version, got '${OPENTOFU_VERSION}'."
fi

PLATFORM_OS="$(detect_os)"
PLATFORM_ARCH="$(detect_arch)"
ARTIFACT_NAME="tofu_${OPENTOFU_VERSION}_${PLATFORM_OS}_${PLATFORM_ARCH}.zip"
CHECKSUM_NAME="tofu_${OPENTOFU_VERSION}_SHA256SUMS"
RELEASE_URL="https://github.com/opentofu/opentofu/releases/download/v${OPENTOFU_VERSION}"

if [[ -n "${OPENTOFU_INSTALL_ROOT:-}" ]]; then
  INSTALL_ROOT="${OPENTOFU_INSTALL_ROOT}"
elif [[ -n "${RUNNER_TEMP:-}" ]]; then
  INSTALL_ROOT="${RUNNER_TEMP}/opentofu"
elif [[ -n "${HOME:-}" ]]; then
  INSTALL_ROOT="${HOME}/.local/share/opentofu"
else
  fail "Set OPENTOFU_INSTALL_ROOT when HOME and RUNNER_TEMP are unavailable."
fi

INSTALL_DIR="${INSTALL_ROOT}/${OPENTOFU_VERSION}/${PLATFORM_OS}_${PLATFORM_ARCH}/bin"

for ((attempt = 1; attempt <= MAX_ATTEMPTS; attempt++)); do
  log "Installing OpenTofu ${OPENTOFU_VERSION} (${PLATFORM_OS}_${PLATFORM_ARCH}), attempt ${attempt}/${MAX_ATTEMPTS}..."

  if install_once && verify_installed_version; then
    if [[ -n "${GITHUB_PATH:-}" ]]; then
      printf '%s\n' "${INSTALL_DIR}" >>"${GITHUB_PATH}"
      log "Added ${INSTALL_DIR} to GITHUB_PATH."
    else
      log "Installed OpenTofu at ${INSTALL_DIR}."
    fi

    exit 0
  fi

  if ((attempt == MAX_ATTEMPTS)); then
    break
  fi

  sleep_seconds="${BACKOFF_SECONDS[$((attempt - 1))]}"
  printf 'OpenTofu install failed on attempt %s; retrying in %ss...\n' "${attempt}" "${sleep_seconds}" >&2
  sleep "${sleep_seconds}"
done

printf 'OpenTofu install failed after %s attempts.\n' "${MAX_ATTEMPTS}" >&2
exit 1
