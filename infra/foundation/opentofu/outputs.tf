output "project_id" {
  value       = local.project_id
  description = "GCP project ID used by the foundation stack."
}

output "project_number" {
  value       = data.google_project.current.number
  description = "GCP project number used by the foundation stack."
}

output "terraform_state_bucket_name" {
  value       = google_storage_bucket.terraform_state.name
  description = "GCS bucket name used for this stack's OpenTofu remote state."
}

output "github_repository_name" {
  value       = local.github_repository_name
  description = "GitHub repository name allowed to deploy."
}

output "github_workload_identity_pool_name" {
  value       = "projects/${data.google_project.current.number}/locations/global/workloadIdentityPools/${var.github_workload_identity_pool_id}"
  description = "Full shared Workload Identity Pool resource name used by GitHub Actions."
}

output "github_workload_identity_provider_name" {
  value       = google_iam_workload_identity_pool_provider.github_actions.name
  description = "Full Workload Identity Provider resource name for GitHub Actions."
}

output "github_deploy_service_account_email" {
  value       = google_service_account.deploy.email
  description = "Service account email used by GitHub Actions deployments."
}

output "app_catalog_deployer_subject" {
  value       = "serviceAccount:${google_service_account.deploy.email}"
  description = "Value to copy into deployer_subjects in ../internal-tools-infra/catalog/apps/freetool.yaml."
}
