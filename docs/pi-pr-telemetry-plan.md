# Pi PR Telemetry Plan

This repo uses a project-local Pi extension plus `scripts/signoff-pr.sh` to collect and export aggregate agent usage telemetry for pull requests.

## Goals

For each PR, report:

1. Tools used while producing the PR.
2. Skills loaded or explicitly invoked.
3. MCP servers/tools called.
4. Token usage, compactions, and context-window usage.
5. Whether the contributing Pi session was single-repo/single-PR or part of a multi-repo/multi-branch flow.

## Runtime collection

The project-local Pi extension lives at:

```text
.pi/extensions/mcp-telemetry.ts
```

It records lightweight JSONL events to:

```text
.git/pi-telemetry/events.jsonl
```

The event stream intentionally stays local and is not committed. Each event includes attribution metadata captured at runtime:

- timestamp
- Pi session id
- Pi session file
- current working directory
- resolved git root
- current branch
- current HEAD SHA
- event type
- tool/skill/MCP metadata when applicable
- assistant usage and context metadata when available

Runtime git metadata is required so `signoff-pr.sh` can deterministically classify multi-repo and multi-branch sessions later. Without this runtime telemetry, classification would be heuristic only.

## MCP bridge

Pi does not natively consume `.mcp.json`. The extension reads this repo's `.mcp.json` and exposes:

- a generic `mcp_call` tool
- best-effort dynamic tools named `mcp__<server>__<tool>` for tools discovered via MCP `tools/list`

For example, a server/tool pair such as:

```text
internal-tools.use_workflow
```

is exposed as a Pi tool named:

```text
mcp__internal_tools__use_workflow
```

and can also be called through:

```text
mcp_call({ server: "internal-tools", tool: "use_workflow", arguments: { ... } })
```

The extension records MCP calls separately from skill loads, so reports can distinguish:

- skill referenced MCP
- MCP was actually called
- MCP returned an error or success

## Skill attribution

Skills are instruction bundles, not native tools. The extension records skill usage from two signals:

1. Explicit user input such as `/skill:new-controller`.
2. Model reads of paths ending in `/<skill-name>/SKILL.md`.

For this repo's shared workflow stubs, MCP-backed work should therefore show both:

- `skill_used` for the stub `SKILL.md`
- `mcp_call` for the corresponding shared workflow tool

## Signoff integration

`scripts/signoff-pr.sh` exports a summary after it verifies/creates the PR and before `gh signoff`:

```text
.pi/pr-telemetry-summary.json
.pi/pr-telemetry-summary.md
```

Those files are ignored by git.

The exporter reads `.git/pi-telemetry/events.jsonl`, slices events for the current repo/branch, then compares contributing session ids against all events in those sessions.

## Classification

Classification is deterministic when runtime telemetry exists:

- multi-repo session: a contributing session has events from more than one git root
- multi-branch session: a contributing session has events from more than one git-root/branch pair
- multi-PR session: derived from multi-branch classification, and can be resolved further by querying GitHub for PRs by branch

Reports include method/confidence fields, for example:

```json
{
  "multiRepoSession": true,
  "multiBranchSession": true,
  "multiPrSession": "unresolved",
  "method": "runtime-telemetry:sessionId+gitRoot+branch",
  "confidence": "high"
}
```

## Token/context interpretation

Context-window usage is a property of the whole Pi session. In multi-repo flows, the report includes both:

- PR-attributed repo/branch slice
- shared contributing-session total

The shared-session total is the authoritative cost/context figure. The PR-attributed slice is useful for rough per-branch inspection but should not be interpreted as exclusive context ownership.
