#!/usr/bin/env node
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { execFileSync } from "node:child_process";

function execGit(args, cwd = process.cwd()) {
  try {
    return execFileSync("git", args, { cwd, encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] }).trim();
  } catch {
    return "";
  }
}

function parseArgs(argv) {
  const args = {};
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (!arg.startsWith("--")) continue;
    const key = arg.slice(2);
    const next = argv[i + 1];
    if (!next || next.startsWith("--")) args[key] = true;
    else {
      args[key] = next;
      i += 1;
    }
  }
  return args;
}

function readJsonl(path) {
  if (!existsSync(path)) return [];
  return readFileSync(path, "utf8")
    .split(/\r?\n/)
    .filter(Boolean)
    .flatMap((line) => {
      try {
        return [JSON.parse(line)];
      } catch {
        return [];
      }
    });
}

function unique(values) {
  return [...new Set(values.filter((value) => value !== undefined && value !== null && value !== ""))];
}

function increment(map, key, by = 1) {
  if (!key) return;
  map.set(key, (map.get(key) ?? 0) + by);
}

function sumUsage(events) {
  const totals = { input: 0, output: 0, cacheRead: 0, cacheWrite: 0, totalTokens: 0, cost: 0 };
  for (const event of events) {
    if (event.eventType !== "assistant_usage" || !event.usage) continue;
    totals.input += Number(event.usage.input ?? 0);
    totals.output += Number(event.usage.output ?? 0);
    totals.cacheRead += Number(event.usage.cacheRead ?? 0);
    totals.cacheWrite += Number(event.usage.cacheWrite ?? 0);
    totals.totalTokens += Number(event.usage.totalTokens ?? (Number(event.usage.input ?? 0) + Number(event.usage.output ?? 0)));
    totals.cost += Number(event.usage.cost?.total ?? 0);
  }
  return totals;
}

function peakContext(events) {
  let peakTokens = 0;
  let contextWindow = 0;
  for (const event of events) {
    const context = event.context;
    if (!context) continue;
    const tokens = Number(context.tokens ?? context.totalTokens ?? context.usedTokens ?? 0);
    const window = Number(context.contextWindow ?? context.window ?? context.maxTokens ?? 0);
    if (tokens > peakTokens) peakTokens = tokens;
    if (window > contextWindow) contextWindow = window;
  }
  return {
    peakTokens,
    contextWindow,
    peakPercent: peakTokens && contextWindow ? Number(((peakTokens / contextWindow) * 100).toFixed(1)) : undefined,
  };
}

function formatNumber(value) {
  if (value === undefined || value === null || Number.isNaN(Number(value))) return "n/a";
  return Number(value).toLocaleString();
}

function tableRows(map) {
  return [...map.entries()].sort((a, b) => b[1] - a[1]).map(([name, count]) => `| ${name} | ${count} |`).join("\n") || "| _none_ | 0 |";
}

function buildSummary({ repoRoot, branch, headSha, baseRef, events }) {
  const currentEvents = events.filter((event) => event.gitRoot === repoRoot && event.branch === branch);
  const sessionIds = unique(currentEvents.map((event) => event.sessionId));
  const sessionEvents = events.filter((event) => sessionIds.includes(event.sessionId));

  const repoBranchPairs = unique(sessionEvents.map((event) => event.gitRoot && event.branch ? `${event.gitRoot}::${event.branch}` : undefined));
  const repos = unique(sessionEvents.map((event) => event.gitRoot));
  const branches = unique(sessionEvents.map((event) => event.gitRoot && event.branch ? `${event.gitRoot} (${event.branch})` : undefined));

  const toolCounts = new Map();
  const skillCounts = new Map();
  const mcpCounts = new Map();
  const mcpSessionCounts = new Map();

  for (const event of currentEvents) {
    if (event.eventType === "tool_call") increment(toolCounts, event.toolName);
    if (event.eventType === "skill_used") increment(skillCounts, event.skillName);
    if (event.eventType === "mcp_call") increment(mcpCounts, `${event.server}.${event.tool}`);
  }
  for (const event of sessionEvents) {
    if (event.eventType === "mcp_call") increment(mcpSessionCounts, `${event.server}.${event.tool}`);
  }

  const repoUsage = sumUsage(currentEvents);
  const sessionUsage = sumUsage(sessionEvents);
  const repoContext = peakContext(currentEvents);
  const sessionContext = peakContext(sessionEvents);
  const repoCompactions = currentEvents.filter((event) => event.eventType === "compaction_end" && !event.aborted).length;
  const sessionCompactions = sessionEvents.filter((event) => event.eventType === "compaction_end" && !event.aborted).length;

  const classification = {
    multiRepoSession: repos.length > 1,
    multiBranchSession: repoBranchPairs.length > 1,
    multiPrSession: repoBranchPairs.length > 1 ? "unresolved" : false,
    method: sessionIds.length > 0 ? "runtime-telemetry:sessionId+gitRoot+branch" : "no-runtime-telemetry",
    confidence: sessionIds.length > 0 ? "high" : "low",
    reposTouchedInSession: repos.length,
  };

  const json = {
    pr: { repoRoot, branch, baseRef, headSha },
    attribution: { ...classification, sessionIds, branches },
    tools: Object.fromEntries(toolCounts),
    skills: Object.fromEntries(skillCounts),
    mcp: { prAttributed: Object.fromEntries(mcpCounts), sessionTotal: Object.fromEntries(mcpSessionCounts) },
    tokens: { prAttributed: repoUsage, sharedSessionTotal: sessionUsage },
    context: { prAttributed: repoContext, sharedSessionTotal: sessionContext },
    compactions: { prAttributed: repoCompactions, sharedSessionTotal: sessionCompactions },
  };

  const md = `## Pi usage summary\n\n` +
    `Attribution method: \`${classification.method}\` (${classification.confidence} confidence)\n\n` +
    `| Metric | Value |\n|---|---:|\n` +
    `| Sessions contributing to this branch | ${sessionIds.length} |\n` +
    `| Multi-repo session | ${classification.multiRepoSession ? "yes" : "no"} |\n` +
    `| Multi-branch session | ${classification.multiBranchSession ? "yes" : "no"} |\n` +
    `| Repositories touched in contributing sessions | ${classification.reposTouchedInSession} |\n` +
    `| PR-attributed input tokens | ${formatNumber(repoUsage.input)} |\n` +
    `| PR-attributed cached tokens read | ${formatNumber(repoUsage.cacheRead)} |\n` +
    `| PR-attributed output tokens | ${formatNumber(repoUsage.output)} |\n` +
    `| Shared-session input tokens | ${formatNumber(sessionUsage.input)} |\n` +
    `| Shared-session cached tokens read | ${formatNumber(sessionUsage.cacheRead)} |\n` +
    `| Shared-session output tokens | ${formatNumber(sessionUsage.output)} |\n` +
    `| PR-attributed compactions | ${repoCompactions} |\n` +
    `| Shared-session compactions | ${sessionCompactions} |\n` +
    `| Peak context during PR-attributed turns | ${repoContext.peakPercent === undefined ? "n/a" : `${repoContext.peakPercent}%`} |\n` +
    `| Peak context during contributing sessions | ${sessionContext.peakPercent === undefined ? "n/a" : `${sessionContext.peakPercent}%`} |\n\n` +
    `### Tools attributed to this PR\n\n| Tool | Calls |\n|---|---:|\n${tableRows(toolCounts)}\n\n` +
    `### Skills attributed to this PR\n\n| Skill | Loads |\n|---|---:|\n${tableRows(skillCounts)}\n\n` +
    `### MCP calls attributed to this PR\n\n| MCP tool | Calls |\n|---|---:|\n${tableRows(mcpCounts)}\n\n` +
    `> Token and context attribution is exact at the recorded event/turn level, but LLM context is shared across a session. For multi-repo sessions, use the shared-session totals for full cost/context and the PR-attributed numbers as a repo/branch slice.\n`;

  return { json, md };
}

const command = process.argv[2] ?? "summarize";
const args = parseArgs(process.argv.slice(3));
const repoRoot = args.repo || execGit(["rev-parse", "--show-toplevel"]);
if (!repoRoot) {
  console.error("Could not resolve git repository root.");
  process.exit(1);
}
const branch = args.branch || execGit(["branch", "--show-current"], repoRoot);
const headSha = args.head || execGit(["rev-parse", "HEAD"], repoRoot);
const baseRef = args.base || "";
const telemetryPath = args.telemetry || join(repoRoot, ".git", "pi-telemetry", "events.jsonl");

if (command !== "summarize") {
  console.error(`Unknown command: ${command}`);
  process.exit(1);
}

const events = readJsonl(telemetryPath);
const summary = buildSummary({ repoRoot, branch, headSha, baseRef, events });

const outJson = args["out-json"] || join(repoRoot, ".pi", "pr-telemetry-summary.json");
const outMd = args["out-md"] || join(repoRoot, ".pi", "pr-telemetry-summary.md");
mkdirSync(dirname(outJson), { recursive: true });
mkdirSync(dirname(outMd), { recursive: true });
writeFileSync(outJson, JSON.stringify(summary.json, null, 2) + "\n");
writeFileSync(outMd, summary.md);
console.log(`Wrote ${outJson}`);
console.log(`Wrote ${outMd}`);
