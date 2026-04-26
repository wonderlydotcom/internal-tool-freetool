#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: scripts/deploy-app-from-tofu.sh [additional tofu apply args]

Builds and pushes the application image, applies the app-owned OpenTofu stack,
and waits for the StatefulSet rollout.

Environment overrides:
  INFRA_DIR                    OpenTofu directory (default: infra/opentofu)
  IMAGE_TAG                    Tag to build and deploy (default: git sha or timestamp)
  IMAGE_DIGEST                 Immutable image digest to deploy (normally resolved after push)
  IMAGE_NAME                   Image name inside Artifact Registry
  GCP_PROJECT_ID               Shared GCP project ID
  ARTIFACT_REGISTRY_LOCATION   Artifact Registry location
  ARTIFACT_REGISTRY_REPO       Per-app Artifact Registry repo ID
  ROLLOUT_TIMEOUT              kubectl rollout timeout (default: 5m)
  SMOKE_TIMEOUT                Candidate smoke Pod health timeout (default: 3m)
  DOCKER_PLATFORM              docker build platform (default: linux/amd64)
  PUBLISH_LATEST               Also push :latest when IMAGE_TAG != latest (default: false)
  SKIP_PRE_PROMOTION_SMOKE     Skip the candidate smoke workload gate (default: false)
  ALLOW_ZERO_READY_BEFORE_DEPLOY
                               Allow normal deploy when prod has zero ready pods (default: false)
  ALLOW_SCALE_TO_ZERO_RECOVERY Allow legacy scale-to-zero recovery (default: false)
  SKIP_PUBLIC_HEALTH_CHECK     Skip external health URL verification (default: false)
  PUBLIC_HEALTH_URL            External health URL (default: https://<domain>/healthy)
  PUBLIC_HEALTH_ALLOWED_CODES  Space-separated accepted status codes (default: 200 204 301 302 401 403)
  PUBLIC_HEALTH_TIMEOUT        External health verification timeout (default: 2m)

If this is the first deployment and tofu outputs do not exist yet, set
GCP_PROJECT_ID, ARTIFACT_REGISTRY_LOCATION, and ARTIFACT_REGISTRY_REPO
explicitly or run an initial tofu apply first.

If KUBECONFIG is set and TF_VAR_kubeconfig_path is not, the script exports the
first kubeconfig path into TF_VAR_kubeconfig_path so OpenTofu uses the same
cluster credentials as kubectl.
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
    echo "Install it or add the Google Cloud SDK bin directory to PATH before deploying." >&2
    exit 1
  fi
}

sync_tf_var_kubeconfig_path() {
  if [[ -n "${TF_VAR_kubeconfig_path:-}" || -z "${KUBECONFIG:-}" ]]; then
    return 0
  fi

  local kubeconfig_path="${KUBECONFIG%%:*}"

  if [[ -z "${kubeconfig_path}" ]]; then
    return 0
  fi

  export TF_VAR_kubeconfig_path="${kubeconfig_path}"
  log "Using kubeconfig ${TF_VAR_kubeconfig_path} for OpenTofu"
}

read_output() {
  local key="$1"
  jq -r --arg key "$key" '.[$key].value // empty' <<<"${OUTPUT_JSON}"
}

resolve_value() {
  local env_name="$1"
  local output_key="$2"
  local label="$3"
  local value="${!env_name:-}"

  if [[ -z "${value}" ]]; then
    value="$(read_output "${output_key}")"
  fi

  if [[ -z "${value}" || "${value}" == "null" ]]; then
    echo "Missing ${label}. Set ${env_name} or run 'tofu -chdir=${INFRA_DIR} apply' once first." >&2
    exit 1
  fi

  printf '%s' "${value}"
}

read_secret_provider_resource_names() {
  local namespace="$1"
  local secret_provider_class_name="$2"

  kubectl -n "${namespace}" get secretproviderclass "${secret_provider_class_name}" -o json \
    | jq -r '.spec.parameters.secrets // ""' \
    | awk -F'"' '/resourceName:/ {print $2}'
}

is_secret_manager_versions_permission_denied() {
  local output="$1"

  [[ "${output}" == *"PERMISSION_DENIED"* ]] || [[ "${output}" == *"secretmanager.versions.list"* ]]
}

validate_runtime_secret_versions() {
  local project_id="$1"
  local namespace="$2"
  local secret_provider_class_name="$3"
  local resource_name=""
  local secret_id=""
  local enabled_version=""
  local gcloud_output=""
  local missing_secret_ids=()

  if [[ -z "${secret_provider_class_name}" || "${secret_provider_class_name}" == "null" ]]; then
    return 0
  fi

  if ! kubectl -n "${namespace}" get secretproviderclass "${secret_provider_class_name}" >/dev/null 2>&1; then
    echo "Missing SecretProviderClass ${secret_provider_class_name} in namespace ${namespace}." >&2
    exit 1
  fi

  while IFS= read -r resource_name; do
    [[ -n "${resource_name}" ]] || continue

    if [[ "${resource_name}" =~ ^projects/[^/]+/secrets/([^/]+)/versions/[^/]+$ ]]; then
      secret_id="${BASH_REMATCH[1]}"
    else
      echo "Could not parse Secret Manager resource name: ${resource_name}" >&2
      exit 1
    fi

    if ! gcloud_output="$(
      gcloud secrets versions list "${secret_id}" \
        --project "${project_id}" \
        --filter "state=enabled" \
        --format 'value(name)' \
        --limit 1 2>&1
    )"; then
      if is_secret_manager_versions_permission_denied "${gcloud_output}"; then
        log "Skipping runtime secret version validation because the current Google Cloud identity cannot list Secret Manager versions. Shared platform filtering remains the authoritative zero-version guard in CI."
        return 0
      fi

      echo "Failed to inspect enabled Secret Manager versions for ${secret_id}: ${gcloud_output}" >&2
      exit 1
    fi

    enabled_version="${gcloud_output}"

    if [[ -z "${enabled_version}" ]]; then
      missing_secret_ids+=("${secret_id}")
    fi
  done < <(read_secret_provider_resource_names "${namespace}" "${secret_provider_class_name}")

  if [[ "${#missing_secret_ids[@]}" -gt 0 ]]; then
    echo "Refusing to deploy: SecretProviderClass ${secret_provider_class_name} in namespace ${namespace} references secrets with no enabled versions:" >&2
    printf '  - %s\n' "${missing_secret_ids[@]}" >&2
    echo "Upload a secret version or remove the secret from the platform app catalog before rolling pods." >&2
    exit 1
  fi
}

is_true() {
  local value="${1:-}"

  [[ "${value}" == "true" || "${value}" == "1" || "${value}" == "yes" || "${value}" == "y" ]]
}

validate_image_digest() {
  local digest="$1"

  [[ "${digest}" =~ ^sha256:[a-f0-9]{64}$ ]]
}

make_k8s_name() {
  local raw="$1"
  local name=""

  name="$(
    printf '%s' "${raw}" \
      | tr '[:upper:]' '[:lower:]' \
      | sed -E 's/[^a-z0-9-]+/-/g; s/^-+//; s/-+$//' \
      | cut -c1-63 \
      | sed -E 's/-+$//'
  )"

  if [[ -z "${name}" ]]; then
    name="deploy-smoke"
  fi

  printf '%s' "${name}"
}

duration_to_seconds() {
  local duration="$1"
  local number=""
  local suffix=""

  if [[ "${duration}" =~ ^([0-9]+)([smh]?)$ ]]; then
    number="${BASH_REMATCH[1]}"
    suffix="${BASH_REMATCH[2]}"

    case "${suffix}" in
      h)
        printf '%s' "$((number * 3600))"
        ;;
      m)
        printf '%s' "$((number * 60))"
        ;;
      *)
        printf '%s' "${number}"
        ;;
    esac
    return 0
  fi

  echo "Invalid duration: ${duration}. Use seconds or a simple s/m/h suffix, for example 120 or 2m." >&2
  exit 1
}

resolve_image_digest() {
  local image_uri="$1"
  local digest=""
  local repo_digest=""

  digest="$(
    gcloud artifacts docker images describe "${image_uri}" \
      --format='value(image_summary.digest)' 2>/dev/null || true
  )"

  if [[ -z "${digest}" ]]; then
    repo_digest="$(
      docker inspect --format='{{range .RepoDigests}}{{println .}}{{end}}' "${image_uri}" 2>/dev/null \
        | awk -F'@' 'NF == 2 {print $2; exit}' || true
    )"
    digest="${repo_digest}"
  fi

  if ! validate_image_digest "${digest}"; then
    echo "Could not resolve an immutable digest for ${image_uri}." >&2
    echo "Resolved value: ${digest:-<empty>}" >&2
    exit 1
  fi

  printf '%s' "${digest}"
}

image_ref_from_digest() {
  local registry_host="$1"
  local project_id="$2"
  local artifact_registry_repo="$3"
  local image_name="$4"
  local digest="$5"

  printf '%s/%s/%s/%s@%s' "${registry_host}" "${project_id}" "${artifact_registry_repo}" "${image_name}" "${digest}"
}

image_digest_from_ref() {
  local image_ref="$1"

  if [[ "${image_ref}" =~ @([^@]+)$ ]]; then
    printf '%s' "${BASH_REMATCH[1]}"
  fi
}

image_tag_from_ref() {
  local image_ref="$1"
  local without_digest="${image_ref%@*}"

  if [[ "${image_ref}" == *"@"* ]]; then
    return 0
  fi

  if [[ "${without_digest}" =~ :([^/:]+)$ ]]; then
    printf '%s' "${BASH_REMATCH[1]}"
  fi
}

selector_from_workload() {
  local namespace="$1"
  local workload_kind="$2"
  local workload_name="$3"

  kubectl -n "${namespace}" get "${workload_kind}" "${workload_name}" -o json \
    | jq -r '.spec.selector.matchLabels // {} | to_entries | map("\(.key)=\(.value)") | join(",")'
}

print_rollout_debug() {
  local namespace="$1"
  local workload_kind="$2"
  local workload_name="$3"
  local selector=""
  local pod_name=""

  echo "Rollout diagnostics for ${workload_kind}/${workload_name} in namespace ${namespace}:" >&2
  kubectl -n "${namespace}" get "${workload_kind}" "${workload_name}" -o wide >&2 || true
  kubectl -n "${namespace}" describe "${workload_kind}" "${workload_name}" >&2 || true

  selector="$(selector_from_workload "${namespace}" "${workload_kind}" "${workload_name}" 2>/dev/null || true)"

  if [[ -n "${selector}" ]]; then
    kubectl -n "${namespace}" get pods -l "${selector}" -o wide >&2 || true

    while IFS= read -r pod_name; do
      [[ -n "${pod_name}" ]] || continue
      kubectl -n "${namespace}" describe pod "${pod_name}" >&2 || true
      kubectl -n "${namespace}" logs "${pod_name}" --all-containers=true --tail=200 >&2 || true
      kubectl -n "${namespace}" logs "${pod_name}" --all-containers=true --previous --tail=200 >&2 || true
    done < <(
      kubectl -n "${namespace}" get pods -l "${selector}" -o json \
        | jq -r '.items | sort_by(.metadata.creationTimestamp) | .[].metadata.name'
    )
  else
    kubectl -n "${namespace}" get pods -o wide >&2 || true
  fi

  kubectl -n "${namespace}" get events --sort-by=.metadata.creationTimestamp >&2 || true
}

workload_exists() {
  local namespace="$1"
  local workload_kind="$2"
  local workload_name="$3"

  kubectl -n "${namespace}" get "${workload_kind}" "${workload_name}" >/dev/null 2>&1
}

current_workload_image() {
  local namespace="$1"
  local workload_kind="$2"
  local workload_name="$3"
  local container_name="$4"

  kubectl -n "${namespace}" get "${workload_kind}" "${workload_name}" -o json \
    | jq -r --arg container_name "${container_name}" \
      '.spec.template.spec.containers[] | select(.name == $container_name) | .image'
}

ready_pod_count_for_workload() {
  local namespace="$1"
  local workload_kind="$2"
  local workload_name="$3"
  local selector=""

  selector="$(selector_from_workload "${namespace}" "${workload_kind}" "${workload_name}" 2>/dev/null || true)"

  if [[ -z "${selector}" ]]; then
    printf '0'
    return 0
  fi

  kubectl -n "${namespace}" get pods -l "${selector}" -o json \
    | jq -r '
      [
        .items[]
        | select(((.status.containerStatuses // []) | length) > 0)
        | select((.status.containerStatuses // [] | map(.ready == true) | all))
      ]
      | length
    '
}

ensure_ready_before_deploy() {
  local namespace="$1"
  local workload_kind="$2"
  local workload_name="$3"
  local ready_count=""

  if ! workload_exists "${namespace}" "${workload_kind}" "${workload_name}"; then
    log "No existing ${workload_kind}/${workload_name} found in ${namespace}; treating this as an initial deploy"
    return 0
  fi

  ready_count="$(ready_pod_count_for_workload "${namespace}" "${workload_kind}" "${workload_name}")"

  if [[ "${ready_count}" != "0" ]]; then
    log "Current ${workload_kind}/${workload_name} has ${ready_count} ready pod(s)"
    return 0
  fi

  if is_true "${ALLOW_ZERO_READY_BEFORE_DEPLOY}"; then
    log "Continuing despite zero ready pods because ALLOW_ZERO_READY_BEFORE_DEPLOY=${ALLOW_ZERO_READY_BEFORE_DEPLOY}"
    return 0
  fi

  echo "Refusing to deploy: current ${workload_kind}/${workload_name} in ${namespace} has zero ready pods." >&2
  echo "Set ALLOW_ZERO_READY_BEFORE_DEPLOY=true only for explicit recovery or first-deploy cases." >&2
  print_rollout_debug "${namespace}" "${workload_kind}" "${workload_name}"
  exit 1
}

find_stale_blocking_pod() {
  local namespace="$1"
  local workload_kind="$2"
  local workload_name="$3"
  local workload_json=""
  local selector=""
  local pods_json=""
  local replicas=""
  local pod_count=""
  local pod_name=""
  local desired_images=""
  local current_images=""
  local pod_ready=""

  workload_json="$(kubectl -n "${namespace}" get "${workload_kind}" "${workload_name}" -o json 2>/dev/null)" || return 1
  replicas="$(jq -r '.spec.replicas // 1' <<<"${workload_json}")"

  if [[ "${replicas}" != "1" ]]; then
    return 1
  fi

  selector="$(jq -r '.spec.selector.matchLabels // {} | to_entries | map("\(.key)=\(.value)") | join(",")' <<<"${workload_json}")"

  if [[ -z "${selector}" ]]; then
    return 1
  fi

  pods_json="$(kubectl -n "${namespace}" get pods -l "${selector}" -o json 2>/dev/null)" || return 1
  pod_count="$(jq -r '.items | length' <<<"${pods_json}")"

  if [[ "${pod_count}" != "1" ]]; then
    return 1
  fi

  desired_images="$(jq -r '.spec.template.spec.containers | map("\(.name)=\(.image)") | join("\n")' <<<"${workload_json}")"
  current_images="$(jq -r '.items[0].spec.containers | map("\(.name)=\(.image)") | join("\n")' <<<"${pods_json}")"

  if [[ "${desired_images}" == "${current_images}" ]]; then
    return 1
  fi

  pod_ready="$(
    jq -r '
      if (.items[0].status.containerStatuses // [] | length) == 0
      then "false"
      else ((.items[0].status.containerStatuses | map(.ready) | all) | tostring)
      end
    ' <<<"${pods_json}"
  )"

  if [[ "${pod_ready}" == "true" ]]; then
    return 1
  fi

  pod_name="$(jq -r '.items[0].metadata.name' <<<"${pods_json}")"

  if [[ -z "${pod_name}" || "${pod_name}" == "null" ]]; then
    return 1
  fi

  printf '%s' "${pod_name}"
}

wait_for_rollout_with_recovery() {
  local namespace="$1"
  local workload_kind="$2"
  local workload_name="$3"
  local stale_pod=""

  if kubectl -n "${namespace}" rollout status "${workload_kind}/${workload_name}" --timeout "${ROLLOUT_TIMEOUT}"; then
    return 0
  fi

  if ! is_true "${ALLOW_SCALE_TO_ZERO_RECOVERY}"; then
    echo "Rollout failed. Refusing legacy scale-to-zero recovery during normal deploy." >&2
    echo "Set ALLOW_SCALE_TO_ZERO_RECOVERY=true only for explicit emergency recovery." >&2
    print_rollout_debug "${namespace}" "${workload_kind}" "${workload_name}"
    return 1
  fi

  stale_pod="$(find_stale_blocking_pod "${namespace}" "${workload_kind}" "${workload_name}" || true)"

  if [[ -n "${stale_pod}" ]]; then
    log "Scaling ${workload_kind}/${workload_name} down to 0 to recycle stale unhealthy pod ${stale_pod}"
    kubectl -n "${namespace}" scale "${workload_kind}/${workload_name}" --replicas=0
    kubectl -n "${namespace}" rollout status "${workload_kind}/${workload_name}" --timeout "${ROLLOUT_TIMEOUT}"
    log "Scaling ${workload_kind}/${workload_name} back to 1 and retrying rollout"
    kubectl -n "${namespace}" scale "${workload_kind}/${workload_name}" --replicas=1

    if kubectl -n "${namespace}" rollout status "${workload_kind}/${workload_name}" --timeout "${ROLLOUT_TIMEOUT}"; then
      return 0
    fi
  fi

  print_rollout_debug "${namespace}" "${workload_kind}" "${workload_name}"
  return 1
}

cleanup_smoke_workload() {
  if [[ -z "${SMOKE_WORKLOAD_NAME:-}" || -z "${SMOKE_NAMESPACE:-}" ]]; then
    return 0
  fi

  kubectl -n "${SMOKE_NAMESPACE}" delete pod "${SMOKE_WORKLOAD_NAME}" \
    --ignore-not-found=true --wait=true --timeout=60s >/dev/null 2>&1 || true
}

render_smoke_workload_manifest() {
  local namespace="$1"
  local smoke_workload_name="$2"
  local image_ref="$3"
  local runtime_service_account="$4"
  local runtime_contract_config_map="$5"
  local app_config_map_name="$6"
  local secret_provider_class_name="$7"
  local health_check_path="$8"
  local data_mount_path="$9"
  local runtime_secrets_mount_path="${10}"
  local openfga_image="${11}"
  local openfga_data_mount_path="${12}"
  local sqlite_pvc_subpath="${13}"
  local openfga_pvc_subpath="${14}"
  local app_config_env_from=""
  local active_deadline_seconds="$((SMOKE_TIMEOUT_SECONDS + 60))"
  local secret_volume_mount=""
  local secret_volume=""

  if [[ -n "${app_config_map_name}" && "${app_config_map_name}" != "null" ]]; then
    app_config_env_from="
        - configMapRef:
            name: ${app_config_map_name}"
  fi

  if [[ -n "${secret_provider_class_name}" && "${secret_provider_class_name}" != "null" ]]; then
    secret_volume_mount="
        - name: runtime-secrets
          mountPath: ${runtime_secrets_mount_path}
          readOnly: true"
    secret_volume="
    - name: runtime-secrets
      csi:
        driver: secrets-store-gke.csi.k8s.io
        readOnly: true
        volumeAttributes:
          secretProviderClass: ${secret_provider_class_name}"
  fi

  cat <<EOF
apiVersion: v1
kind: Pod
metadata:
  name: ${smoke_workload_name}
  namespace: ${namespace}
  labels:
    app.kubernetes.io/name: ${smoke_workload_name}
    app.kubernetes.io/managed-by: deploy-smoke
    internal-tools.wonderly.io/deploy-smoke: "true"
    internal-tools.wonderly.io/service: backup
spec:
  activeDeadlineSeconds: ${active_deadline_seconds}
  restartPolicy: Never
  serviceAccountName: ${runtime_service_account}
  terminationGracePeriodSeconds: 5
  initContainers:
    - name: prepare-data-dirs
      image: busybox:1.36
      command: ["/bin/sh", "-c"]
      args:
        - mkdir -p /mnt/pvc/${sqlite_pvc_subpath} /mnt/pvc/${openfga_pvc_subpath} && chmod -R a+rwX /mnt/pvc/${sqlite_pvc_subpath} /mnt/pvc/${openfga_pvc_subpath}
      volumeMounts:
        - name: data
          mountPath: /mnt/pvc
    - name: openfga-migrate
      image: ${openfga_image}
      args: ["migrate"]
      env:
        - name: OPENFGA_DATASTORE_ENGINE
          value: sqlite
        - name: OPENFGA_DATASTORE_URI
          value: file:${openfga_data_mount_path}/openfga.db
      volumeMounts:
        - name: data
          mountPath: ${openfga_data_mount_path}
          subPath: ${openfga_pvc_subpath}
  containers:
    - name: api
      image: ${image_ref}
      imagePullPolicy: Always
      ports:
        - name: http
          containerPort: 8080
      envFrom:
        - configMapRef:
            name: ${runtime_contract_config_map}${app_config_env_from}
      readinessProbe:
        httpGet:
          path: ${health_check_path}
          port: 8080
        initialDelaySeconds: 5
        periodSeconds: 5
        timeoutSeconds: 5
        failureThreshold: 24
      volumeMounts:
        - name: data
          mountPath: ${data_mount_path}
          subPath: ${sqlite_pvc_subpath}${secret_volume_mount}
    - name: openfga
      image: ${openfga_image}
      args: ["run"]
      ports:
        - name: openfga-http
          containerPort: 8090
        - name: openfga-grpc
          containerPort: 8091
      env:
        - name: OPENFGA_DATASTORE_ENGINE
          value: sqlite
        - name: OPENFGA_DATASTORE_URI
          value: file:${openfga_data_mount_path}/openfga.db
        - name: OPENFGA_LOG_FORMAT
          value: json
        - name: OPENFGA_HTTP_ADDR
          value: 0.0.0.0:8090
        - name: OPENFGA_GRPC_ADDR
          value: 0.0.0.0:8091
      volumeMounts:
        - name: data
          mountPath: ${openfga_data_mount_path}
          subPath: ${openfga_pvc_subpath}
  volumes:
    - name: data
      emptyDir: {}${secret_volume}
EOF
}

run_pre_promotion_smoke() {
  local namespace="$1"
  local workload_name="$2"
  local image_ref="$3"
  local runtime_service_account="$4"
  local runtime_contract_config_map="$5"
  local app_config_map_name="$6"
  local secret_provider_class_name="$7"
  local health_check_path="$8"
  local data_mount_path="$9"
  local runtime_secrets_mount_path="${10}"
  local openfga_image="${11}"
  local openfga_data_mount_path="${12}"
  local sqlite_pvc_subpath="${13}"
  local openfga_pvc_subpath="${14}"
  local smoke_workload_name=""

  if is_true "${SKIP_PRE_PROMOTION_SMOKE}"; then
    log "Skipping pre-promotion smoke gate because SKIP_PRE_PROMOTION_SMOKE=${SKIP_PRE_PROMOTION_SMOKE}"
    return 0
  fi

  if [[ -z "${namespace}" || "${namespace}" == "null" ]]; then
    echo "Cannot run pre-promotion smoke gate because namespace output is missing." >&2
    exit 1
  fi

  if [[ -z "${runtime_service_account}" || "${runtime_service_account}" == "null" ]]; then
    echo "Cannot run pre-promotion smoke gate because runtime service account output is missing." >&2
    exit 1
  fi

  if [[ -z "${runtime_contract_config_map}" || "${runtime_contract_config_map}" == "null" ]]; then
    echo "Cannot run pre-promotion smoke gate because runtime contract ConfigMap output is missing." >&2
    exit 1
  fi

  if [[ -z "${openfga_image}" || "${openfga_image}" == "null" ]]; then
    echo "Cannot run pre-promotion smoke gate because OpenFGA image output is missing." >&2
    exit 1
  fi

  smoke_workload_name="$(make_k8s_name "${workload_name}-deploy-smoke-${IMAGE_TAG}-${RANDOM}")"
  SMOKE_NAMESPACE="${namespace}"
  SMOKE_WORKLOAD_NAME="${smoke_workload_name}"

  log "Running pre-promotion smoke Pod ${SMOKE_WORKLOAD_NAME} with ${image_ref}"
  render_smoke_workload_manifest \
    "${namespace}" \
    "${SMOKE_WORKLOAD_NAME}" \
    "${image_ref}" \
    "${runtime_service_account}" \
    "${runtime_contract_config_map}" \
    "${app_config_map_name}" \
    "${secret_provider_class_name}" \
    "${health_check_path}" \
    "${data_mount_path}" \
    "${runtime_secrets_mount_path}" \
    "${openfga_image}" \
    "${openfga_data_mount_path}" \
    "${sqlite_pvc_subpath}" \
    "${openfga_pvc_subpath}" \
    | kubectl apply -f - >/dev/null

  if ! kubectl -n "${namespace}" wait --for=condition=Ready "pod/${SMOKE_WORKLOAD_NAME}" --timeout "${SMOKE_TIMEOUT}"; then
    echo "Pre-promotion smoke gate failed for ${image_ref}." >&2
    print_rollout_debug "${namespace}" "pod" "${SMOKE_WORKLOAD_NAME}"
    cleanup_smoke_workload
    exit 1
  fi

  log "Pre-promotion smoke gate passed for ${image_ref}"
  cleanup_smoke_workload
  SMOKE_NAMESPACE=""
  SMOKE_WORKLOAD_NAME=""
}

apply_image_with_tofu() {
  local image_name="$1"
  local image_tag="$2"
  local image_digest="$3"

  tofu -chdir="${INFRA_DIR}" apply -auto-approve \
    "${TOFU_APPLY_ARGS[@]}" \
    -var "image_name=${image_name}" \
    -var "image_tag=${image_tag}" \
    -var "image_digest=${image_digest}"
}

status_code_allowed() {
  local status_code="$1"
  local allowed_code=""

  for allowed_code in ${PUBLIC_HEALTH_ALLOWED_CODES}; do
    if [[ "${status_code}" == "${allowed_code}" ]]; then
      return 0
    fi
  done

  return 1
}

wait_for_public_health() {
  local domain_name="$1"
  local health_url="${PUBLIC_HEALTH_URL:-}"
  local deadline=""
  local status_code=""

  if is_true "${SKIP_PUBLIC_HEALTH_CHECK}"; then
    log "Skipping public health verification because SKIP_PUBLIC_HEALTH_CHECK=${SKIP_PUBLIC_HEALTH_CHECK}"
    return 0
  fi

  if [[ -z "${health_url}" && -n "${domain_name}" && "${domain_name}" != "null" ]]; then
    health_url="https://${domain_name}/healthy"
  fi

  if [[ -z "${health_url}" ]]; then
    log "Skipping public health verification because no domain_name output or PUBLIC_HEALTH_URL was provided"
    return 0
  fi

  require_cmd curl

  deadline="$(($(date '+%s') + PUBLIC_HEALTH_TIMEOUT_SECONDS))"
  log "Waiting for public health URL ${health_url}"

  while [[ "$(date '+%s')" -le "${deadline}" ]]; do
    status_code="$(curl -k -sS -o /dev/null -w '%{http_code}' --max-time 10 "${health_url}" 2>/dev/null || true)"

    if status_code_allowed "${status_code}"; then
      log "Public health URL returned accepted status ${status_code}"
      return 0
    fi

    sleep 5
  done

  echo "Public health verification failed for ${health_url}. Last status: ${status_code:-<none>}." >&2
  return 1
}

rollback_to_previous_image() {
  local reason="$1"
  local namespace="$2"
  local workload_kind="$3"
  local workload_name="$4"
  local container_name="$5"
  local previous_image_ref="$6"
  local previous_tag=""
  local previous_digest=""

  if [[ -z "${previous_image_ref}" || "${previous_image_ref}" == "null" ]]; then
    echo "Cannot roll back after ${reason}: previous image was not captured." >&2
    return 1
  fi

  log "Rolling back ${workload_kind}/${workload_name} to ${previous_image_ref} after ${reason}"
  kubectl -n "${namespace}" set image "${workload_kind}/${workload_name}" "${container_name}=${previous_image_ref}"

  if ! kubectl -n "${namespace}" rollout status "${workload_kind}/${workload_name}" --timeout "${ROLLOUT_TIMEOUT}"; then
    print_rollout_debug "${namespace}" "${workload_kind}" "${workload_name}"
    return 1
  fi

  previous_digest="$(image_digest_from_ref "${previous_image_ref}")"
  previous_tag="$(image_tag_from_ref "${previous_image_ref}")"

  if [[ -z "${previous_tag}" ]]; then
    previous_tag="rollback"
  fi

  log "Reconciling OpenTofu state with rollback image"
  if ! apply_image_with_tofu "${IMAGE_NAME}" "${previous_tag}" "${previous_digest}"; then
    echo "Rollback restored Kubernetes, but failed to reconcile OpenTofu state." >&2
    return 1
  fi
}

if [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  usage
  exit 0
fi

for cmd in docker gcloud jq kubectl tofu; do
  require_cmd "${cmd}"
done

ensure_gke_auth_plugin

INFRA_DIR="${INFRA_DIR:-infra/opentofu}"
ROLLOUT_TIMEOUT="${ROLLOUT_TIMEOUT:-5m}"
SMOKE_TIMEOUT="${SMOKE_TIMEOUT:-3m}"
SMOKE_TIMEOUT_SECONDS="$(duration_to_seconds "${SMOKE_TIMEOUT}")"
DOCKER_PLATFORM="${DOCKER_PLATFORM:-linux/amd64}"
PUBLISH_LATEST="${PUBLISH_LATEST:-false}"
SKIP_PRE_PROMOTION_SMOKE="${SKIP_PRE_PROMOTION_SMOKE:-false}"
ALLOW_ZERO_READY_BEFORE_DEPLOY="${ALLOW_ZERO_READY_BEFORE_DEPLOY:-false}"
ALLOW_SCALE_TO_ZERO_RECOVERY="${ALLOW_SCALE_TO_ZERO_RECOVERY:-false}"
SKIP_PUBLIC_HEALTH_CHECK="${SKIP_PUBLIC_HEALTH_CHECK:-false}"
PUBLIC_HEALTH_ALLOWED_CODES="${PUBLIC_HEALTH_ALLOWED_CODES:-200 204 301 302 401 403}"
PUBLIC_HEALTH_TIMEOUT="${PUBLIC_HEALTH_TIMEOUT:-2m}"
PUBLIC_HEALTH_TIMEOUT_SECONDS="$(duration_to_seconds "${PUBLIC_HEALTH_TIMEOUT}")"
CONTAINER_NAME="${CONTAINER_NAME:-api}"
SMOKE_NAMESPACE=""
SMOKE_WORKLOAD_NAME=""
TOFU_APPLY_ARGS=("$@")

if [[ ! -d "${INFRA_DIR}" ]]; then
  echo "Infra directory not found: ${INFRA_DIR}" >&2
  exit 1
fi

trap cleanup_smoke_workload EXIT

sync_tf_var_kubeconfig_path

if git rev-parse --git-dir >/dev/null 2>&1; then
  DEFAULT_IMAGE_TAG="$(git rev-parse --short HEAD)"
else
  DEFAULT_IMAGE_TAG="$(date '+%Y%m%d%H%M%S')"
fi

IMAGE_TAG="${IMAGE_TAG:-${DEFAULT_IMAGE_TAG}}"

if ! OUTPUT_JSON="$(tofu -chdir="${INFRA_DIR}" output -json 2>/dev/null)"; then
  OUTPUT_JSON='{}'
fi

PROJECT_ID="$(resolve_value GCP_PROJECT_ID project_id "GCP project ID")"
ARTIFACT_REGISTRY_LOCATION="$(resolve_value ARTIFACT_REGISTRY_LOCATION artifact_registry_location "Artifact Registry location")"
ARTIFACT_REGISTRY_REPO="$(resolve_value ARTIFACT_REGISTRY_REPO artifact_registry_repo "Artifact Registry repository")"
NAMESPACE="$(resolve_value APP_NAMESPACE namespace "app namespace")"
WORKLOAD_NAME="$(resolve_value WORKLOAD_NAME workload_name "workload name")"
WORKLOAD_KIND="statefulset"
SECRET_PROVIDER_CLASS_NAME="$(read_output secret_provider_class_name)"
RUNTIME_SERVICE_ACCOUNT="$(read_output runtime_service_account)"
RUNTIME_CONTRACT_CONFIG_MAP_NAME="$(read_output runtime_contract_config_map_name)"
APP_CONFIG_MAP_NAME="$(read_output app_config_map_name)"
DOMAIN_NAME="$(read_output domain_name)"
HEALTH_CHECK_PATH="${HEALTH_CHECK_PATH:-$(read_output health_check_path)}"
DATA_MOUNT_PATH="${DATA_MOUNT_PATH:-$(read_output data_mount_path)}"
RUNTIME_SECRETS_MOUNT_PATH="${RUNTIME_SECRETS_MOUNT_PATH:-$(read_output runtime_secrets_mount_path)}"
OPENFGA_IMAGE="${OPENFGA_IMAGE:-$(read_output openfga_image)}"
OPENFGA_DATA_MOUNT_PATH="${OPENFGA_DATA_MOUNT_PATH:-$(read_output openfga_data_mount_path)}"
SQLITE_PVC_SUBPATH="${SQLITE_PVC_SUBPATH:-$(read_output sqlite_pvc_subpath)}"
OPENFGA_PVC_SUBPATH="${OPENFGA_PVC_SUBPATH:-$(read_output openfga_pvc_subpath)}"

if [[ -z "${HEALTH_CHECK_PATH}" || "${HEALTH_CHECK_PATH}" == "null" ]]; then
  HEALTH_CHECK_PATH="/healthy"
fi

if [[ -z "${DATA_MOUNT_PATH}" || "${DATA_MOUNT_PATH}" == "null" ]]; then
  DATA_MOUNT_PATH="/app/data"
fi

if [[ -z "${RUNTIME_SECRETS_MOUNT_PATH}" || "${RUNTIME_SECRETS_MOUNT_PATH}" == "null" ]]; then
  RUNTIME_SECRETS_MOUNT_PATH="/var/run/secrets/app"
fi

if [[ -z "${OPENFGA_DATA_MOUNT_PATH}" || "${OPENFGA_DATA_MOUNT_PATH}" == "null" ]]; then
  OPENFGA_DATA_MOUNT_PATH="/home/nonroot"
fi

if [[ -z "${SQLITE_PVC_SUBPATH}" || "${SQLITE_PVC_SUBPATH}" == "null" ]]; then
  SQLITE_PVC_SUBPATH="freetool-db"
fi

if [[ -z "${OPENFGA_PVC_SUBPATH}" || "${OPENFGA_PVC_SUBPATH}" == "null" ]]; then
  OPENFGA_PVC_SUBPATH="openfga"
fi

IMAGE_NAME="${IMAGE_NAME:-$(read_output image_name)}"
if [[ -z "${IMAGE_NAME}" || "${IMAGE_NAME}" == "null" ]]; then
  IMAGE_NAME="freetool-api"
fi

REGISTRY_HOST="${ARTIFACT_REGISTRY_LOCATION}-docker.pkg.dev"
IMAGE_URI="${REGISTRY_HOST}/${PROJECT_ID}/${ARTIFACT_REGISTRY_REPO}/${IMAGE_NAME}:${IMAGE_TAG}"
LATEST_IMAGE_URI="${REGISTRY_HOST}/${PROJECT_ID}/${ARTIFACT_REGISTRY_REPO}/${IMAGE_NAME}:latest"

if [[ -n "${SECRET_PROVIDER_CLASS_NAME}" && "${SECRET_PROVIDER_CLASS_NAME}" != "null" ]]; then
  log "Validating runtime secrets from ${SECRET_PROVIDER_CLASS_NAME} in namespace ${NAMESPACE}"
  validate_runtime_secret_versions "${PROJECT_ID}" "${NAMESPACE}" "${SECRET_PROVIDER_CLASS_NAME}"
else
  log "Skipping runtime secret validation because SecretProviderClass output is not available"
fi

ensure_ready_before_deploy "${NAMESPACE}" "${WORKLOAD_KIND}" "${WORKLOAD_NAME}"

log "Building image ${IMAGE_URI}"
gcloud auth configure-docker "${REGISTRY_HOST}" --quiet
docker build --platform "${DOCKER_PLATFORM}" -f src/Freetool.Api/Dockerfile -t "${IMAGE_URI}" .
docker push "${IMAGE_URI}"

if [[ "${PUBLISH_LATEST}" == "true" && "${IMAGE_TAG}" != "latest" ]]; then
  log "Publishing latest tag ${LATEST_IMAGE_URI}"
  docker tag "${IMAGE_URI}" "${LATEST_IMAGE_URI}"
  docker push "${LATEST_IMAGE_URI}"
fi

if [[ -z "${IMAGE_DIGEST:-}" ]]; then
  IMAGE_DIGEST="$(resolve_image_digest "${IMAGE_URI}")"
elif ! validate_image_digest "${IMAGE_DIGEST}"; then
  echo "IMAGE_DIGEST must be a sha256 digest, got: ${IMAGE_DIGEST}" >&2
  exit 1
fi

IMAGE_DIGEST_URI="$(image_ref_from_digest "${REGISTRY_HOST}" "${PROJECT_ID}" "${ARTIFACT_REGISTRY_REPO}" "${IMAGE_NAME}" "${IMAGE_DIGEST}")"

run_pre_promotion_smoke \
  "${NAMESPACE}" \
  "${WORKLOAD_NAME}" \
  "${IMAGE_DIGEST_URI}" \
  "${RUNTIME_SERVICE_ACCOUNT}" \
  "${RUNTIME_CONTRACT_CONFIG_MAP_NAME}" \
  "${APP_CONFIG_MAP_NAME}" \
  "${SECRET_PROVIDER_CLASS_NAME}" \
  "${HEALTH_CHECK_PATH}" \
  "${DATA_MOUNT_PATH}" \
  "${RUNTIME_SECRETS_MOUNT_PATH}" \
  "${OPENFGA_IMAGE}" \
  "${OPENFGA_DATA_MOUNT_PATH}" \
  "${SQLITE_PVC_SUBPATH}" \
  "${OPENFGA_PVC_SUBPATH}"

PREVIOUS_IMAGE_REF=""
if workload_exists "${NAMESPACE}" "${WORKLOAD_KIND}" "${WORKLOAD_NAME}"; then
  PREVIOUS_IMAGE_REF="$(current_workload_image "${NAMESPACE}" "${WORKLOAD_KIND}" "${WORKLOAD_NAME}" "${CONTAINER_NAME}")"
  log "Current production image is ${PREVIOUS_IMAGE_REF}"
fi

log "Applying OpenTofu with image_tag=${IMAGE_TAG} image_digest=${IMAGE_DIGEST}"
if ! apply_image_with_tofu "${IMAGE_NAME}" "${IMAGE_TAG}" "${IMAGE_DIGEST}"; then
  rollback_to_previous_image "OpenTofu apply failure" "${NAMESPACE}" "${WORKLOAD_KIND}" "${WORKLOAD_NAME}" "${CONTAINER_NAME}" "${PREVIOUS_IMAGE_REF}" || true
  exit 1
fi

OUTPUT_JSON="$(tofu -chdir="${INFRA_DIR}" output -json)"
DEPLOYED_IMAGE_REF="$(read_output image_ref)"
DOMAIN_NAME="$(read_output domain_name)"

log "Waiting for rollout in namespace ${NAMESPACE}"
if ! wait_for_rollout_with_recovery "${NAMESPACE}" "${WORKLOAD_KIND}" "${WORKLOAD_NAME}"; then
  rollback_to_previous_image "rollout failure" "${NAMESPACE}" "${WORKLOAD_KIND}" "${WORKLOAD_NAME}" "${CONTAINER_NAME}" "${PREVIOUS_IMAGE_REF}" || true
  exit 1
fi

if [[ "$(ready_pod_count_for_workload "${NAMESPACE}" "${WORKLOAD_KIND}" "${WORKLOAD_NAME}")" == "0" ]]; then
  echo "Post-deploy verification failed: ${WORKLOAD_KIND}/${WORKLOAD_NAME} has zero ready pods." >&2
  rollback_to_previous_image "zero ready pods after deploy" "${NAMESPACE}" "${WORKLOAD_KIND}" "${WORKLOAD_NAME}" "${CONTAINER_NAME}" "${PREVIOUS_IMAGE_REF}" || true
  exit 1
fi

if ! wait_for_public_health "${DOMAIN_NAME}"; then
  rollback_to_previous_image "public health verification failure" "${NAMESPACE}" "${WORKLOAD_KIND}" "${WORKLOAD_NAME}" "${CONTAINER_NAME}" "${PREVIOUS_IMAGE_REF}" || true
  exit 1
fi

cat <<EOF
Deployment finished.

namespace=${NAMESPACE}
workload=${WORKLOAD_NAME}
image=${DEPLOYED_IMAGE_REF}
image_digest=${IMAGE_DIGEST}
domain=${DOMAIN_NAME}
EOF
