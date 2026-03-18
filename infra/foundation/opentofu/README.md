# Foundation OpenTofu

This stack bootstraps the repo-owned Google Cloud identity used by GitHub Actions deployments.

It creates:

- one GCS bucket for the foundation stack's own remote state
- one repo-specific GitHub Actions Workload Identity Provider attached to the shared pool
- one deploy service account for this repository

It reuses the existing shared Workload Identity Pool `github-actions`; it does not try to create a new project-global pool per repo.

It does **not** create the app-owned state bucket used by `infra/opentofu`. That bucket comes from `../internal-tools-infra/platform/apps` and must still be copied from the app contract into GitHub Actions as `TOFU_BACKEND_BUCKET` with `TOFU_BACKEND_PREFIX=infra/opentofu`.

## Usage

First-time bootstrap must start from local state because this stack creates its own backend bucket.

1. Copy `terraform.tfvars.example` to `terraform.tfvars`.
2. Copy `backend.hcl.example` to `backend.hcl`.
3. Create a scratch copy of this directory without the `backend "gcs"` block and apply there.
4. Copy the resulting `terraform.tfstate` back into this directory.
5. Run `tofu init -migrate-state -force-copy -input=false -backend-config=backend.hcl`.

After that migration, normal commands can run directly from `infra/foundation/opentofu`.

## Outputs

Use these outputs to wire the deploy workflow:

- `project_id` -> `GCP_PROJECT_ID`
- `github_workload_identity_provider_name` -> `GCP_WORKLOAD_IDENTITY_PROVIDER`
- `github_deploy_service_account_email` -> `GCP_DEPLOY_SERVICE_ACCOUNT`
- `app_catalog_deployer_subject` -> copy into `deployer_subjects` in `../internal-tools-infra/catalog/apps/freetool.yaml`

The app deploy workflow still needs cluster and app-contract values:

- `GKE_CLUSTER_NAME`
- `GKE_CLUSTER_LOCATION`
- `TOFU_BACKEND_BUCKET`
- `TOFU_BACKEND_PREFIX`
- `TOFU_TFVARS_BASE64`
