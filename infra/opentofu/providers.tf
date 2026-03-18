locals {
  kubeconfig_path_value = try(trimspace(var.kubeconfig_path), "")
  kubeconfig_path       = local.kubeconfig_path_value != "" ? pathexpand(local.kubeconfig_path_value) : pathexpand("~/.kube/config")
}

provider "kubernetes" {
  config_path = local.kubeconfig_path
}
