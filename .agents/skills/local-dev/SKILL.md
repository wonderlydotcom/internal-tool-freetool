---
name: local-dev
description: Run and seed internal tool app repos locally with the shared CLI while preserving data and secret safety boundaries.
mcp_server: internal-tools
mcp_tool: use_workflow
mcp_workflow: local-dev
mcp_repo: freetool
mcp_kind: shared-stub
---

# Local Dev

Call `internal-tools.use_workflow` with:

- `workflow_name="local-dev"`
- `repo_name="freetool"`

If the task is not an obvious fit for this stub, call `internal-tools.recommend_workflows` first and then use the top shared workflow before editing.

Then follow the returned:

- files to inspect
- workflow steps
- validation commands
- related workflows

If the change also touches adjacent concerns, follow the related workflows returned by `internal-tools.use_workflow`.
