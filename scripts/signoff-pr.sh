#!/usr/bin/env bash
if (( BASH_VERSINFO[0] < 4 )); then
  printf 'This script requires Bash 4.0 or newer; found %s.\n' "${BASH_VERSION:-unknown}" >&2
  exit 1
fi

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/signoff-pr.XXXXXX")"
trap 'rm -rf "$TMP_ROOT"' EXIT

BACKEND_COVERAGE_THRESHOLD="${SIGNOFF_BACKEND_COVERAGE_THRESHOLD:-90}"
DOCKER_COMPOSE_CMD=()

run_step() {
  local label="$1"
  shift

  echo
  echo "==> $label"
  "$@"
}

require_cmd() {
  local cmd="$1"
  local message="$2"

  if command -v "$cmd" >/dev/null 2>&1; then
    return
  fi

  echo "$message"
  exit 1
}

ensure_ruby_available() {
  require_cmd ruby "YAML validation requires 'ruby' in PATH."
}

ensure_tofu_available() {
  require_cmd tofu "Infrastructure validation requires 'tofu' in PATH."
}

ensure_python_available() {
  require_cmd python3 "Python script validation requires 'python3' in PATH."
}

capture_tracked_status() {
  git status --porcelain=v1 --untracked-files=no
}

ensure_tracked_status_unchanged() {
  local before="$1"
  local after
  after="$(capture_tracked_status)"

  if [ "$before" = "$after" ]; then
    return
  fi

  echo "Validation commands changed tracked files. Fix or commit those changes before signoff."
  git diff --stat
  exit 1
}

resolve_solution_file() {
  local solutions=()
  local solution

  mapfile -t solutions < <(find "$ROOT_DIR" -maxdepth 1 -name '*.sln' -print | sed 's#^.*/##' | sort)

  if [ "${#solutions[@]}" -eq 0 ]; then
    echo "No solution file found at the repository root."
    exit 1
  fi

  for solution in "${solutions[@]}"; do
    if [[ "$solution" != *.Production.sln ]]; then
      echo "$solution"
      return
    fi
  done

  echo "${solutions[0]}"
}

resolve_docker_compose_cmd() {
  if [ "${#DOCKER_COMPOSE_CMD[@]}" -gt 0 ]; then
    return
  fi

  if command -v docker >/dev/null 2>&1 && docker compose version >/dev/null 2>&1; then
    DOCKER_COMPOSE_CMD=(docker compose)
    return
  fi

  if command -v docker-compose >/dev/null 2>&1; then
    DOCKER_COMPOSE_CMD=(docker-compose)
    return
  fi

  echo "Docker Compose is required to start the local OpenFGA test dependency."
  echo "Install Docker Desktop (or docker-compose) and retry."
  exit 1
}

repo_uses_openfga() {
  grep -qi 'openfga' docker-compose.yml docker-compose.yaml docker-compose.dev.yml 2>/dev/null
}

ensure_openfga_ready() {
  local readiness_url="http://127.0.0.1:8090/stores"
  local max_attempts=30
  local attempt=1

  if ! repo_uses_openfga; then
    return
  fi

  require_cmd curl "'curl' is required to verify OpenFGA readiness."

  if curl -fsS "$readiness_url" >/dev/null 2>&1; then
    echo "OpenFGA is already reachable at $readiness_url"
    return
  fi

  resolve_docker_compose_cmd
  "${DOCKER_COMPOSE_CMD[@]}" up -d openfga

  until curl -fsS "$readiness_url" >/dev/null 2>&1; do
    if [ "$attempt" -ge "$max_attempts" ]; then
      echo "OpenFGA did not become ready at $readiness_url after $((max_attempts * 2)) seconds."
      "${DOCKER_COMPOSE_CMD[@]}" ps openfga || true
      exit 1
    fi

    sleep 2
    attempt=$((attempt + 1))
  done

  echo "OpenFGA is ready at $readiness_url"
}

validate_yaml_files() {
  if [ "${#CHANGED_YAML_FILES[@]}" -eq 0 ]; then
    echo "No non-workflow YAML files changed; skipping YAML validation."
    return
  fi

  ruby -e '
    require "psych"
    ARGV.each do |path|
      Psych.parse_stream(File.read(path))
      puts "Validated #{path}"
    end
  ' "${CHANGED_YAML_FILES[@]}"
}

validate_shell_scripts() {
  if [ "${#CHANGED_SHELL_FILES[@]}" -eq 0 ]; then
    echo "No shell scripts changed; skipping shell validation."
    return
  fi

  bash -n "${CHANGED_SHELL_FILES[@]}"
}

validate_python_scripts() {
  if [ "${#CHANGED_PYTHON_FILES[@]}" -eq 0 ]; then
    echo "No Python scripts changed; skipping Python validation."
    return
  fi

  PYTHONPYCACHEPREFIX="$TMP_ROOT/python-pycache" python3 -m py_compile "${CHANGED_PYTHON_FILES[@]}"
}
validate_hosted_service_registrations() {
  if [ ! -f "$ROOT_DIR/scripts/check_hosted_service_registrations.py" ]; then
    echo "No hosted-service registration checker found; skipping DI registration validation."
    return
  fi

  local scan_roots=()
  [ -d "$ROOT_DIR/src" ] && scan_roots+=(src)
  [ -d "$ROOT_DIR/backend" ] && scan_roots+=(backend)

  if [ "${#scan_roots[@]}" -eq 0 ]; then
    echo "No backend source roots found; skipping hosted-service DI registration validation."
    return
  fi

  PYTHONPYCACHEPREFIX="$TMP_ROOT/python-pycache" python3 scripts/check_hosted_service_registrations.py "${scan_roots[@]}"
}


validate_tofu_root() {
  local root="$1"
  local slug
  slug="$(tr '/.' '__' <<<"$root")"
  local data_dir="$TMP_ROOT/tofu-$slug"

  mkdir -p "$data_dir"

  TF_DATA_DIR="$data_dir" tofu -chdir="$root" init -backend=false -input=false -lockfile=readonly

  TF_DATA_DIR="$data_dir" tofu -chdir="$root" validate
}

validate_backend_changed_coverage() {
  local results_dir="$1"
  local reports=()
  local args=(backend --threshold "$BACKEND_COVERAGE_THRESHOLD" --diff-base "$DIFF_BASE_REF")

  mapfile -t reports < <(find "$results_dir" -name 'coverage.cobertura.xml' -print | sort)

  if [ "${#reports[@]}" -eq 0 ]; then
    echo "Backend coverage reports were not produced."
    exit 1
  fi

  for report in "${reports[@]}"; do
    args+=(--report "$report")
  done

  for file in "${CHANGED_FILES[@]}"; do
    args+=(--changed-file "$file")
  done

  PYTHONPYCACHEPREFIX="$TMP_ROOT/python-pycache" python3 scripts/check_changed_coverage.py "${args[@]}"
}

ensure_not_default_branch() {
  local current_branch
  current_branch="$(git branch --show-current)"

  if [ "$current_branch" = "$DEFAULT_BRANCH" ]; then
    echo "Refusing to run on '$DEFAULT_BRANCH'. Create a feature branch and retry."
    exit 1
  fi
}

pick_remote() {
  if git remote get-url origin >/dev/null 2>&1; then
    echo "origin"
    return
  fi

  git remote | head -n 1
}

resolve_default_branch() {
  local remote remote_head
  remote="$(pick_remote)"

  if [ -n "$remote" ]; then
    remote_head="$(git symbolic-ref --quiet --short "refs/remotes/$remote/HEAD" 2>/dev/null || true)"
    if [ -n "$remote_head" ]; then
      echo "${remote_head#"$remote/"}"
      return
    fi
  fi

  if git show-ref --verify --quiet refs/heads/main; then
    echo "main"
    return
  fi

  if git show-ref --verify --quiet refs/heads/master; then
    echo "master"
    return
  fi

  echo "Could not determine the repository default branch."
  echo "Set the remote HEAD, or create a local 'main' or 'master' branch, then retry."
  exit 1
}

resolve_diff_base_ref() {
  local remote branch
  remote="$(pick_remote)"
  branch="$DEFAULT_BRANCH"

  if [ -n "$remote" ] && git show-ref --verify --quiet "refs/remotes/$remote/$branch"; then
    echo "$remote/$branch"
    return
  fi

  if git show-ref --verify --quiet "refs/heads/$branch"; then
    echo "$branch"
    return
  fi

  echo "Could not find a diff baseline for '$branch'."
  echo "Fetch '$remote/$branch' or create a local '$branch', then retry."
  exit 1
}

ensure_upstream_tracking() {
  local branch remote expected_upstream current_upstream
  branch="$(git branch --show-current)"
  remote="$(pick_remote)"

  if [ -z "$remote" ]; then
    echo "No git remotes found. Add a remote and retry."
    exit 1
  fi

  expected_upstream="$remote/$branch"
  current_upstream="$(git rev-parse --abbrev-ref --symbolic-full-name "@{upstream}" 2>/dev/null || true)"

  if [ "$current_upstream" != "$expected_upstream" ]; then
    echo "Configuring '$branch' to track '$expected_upstream'."
    git push -u "$remote" "$branch"
    return
  fi

  if git ls-remote --exit-code --heads "$remote" "$branch" >/dev/null 2>&1; then
    return
  fi

  echo "Remote branch '$expected_upstream' does not exist yet; pushing it now."
  git push -u "$remote" "$branch"
}

ensure_pull_request() {
  local branch existing_pr_url
  branch="$(git branch --show-current)"

  existing_pr_url="$(gh pr list \
    --head "$branch" \
    --base "$DEFAULT_BRANCH" \
    --state open \
    --json url \
    --jq '.[0].url' 2>/dev/null || true)"

  if [ -n "$existing_pr_url" ] && [ "$existing_pr_url" != "null" ]; then
    echo "Found existing pull request: $existing_pr_url"
    return
  fi

  echo "No pull request found for '$branch'; creating one against '$DEFAULT_BRANCH'."
  gh pr create --head "$branch" --base "$DEFAULT_BRANCH" --fill
}

validate_pull_request_title() {
  local branch title pattern
  branch="$(git branch --show-current)"
  title="$(gh pr view "$branch" --json title --jq '.title' 2>/dev/null || true)"

  if [ -z "$title" ] || [ "$title" = "null" ]; then
    echo "Could not resolve current pull request title for branch '$branch'." >&2
    exit 1
  fi

  pattern='^(feat|fix|docs|style|refactor|perf|test|build|ci|chore|revert)(\([a-z0-9][a-z0-9._/-]*\))?!?: [A-Z].*$'

  if [[ "$title" =~ $pattern ]]; then
    return
  fi

  cat >&2 <<MSG
Pull request title must follow Conventional Commits:
  <type>[optional scope][!]: <Description>

Allowed types:
  feat, fix, docs, style, refactor, perf, test, build, ci, chore, revert

Examples:
  feat: Add customer export
  fix(auth): Refresh expired tokens
  chore(deps)!: Upgrade runtime

Current title:
  $title

Rename the PR and rerun ./scripts/signoff-pr.sh.
MSG
  exit 1
}

export_pi_pr_telemetry() {
  if [ ! -f "$ROOT_DIR/scripts/pi-pr-telemetry.mjs" ]; then
    echo "Pi PR telemetry exporter not found; skipping telemetry summary."
    return
  fi

  if ! command -v node >/dev/null 2>&1; then
    echo "Node.js not found; skipping Pi PR telemetry summary."
    return
  fi

  local base_ref="${1:-${DIFF_BASE_REF:-${BASE_REF:-}}}"

  if ! node "$ROOT_DIR/scripts/pi-pr-telemetry.mjs" summarize \
    --repo "$ROOT_DIR" \
    --branch "$(git branch --show-current)" \
    --base "$base_ref" \
    --head "$(git rev-parse HEAD)" \
    --cutoff "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" \
    --out-json "$ROOT_DIR/.pi/pr-telemetry-summary.json" \
    --out-md "$ROOT_DIR/.pi/pr-telemetry-summary.md"; then
    echo "Pi PR telemetry summary failed; continuing without telemetry comment."
    return
  fi

  if ! publish_pi_pr_telemetry_comment "$ROOT_DIR/.pi/pr-telemetry-summary.md"; then
    echo "Pi PR telemetry comment failed; continuing with signoff."
  fi
}

publish_pi_pr_telemetry_comment() {
  local summary_path="$1"
  local marker='<!-- pi-pr-telemetry -->'
  local branch pr_number repo_name comment_body existing_comment_id

  if [ ! -s "$summary_path" ]; then
    echo "Pi PR telemetry summary was not produced; skipping PR comment."
    return
  fi

  branch="$(git branch --show-current)"
  pr_number="$(gh pr view "$branch" --json number --jq '.number' 2>/dev/null || true)"
  if [ -z "$pr_number" ] || [ "$pr_number" = "null" ]; then
    echo "Could not resolve current pull request number; skipping Pi telemetry PR comment."
    return
  fi

  repo_name="$(gh repo view --json nameWithOwner --jq '.nameWithOwner' 2>/dev/null || true)"
  if [ -z "$repo_name" ] || [ "$repo_name" = "null" ]; then
    echo "Could not resolve GitHub repository; skipping Pi telemetry PR comment."
    return
  fi

  comment_body="$(printf '%s\n\n%s\n' "$marker" "$(cat "$summary_path")")"

  existing_comment_id="$(gh api --paginate "repos/$repo_name/issues/$pr_number/comments" \
    --jq ".[] | select(.body | contains(\"$marker\")) | .id" 2>/dev/null \
    | tail -n 1 || true)"

  if [ -n "$existing_comment_id" ]; then
    if ! gh api --method PATCH "repos/$repo_name/issues/comments/$existing_comment_id" -f body="$comment_body" >/dev/null; then
      echo "Could not update Pi PR telemetry comment on #$pr_number."
      return
    fi
    echo "Updated Pi PR telemetry comment on #$pr_number."
    return
  fi

  if ! gh api --method POST "repos/$repo_name/issues/$pr_number/comments" -f body="$comment_body" >/dev/null; then
    echo "Could not create Pi PR telemetry comment on #$pr_number."
    return
  fi
  echo "Created Pi PR telemetry comment on #$pr_number."
}

DEFAULT_BRANCH="$(resolve_default_branch)"
DIFF_BASE_REF="$(resolve_diff_base_ref)"
SOLUTION_FILE="$(resolve_solution_file)"

ensure_not_default_branch

mapfile -t CHANGED_FILES < <(
  {
    git diff --name-only "$DIFF_BASE_REF"...HEAD
    git diff --name-only
    git diff --cached --name-only
  } | awk 'NF' | sort -u
)
CHANGED_FILES_TEXT="$(printf '%s\n' "${CHANGED_FILES[@]}")"

RUN_BACKEND=false
RUN_INFRA=false
RUN_WORKFLOW_VALIDATION=false
RUN_YAML_VALIDATION=false
RUN_SHELL_VALIDATION=false
RUN_PYTHON_VALIDATION=false

CHANGED_WORKFLOW_FILES=()
mapfile -t CHANGED_WORKFLOW_FILES < <(
  printf '%s\n' "${CHANGED_FILES[@]}" \
    | grep -E '^\.github/workflows/[^[:space:]]+\.ya?ml$' \
    || true
)

CHANGED_YAML_FILES=()
mapfile -t CHANGED_YAML_FILES < <(
  printf '%s\n' "${CHANGED_FILES[@]}" \
    | grep -E '^[^[:space:]]+\.ya?ml$' \
    | grep -Ev '^\.github/workflows/' \
    || true
)

CHANGED_SHELL_FILES=()
mapfile -t CHANGED_SHELL_FILES < <(
  printf '%s\n' "${CHANGED_FILES[@]}" | grep -E '^[^[:space:]]+\.sh$' || true
)

CHANGED_PYTHON_FILES=()
mapfile -t CHANGED_PYTHON_FILES < <(
  printf '%s\n' "${CHANGED_FILES[@]}" | grep -E '^[^[:space:]]+\.py$' || true
)

if grep -qE '^(src/|scripts/signoff-pr\.sh$|scripts/check_changed_coverage\.py$|scripts/check_hosted_service_registrations\.py$|[^/]+\.sln$|Directory\.Build\.props$|\.editorconfig$|\.config/dotnet-tools\.json$)' <<<"$CHANGED_FILES_TEXT"; then
  RUN_BACKEND=true
fi


if grep -qE '^infra/' <<<"$CHANGED_FILES_TEXT"; then
  RUN_INFRA=true
fi

if [ "${#CHANGED_WORKFLOW_FILES[@]}" -gt 0 ]; then
  RUN_WORKFLOW_VALIDATION=true
fi

if [ "${#CHANGED_YAML_FILES[@]}" -gt 0 ]; then
  RUN_YAML_VALIDATION=true
fi

if [ "${#CHANGED_SHELL_FILES[@]}" -gt 0 ]; then
  RUN_SHELL_VALIDATION=true
fi

if [ "${#CHANGED_PYTHON_FILES[@]}" -gt 0 ]; then
  RUN_PYTHON_VALIDATION=true
fi

VALIDATION_TRACKED_STATUS="$(capture_tracked_status)"

run_step "Checking git diff formatting" git diff --check

if [ "$RUN_BACKEND" = true ]; then
  ensure_python_available
  run_step "Restoring local dotnet tools" dotnet tool restore
  run_step "Running formatter check" dotnet tool run fantomas --check .
  run_step "Checking hosted-service DI registrations" validate_hosted_service_registrations
  run_step "Building solution (Release)" dotnet build "$SOLUTION_FILE" -c Release

  if repo_uses_openfga; then
    run_step "Ensuring OpenFGA is running for backend integration tests" ensure_openfga_ready
  fi

  BACKEND_COVERAGE_RESULTS_DIR="$TMP_ROOT/backend-coverage"
  run_step "Running backend tests with coverage (Release)" dotnet test "$SOLUTION_FILE" -c Release --no-build --collect:"XPlat Code Coverage" --results-directory "$BACKEND_COVERAGE_RESULTS_DIR"
  run_step "Checking backend changed-file coverage (${BACKEND_COVERAGE_THRESHOLD}% minimum)" validate_backend_changed_coverage "$BACKEND_COVERAGE_RESULTS_DIR"
else
  echo "No backend-related files changed; skipping backend verification steps."
fi

if [ "$RUN_INFRA" = true ]; then
  ensure_tofu_available
  run_step "Checking OpenTofu formatting" tofu fmt -recursive -check

  if [ -d infra/opentofu ]; then
    run_step "Validating infra/opentofu" validate_tofu_root infra/opentofu
  fi

  if [ -d infra/foundation/opentofu ]; then
    run_step "Validating infra/foundation/opentofu" validate_tofu_root infra/foundation/opentofu
  fi
else
  echo "No changes under infra/; skipping OpenTofu validation."
fi

if [ "$RUN_WORKFLOW_VALIDATION" = true ]; then
  require_cmd actionlint $'actionlint is required to validate changed GitHub Actions workflows.\nInstall: https://github.com/rhysd/actionlint'
  run_step "Validating changed GitHub Actions workflows" actionlint "${CHANGED_WORKFLOW_FILES[@]}"
else
  echo "No workflow files changed; skipping workflow validation."
fi

if [ "$RUN_YAML_VALIDATION" = true ]; then
  ensure_ruby_available
  run_step "Validating changed YAML files" validate_yaml_files
else
  echo "No non-workflow YAML files changed; skipping YAML validation."
fi

if [ "$RUN_SHELL_VALIDATION" = true ]; then
  run_step "Validating changed shell scripts" validate_shell_scripts
else
  echo "No shell scripts changed; skipping shell validation."
fi

if [ "$RUN_PYTHON_VALIDATION" = true ]; then
  ensure_python_available
  run_step "Validating changed Python scripts" validate_python_scripts
else
  echo "No Python scripts changed; skipping Python validation."
fi

echo "No generated frontend API types remain; skipping frontend contract validation."

if ! command -v gh >/dev/null 2>&1; then
  echo "GitHub CLI ('gh') is required but was not found in PATH."
  echo "Install: https://cli.github.com/"
  exit 1
fi

if ! gh help signoff >/dev/null 2>&1; then
  cat <<'MSG'
The 'gh signoff' command is not available.
Install the extension and retry:
  gh extension install basecamp/gh-signoff
MSG
  exit 1
fi

ensure_tracked_status_unchanged "$VALIDATION_TRACKED_STATUS"
ensure_upstream_tracking
ensure_pull_request
validate_pull_request_title
run_step "Exporting Pi PR telemetry summary" export_pi_pr_telemetry

echo "Signing off PR with gh-signoff..."
if [ "$#" -gt 0 ]; then
  echo "Ignoring unexpected arguments to signoff-pr.sh: $*"
fi

gh signoff
