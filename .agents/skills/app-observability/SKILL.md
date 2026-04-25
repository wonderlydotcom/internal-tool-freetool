---
name: app-observability
description: Triage app logs, traces, metrics, and alerts through the owner-safe internal-tools control plane and CLI.
mcp_server: internal-tools
mcp_tool: use_workflow
mcp_workflow: app-observability
mcp_repo: internal-tool-freetool
mcp_kind: shared-stub
---

# App Observability

Call `internal-tools.use_workflow` with:

- `workflow_name="app-observability"`
- `repo_name="internal-tools-starter"`

If the task is not an obvious fit for this stub, call `internal-tools.recommend_workflows` first and then use the top shared workflow before editing.

Then follow the returned:

- files to inspect
- workflow steps
- validation commands
- related workflows

If the change also touches adjacent concerns, follow the related workflows returned by `internal-tools.use_workflow`.
