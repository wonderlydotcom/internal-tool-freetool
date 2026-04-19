locals {
  kubeconfig_path_value = try(trimspace(var.kubeconfig_path), "")
}

provider "kubernetes" {
  config_path = local.kubeconfig_path_value != "" ? pathexpand(local.kubeconfig_path_value) : null
}
