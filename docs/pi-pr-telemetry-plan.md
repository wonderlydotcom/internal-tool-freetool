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

It records lightweight JSONL events to the repo-local stream:

```text
.git/pi-telemetry/events.jsonl
```

and mirrors the same aggregate events to a user-local stream:

```text
~/.pi/agent/pr-telemetry/events.jsonl
```

Both event streams intentionally stay local and are not committed. Each event includes attribution metadata captured at runtime:

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

Runtime git metadata lets `signoff-pr.sh` deterministically classify multi-repo and multi-branch sessions later. When runtime telemetry is unavailable, the exporter falls back to Pi's local session JSONL files and derives a heuristic repo/branch slice from session id plus repo/branch mentions in tool calls and tool results.

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

The exporter reads, in order:

1. `.git/pi-telemetry/events.jsonl`
2. `~/.pi/agent/pr-telemetry/events.jsonl`
3. Pi session logs under `~/.pi/agent/sessions/` as a fallback

It slices events for the current repo/branch, then compares contributing session ids against all events in those sessions. If a contributing Pi session started outside the repo folder, the exporter treats the whole session as the PR context because that is the context the model used while producing the PR. `signoff-pr.sh` passes a cutoff timestamp to keep later Pi activity from changing the summary when old branches are re-summarized.

## Classification

Classification is deterministic when runtime telemetry exists and heuristic when the exporter falls back to Pi session logs:

- multi-repo session: a contributing session has events or session-log mentions from more than one git root
- multi-branch session: a contributing session has events or session-log mentions from more than one branch name
- multi-PR session: a contributing session has more than one git-root/branch pair, and can be resolved further by querying GitHub for PRs by branch

Reports include method/confidence fields, for example:

```json
{
  "multiRepoSession": true,
  "multiBranchSession": false,
  "multiPrSession": "likely",
  "method": "runtime-telemetry+pi-session-log",
  "confidence": "medium"
}
```

## Token/context interpretation

Context-window usage is a property of the whole Pi session. In multi-repo flows, the report includes both:

- PR-attributed repo/branch slice
- shared contributing-session total

The shared-session total is the authoritative cost/context figure. When a contributing session started outside the repo folder, the PR-attributed totals intentionally equal the full session totals; this duplicates cost/context across PRs from the same multi-repo session, but it is more honest than pretending the shared model context can be cleanly split by repository. For repo-started session-log fallback summaries, PR-attributed slices remain heuristic based on repo/branch mentions in tool calls and results.
