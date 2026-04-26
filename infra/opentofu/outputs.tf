output "namespace" {
  value       = var.platform_contract.namespace
  description = "Platform-created namespace where the workload is deployed."
}

output "domain_name" {
  value       = var.platform_contract.domain_name
  description = "Public domain name allocated by internal-tools-infra."
}

output "workload_name" {
  value       = kubernetes_stateful_set_v1.app.metadata[0].name
  description = "Name of the app-owned StatefulSet."
}

output "project_id" {
  value       = var.project_id
  description = "GCP project ID that owns the shared app Artifact Registry repositories."
}

output "artifact_registry_location" {
  value       = var.artifact_registry_location
  description = "Artifact Registry location used by the app deployment."
}

output "artifact_registry_repo" {
  value       = var.platform_contract.artifact_registry_repo
  description = "Per-app Artifact Registry repository ID from the platform contract."
}

output "image_name" {
  value       = var.image_name
  description = "Image name deployed inside the per-app Artifact Registry repository."
}

output "image_ref" {
  value       = local.image_ref
  description = "Fully-qualified Artifact Registry image reference used by the workload."
}

output "image_digest" {
  value       = var.image_digest
  description = "Immutable digest used by the workload when digest deployment is enabled."
}

output "openfga_image" {
  value       = var.openfga_image
  description = "OpenFGA image used by the sidecar and migration init container."
}

output "runtime_service_account" {
  value       = var.platform_contract.runtime_service_account
  description = "Platform-managed Kubernetes service account used by the workload."
}

output "service_name" {
  value       = var.platform_contract.service_name
  description = "Platform-managed ClusterIP service that fronts the workload."
}

output "pvc_name" {
  value       = var.platform_contract.pvc_name
  description = "Platform-managed PVC mounted into the workload."
}

output "runtime_contract_config_map_name" {
  value       = var.platform_contract.runtime_contract_config_map
  description = "Platform-managed ConfigMap exposed to the workload via envFrom."
}

output "app_config_map_name" {
  value       = kubernetes_config_map_v1.app_config.metadata[0].name
  description = "App-owned ConfigMap created by this stack."
}

output "secret_provider_class_name" {
  value       = local.secret_provider_class_name
  description = "Platform-managed SecretProviderClass name when runtime secrets are configured."
}

output "health_check_path" {
  value       = var.platform_contract.health_check_path
  description = "HTTP path used by the app readiness and liveness probes."
}

output "data_mount_path" {
  value       = var.data_mount_path
  description = "Mount path for Freetool SQLite data inside the API container."
}

output "runtime_secrets_mount_path" {
  value       = var.runtime_secrets_mount_path
  description = "Mount path for platform-managed runtime secrets."
}

output "openfga_data_mount_path" {
  value       = var.openfga_data_mount_path
  description = "Mount path for OpenFGA SQLite data inside the OpenFGA containers."
}

output "sqlite_pvc_subpath" {
  value       = var.sqlite_pvc_subpath
  description = "PVC subpath used for Freetool SQLite files."
}

output "openfga_pvc_subpath" {
  value       = var.openfga_pvc_subpath
  description = "PVC subpath used for OpenFGA SQLite files."
}

output "state_bucket_name" {
  value       = var.platform_contract.state_bucket_name
  description = "Per-app GCS bucket intended for this repo's OpenTofu state."
}

output "iap_jwt_audience" {
  value       = var.platform_contract.iap_jwt_audience
  description = "IAP JWT audience from the platform contract."
}
