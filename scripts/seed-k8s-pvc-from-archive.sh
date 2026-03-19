#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/seed-k8s-pvc-from-archive.sh --archive <path> --namespace <ns> --pvc <name> --artifact-repo <repo> --image-name <name> [options]

Builds a short-lived restore image that contains a local archive, pushes it
to Artifact Registry, creates a namespaced restore Job, waits for completion,
and streams the Job logs.

Required arguments:
  --archive                 Local .tgz archive created from the VM data root
  --namespace               Target Kubernetes namespace (for example: app-freetool)
  --pvc                     Target PVC name (for example: data)
  --artifact-repo           Per-app Artifact Registry repo ID (for example: freetool)
  --image-name              Image name to reuse for the restore image tag namespace

Optional arguments:
  --project-id              GCP project ID (default: GCP_PROJECT_ID or wonderly-idp-sso)
  --artifact-location       Artifact Registry location (default: ARTIFACT_REGISTRY_LOCATION or us-central1)
  --runtime-service-account Runtime service account name (default: runtime)
  --restore-root            PVC mount path inside the Job (default: /volume)
  --job-name                Restore Job name (default: sqlite-restore)
  --timeout                 kubectl wait timeout (default: 10m)
  --docker-platform         docker build platform (default: linux/amd64)
EOF
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Missing required command: $1" >&2
    exit 1
  }
}

log() {
  printf '[%s] %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*"
}

ensure_gke_auth_plugin() {
  local kubeconfig_paths="${KUBECONFIG:-${HOME}/.kube/config}"
  local kubeconfig_uses_plugin=false
  local previous_ifs="${IFS}"
  IFS=':'

  for kubeconfig_path in ${kubeconfig_paths}; do
    if [[ -f "${kubeconfig_path}" ]] && grep -q "gke-gcloud-auth-plugin" "${kubeconfig_path}"; then
      kubeconfig_uses_plugin=true
      break
    fi
  done

  IFS="${previous_ifs}"

  if [[ "${kubeconfig_uses_plugin}" != "true" ]]; then
    return 0
  fi

  if command -v gke-gcloud-auth-plugin >/dev/null 2>&1; then
    return 0
  fi

  local sdk_root=""
  sdk_root="$(gcloud info --format='value(installation.sdk_root)' 2>/dev/null || true)"

  if [[ -n "${sdk_root}" && -x "${sdk_root}/bin/gke-gcloud-auth-plugin" ]]; then
    export PATH="${sdk_root}/bin:${PATH}"
  fi

  if ! command -v gke-gcloud-auth-plugin >/dev/null 2>&1; then
    echo "Missing required command: gke-gcloud-auth-plugin" >&2
    echo "Install it or add the Google Cloud SDK bin directory to PATH before running restore jobs." >&2
    exit 1
  fi
}

archive_sha256() {
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | awk '{ print $1 }'
    return 0
  fi

  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{ print $1 }'
    return 0
  fi

  echo "Missing required command: shasum or sha256sum" >&2
  exit 1
}

wait_for_job_pod() {
  local pod_name=""
  local attempts=0

  while (( attempts < 60 )); do
    pod_name="$(
      kubectl -n "${NAMESPACE}" get pods -l "job-name=${JOB_NAME}" -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || true
    )"

    if [[ -n "${pod_name}" ]]; then
      printf '%s' "${pod_name}"
      return 0
    fi

    attempts=$((attempts + 1))
    sleep 2
  done

  return 1
}

print_failure_context() {
  kubectl -n "${NAMESPACE}" describe "job/${JOB_NAME}" || true
  kubectl -n "${NAMESPACE}" logs "job/${JOB_NAME}" --all-containers=true --tail=-1 || true
}

ARCHIVE_PATH=""
NAMESPACE=""
PVC_NAME=""
ARTIFACT_REGISTRY_REPO=""
IMAGE_NAME=""
PROJECT_ID="${GCP_PROJECT_ID:-wonderly-idp-sso}"
ARTIFACT_REGISTRY_LOCATION="${ARTIFACT_REGISTRY_LOCATION:-us-central1}"
RUNTIME_SERVICE_ACCOUNT="runtime"
RESTORE_ROOT="/volume"
JOB_NAME="sqlite-restore"
JOB_TIMEOUT="10m"
DOCKER_PLATFORM="linux/amd64"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --archive)
      ARCHIVE_PATH="$2"
      shift 2
      ;;
    --namespace)
      NAMESPACE="$2"
      shift 2
      ;;
    --pvc)
      PVC_NAME="$2"
      shift 2
      ;;
    --artifact-repo)
      ARTIFACT_REGISTRY_REPO="$2"
      shift 2
      ;;
    --image-name)
      IMAGE_NAME="$2"
      shift 2
      ;;
    --project-id)
      PROJECT_ID="$2"
      shift 2
      ;;
    --artifact-location)
      ARTIFACT_REGISTRY_LOCATION="$2"
      shift 2
      ;;
    --runtime-service-account)
      RUNTIME_SERVICE_ACCOUNT="$2"
      shift 2
      ;;
    --restore-root)
      RESTORE_ROOT="$2"
      shift 2
      ;;
    --job-name)
      JOB_NAME="$2"
      shift 2
      ;;
    --timeout)
      JOB_TIMEOUT="$2"
      shift 2
      ;;
    --docker-platform)
      DOCKER_PLATFORM="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

for cmd in docker gcloud kubectl; do
  require_cmd "${cmd}"
done

ensure_gke_auth_plugin

if [[ -z "${ARCHIVE_PATH}" || -z "${NAMESPACE}" || -z "${PVC_NAME}" || -z "${ARTIFACT_REGISTRY_REPO}" || -z "${IMAGE_NAME}" ]]; then
  usage >&2
  exit 1
fi

if [[ ! -f "${ARCHIVE_PATH}" ]]; then
  echo "Archive not found: ${ARCHIVE_PATH}" >&2
  exit 1
fi

REGISTRY_HOST="${ARTIFACT_REGISTRY_LOCATION}-docker.pkg.dev"
ARCHIVE_HASH="$(archive_sha256 "${ARCHIVE_PATH}")"
IMAGE_TAG="${IMAGE_TAG:-restore-$(date '+%Y%m%d%H%M%S')-${ARCHIVE_HASH:0:12}}"
RESTORE_IMAGE_URI="${REGISTRY_HOST}/${PROJECT_ID}/${ARTIFACT_REGISTRY_REPO}/${IMAGE_NAME}:${IMAGE_TAG}"
TMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/seed-k8s-pvc.XXXXXX")"
MANIFEST_PATH="${TMP_DIR}/job.yaml"
LOG_PID=""

cleanup() {
  if [[ -n "${LOG_PID}" ]]; then
    wait "${LOG_PID}" 2>/dev/null || true
  fi

  rm -rf "${TMP_DIR}"
}

trap cleanup EXIT

cp "${ARCHIVE_PATH}" "${TMP_DIR}/archive.tgz"

cat > "${TMP_DIR}/restore.sh" <<'EOF'
#!/bin/sh
set -eu

restore_target="${RESTORE_TARGET:-/volume}"

require_root() {
  root_name="$1"

  if ! tar -tzf /seed/archive.tgz | awk -F/ 'NF { print $1 }' | sort -u | grep -qx "${root_name}"; then
    echo "Expected archive to contain ${root_name}/ at the top level." >&2
    exit 1
  fi
}

require_root "freetool-db"
require_root "openfga"

mkdir -p "${restore_target}"
for existing_path in "${restore_target}"/* "${restore_target}"/.[!.]* "${restore_target}"/..?*; do
  case "${existing_path}" in
    "${restore_target}/lost+found")
      continue
      ;;
  esac

  if [ -e "${existing_path}" ]; then
    rm -rf "${existing_path}"
  fi
done

tar -xzf /seed/archive.tgz -C "${restore_target}"
chmod -R a+rwX "${restore_target}"

echo "Restore complete. Seeded ${restore_target} with:"
find "${restore_target}" -maxdepth 2 -mindepth 1 | sort
EOF

cat > "${TMP_DIR}/Dockerfile" <<'EOF'
FROM alpine:3.21

RUN apk add --no-cache tar

WORKDIR /seed
COPY archive.tgz /seed/archive.tgz
COPY restore.sh /seed/restore.sh

RUN chmod +x /seed/restore.sh

ENTRYPOINT ["/seed/restore.sh"]
EOF

cat > "${MANIFEST_PATH}" <<EOF
apiVersion: batch/v1
kind: Job
metadata:
  name: ${JOB_NAME}
  namespace: ${NAMESPACE}
  labels:
    app.kubernetes.io/managed-by: codex
    internal-tools.wonderly.io/service: app
spec:
  backoffLimit: 0
  ttlSecondsAfterFinished: 86400
  template:
    metadata:
      labels:
        internal-tools.wonderly.io/service: app
    spec:
      serviceAccountName: ${RUNTIME_SERVICE_ACCOUNT}
      restartPolicy: Never
      containers:
        - name: restore
          image: ${RESTORE_IMAGE_URI}
          imagePullPolicy: Always
          env:
            - name: RESTORE_TARGET
              value: ${RESTORE_ROOT}
          volumeMounts:
            - name: data
              mountPath: ${RESTORE_ROOT}
      volumes:
        - name: data
          persistentVolumeClaim:
            claimName: ${PVC_NAME}
EOF

log "Building restore image ${RESTORE_IMAGE_URI}"
gcloud auth configure-docker "${REGISTRY_HOST}" --quiet
docker build --platform "${DOCKER_PLATFORM}" -t "${RESTORE_IMAGE_URI}" "${TMP_DIR}"
docker push "${RESTORE_IMAGE_URI}"

log "Deleting any previous restore job named ${JOB_NAME}"
kubectl -n "${NAMESPACE}" delete "job/${JOB_NAME}" --ignore-not-found --wait=true

log "Creating restore job ${JOB_NAME} in namespace ${NAMESPACE}"
kubectl apply -f "${MANIFEST_PATH}"

if POD_NAME="$(wait_for_job_pod)"; then
  log "Streaming restore job logs from pod ${POD_NAME}"
  kubectl -n "${NAMESPACE}" logs -f "${POD_NAME}" &
  LOG_PID=$!
fi

if ! kubectl -n "${NAMESPACE}" wait --for=condition=complete "job/${JOB_NAME}" --timeout "${JOB_TIMEOUT}"; then
  echo "Restore job ${JOB_NAME} did not complete successfully." >&2
  print_failure_context
  exit 1
fi

if [[ -n "${LOG_PID}" ]]; then
  wait "${LOG_PID}" || true
fi

cat <<EOF
Restore finished.

namespace=${NAMESPACE}
job=${JOB_NAME}
pvc=${PVC_NAME}
image=${RESTORE_IMAGE_URI}
EOF
