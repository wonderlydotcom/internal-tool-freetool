#!/usr/bin/env node
import { existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from "node:fs";
import { dirname, join, resolve, basename } from "node:path";
import { execFileSync } from "node:child_process";
import { homedir } from "node:os";

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

function dedupeEvents(events) {
  const seen = new Set();
  return events.filter((event) => {
    const key = event.eventId || `${event.eventSource ?? "runtime"}:${JSON.stringify(event)}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function sumUsage(events) {
  const totals = { input: 0, output: 0, cacheRead: 0, cacheWrite: 0, totalTokens: 0, cost: 0 };
  const seen = new Set();
  for (const event of events) {
    if (event.eventType !== "assistant_usage" || !event.usage) continue;
    const key = event.eventId || JSON.stringify(event.usage) + event.timestamp + event.sessionId;
    if (seen.has(key)) continue;
    seen.add(key);
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

function defaultGlobalTelemetryPath() {
  return join(process.env.PI_PR_TELEMETRY_DIR || join(homedir(), ".pi", "agent", "pr-telemetry"), "events.jsonl");
}

function defaultSessionRoot() {
  return process.env.PI_SESSION_ROOT || join(homedir(), ".pi", "agent", "sessions");
}

function walkJsonl(root) {
  if (!root || !existsSync(root)) return [];
  const results = [];
  const stack = [root];
  while (stack.length > 0) {
    const current = stack.pop();
    let entries = [];
    try {
      entries = readdirSync(current, { withFileTypes: true });
    } catch {
      continue;
    }
    for (const entry of entries) {
      const path = join(current, entry.name);
      if (entry.isDirectory()) stack.push(path);
      else if (entry.isFile() && entry.name.endsWith(".jsonl")) results.push(path);
    }
  }
  return results;
}

function remoteSlug(remoteUrl) {
  const trimmed = remoteUrl.trim().replace(/\.git$/, "");
  const ssh = trimmed.match(/github\.com[:/]([^/]+\/[^/]+)$/);
  if (ssh) return ssh[1];
  return undefined;
}

function discoverSiblingRepos(repoRoot) {
  const roots = new Set([repoRoot]);
  const parent = dirname(repoRoot);
  try {
    for (const entry of readdirSync(parent, { withFileTypes: true })) {
      if (!entry.isDirectory()) continue;
      const candidate = join(parent, entry.name);
      if (existsSync(join(candidate, ".git"))) roots.add(candidate);
    }
  } catch {
    // Ignore discovery failures; the current repo is still available.
  }

  return [...roots].map((root) => {
    const remote = execGit(["remote", "get-url", "origin"], root);
    return {
      root,
      name: basename(root),
      remote,
      slug: remote ? remoteSlug(remote) : undefined,
    };
  });
}

function extractRepoMentions(text, repoInfos) {
  if (!text) return [];
  const roots = new Set();
  for (const repo of repoInfos) {
    if (text.includes(repo.root)) {
      roots.add(repo.root);
      continue;
    }
    if (repo.slug && text.includes(repo.slug)) {
      roots.add(repo.root);
      continue;
    }
    // Require a path-like mention for local directory names. Bare names show up in
    // generic directory listings and would over-count repositories that were seen
    // but not actually touched by the session.
    if (text.includes(`${repo.name}/`) || text.includes(`./${repo.name}`)) roots.add(repo.root);
  }
  return [...roots];
}

function rootsForPath(value, cwd, repoInfos) {
  if (typeof value !== "string" || value.length === 0) return [];
  const path = value.startsWith("/") ? value : resolve(cwd, value);
  return repoInfos.filter((repo) => path === repo.root || path.startsWith(`${repo.root}/`)).map((repo) => repo.root);
}

function contentText(content) {
  if (!Array.isArray(content)) return "";
  return content
    .map((item) => {
      if (typeof item?.text === "string") return item.text;
      if (item?.type === "toolCall") return JSON.stringify({ name: item.name, arguments: item.arguments ?? {} });
      return "";
    })
    .filter(Boolean)
    .join("\n");
}

function repoBranches(roots, branch) {
  if (!branch) return [];
  return unique(roots).map((root) => ({ gitRoot: root, branch }));
}

function parseMcpToolName(toolName, args) {
  if (toolName === "mcp_call") return { server: args?.server, tool: args?.tool };
  const match = toolName.match(/^mcp__([^_].*?)__(.+)$/);
  if (!match) return undefined;
  return { server: match[1].replaceAll("_", "-"), tool: match[2].replaceAll("_", "-") };
}

function signalTextForEntry(entry) {
  if (entry.type === "session") return entry.cwd ?? "";
  const message = entry.message;
  if (!message) return "";
  if (message.role === "toolResult") return contentText(message.content);
  return (message.content ?? [])
    .map((item) => {
      if (item?.type === "toolCall") return JSON.stringify({ name: item.name, arguments: item.arguments ?? {} });
      if (item?.type === "text" && typeof item.text === "string") return item.text;
      return "";
    })
    .filter(Boolean)
    .join("\n");
}

function synthesizeEventsFromSessionFile(sessionPath, repoInfos, targetBranch, cutoffMs) {
  const entries = readJsonl(sessionPath).filter((entry) => !entry.timestamp || Number.isNaN(cutoffMs) || Date.parse(entry.timestamp) <= cutoffMs);
  if (entries.length === 0) return [];

  const sessionEntry = entries.find((entry) => entry.type === "session");
  const sessionId = sessionEntry?.id || basename(sessionPath).replace(/\.jsonl$/, "").split("_").at(-1);
  const sessionCwd = sessionEntry?.cwd || dirname(sessionPath);
  const sessionSignalText = entries.map(signalTextForEntry).join("\n");
  const branchMentioned = Boolean(targetBranch && sessionSignalText.includes(targetBranch));
  const sessionRoots = new Set(extractRepoMentions(sessionSignalText, repoInfos));
  for (const root of rootsForPath(sessionCwd, sessionCwd, repoInfos)) sessionRoots.add(root);

  if (sessionRoots.size === 0 || !branchMentioned) return [];

  const toolResultRoots = new Map();
  for (const entry of entries) {
    const message = entry.message;
    if (entry.type !== "message" || message?.role !== "toolResult") continue;
    const roots = extractRepoMentions(contentText(message.content), repoInfos);
    if (roots.length > 0) toolResultRoots.set(message.toolCallId, roots);
  }

  const events = [{
    eventSource: "pi-session",
    eventId: `${sessionPath}:session_touch:${targetBranch}`,
    timestamp: sessionEntry?.timestamp || entries[0]?.timestamp,
    sessionId,
    sessionFile: sessionPath,
    cwd: sessionCwd,
    eventType: "session_touch",
    touchedGitRoots: [...sessionRoots],
    repoBranches: repoBranches([...sessionRoots], targetBranch),
  }];

  for (const entry of entries) {
    const message = entry.message;
    if (entry.type !== "message" || message?.role !== "assistant") continue;

    const turnRoots = new Set(extractRepoMentions(contentText(message.content), repoInfos));
    const toolCalls = (message.content ?? []).filter((item) => item?.type === "toolCall");

    for (const toolCall of toolCalls) {
      const toolText = JSON.stringify({ name: toolCall.name, arguments: toolCall.arguments ?? {} });
      const roots = new Set(extractRepoMentions(toolText, repoInfos));
      for (const root of toolResultRoots.get(toolCall.id) ?? []) roots.add(root);
      for (const root of roots) turnRoots.add(root);

      const touchedGitRoots = [...roots];
      events.push({
        eventSource: "pi-session",
        eventId: `${sessionPath}:${entry.id}:${toolCall.id}:tool_call`,
        timestamp: entry.timestamp,
        sessionId,
        sessionFile: sessionPath,
        cwd: sessionCwd,
        eventType: "tool_call",
        toolName: toolCall.name,
        toolCallId: toolCall.id,
        touchedGitRoots,
        repoBranches: repoBranches(touchedGitRoots, targetBranch),
      });

      const mcp = parseMcpToolName(toolCall.name, toolCall.arguments ?? {});
      if (mcp?.server && mcp?.tool) {
        events.push({
          eventSource: "pi-session",
          eventId: `${sessionPath}:${entry.id}:${toolCall.id}:mcp_call`,
          timestamp: entry.timestamp,
          sessionId,
          sessionFile: sessionPath,
          cwd: sessionCwd,
          eventType: "mcp_call",
          server: mcp.server,
          tool: mcp.tool,
          touchedGitRoots,
          repoBranches: repoBranches(touchedGitRoots, targetBranch),
        });
      }
    }

    events.push({
      eventSource: "pi-session",
      eventId: `${sessionPath}:${entry.id}:assistant_usage`,
      timestamp: entry.timestamp,
      sessionId,
      sessionFile: sessionPath,
      cwd: sessionCwd,
      eventType: "assistant_usage",
      provider: message.provider,
      model: message.model,
      usage: message.usage,
      touchedGitRoots: [...turnRoots],
      repoBranches: repoBranches([...turnRoots], targetBranch),
    });
  }

  return events;
}

function synthesizeEventsFromPiSessions({ repoRoot, branch, sessionRoot, cutoff }) {
  const repoInfos = discoverSiblingRepos(repoRoot);
  const cutoffMs = cutoff ? Date.parse(cutoff) : Date.now();
  return walkJsonl(sessionRoot).flatMap((sessionPath) => synthesizeEventsFromSessionFile(sessionPath, repoInfos, branch, cutoffMs));
}

function eventRepoBranches(event) {
  const pairs = [];
  if (event.gitRoot && event.branch) pairs.push({ gitRoot: event.gitRoot, branch: event.branch });
  for (const pair of event.repoBranches ?? []) {
    if (typeof pair === "string") {
      const index = pair.lastIndexOf("::");
      if (index > 0) pairs.push({ gitRoot: pair.slice(0, index), branch: pair.slice(index + 2) });
    } else if (pair?.gitRoot && pair?.branch) {
      pairs.push({ gitRoot: pair.gitRoot, branch: pair.branch });
    }
  }
  return pairs;
}

function eventRepos(event) {
  return unique([event.gitRoot, ...(event.touchedGitRoots ?? []), ...eventRepoBranches(event).map((pair) => pair.gitRoot)]);
}

function eventTouchesRepoBranch(event, repoRoot, branch) {
  if (event.gitRoot === repoRoot && event.branch === branch) return true;
  return eventRepoBranches(event).some((pair) => pair.gitRoot === repoRoot && pair.branch === branch);
}

function pathIsInside(path, root) {
  if (!path || !root) return false;
  const resolvedPath = resolve(path);
  const resolvedRoot = resolve(root);
  return resolvedPath === resolvedRoot || resolvedPath.startsWith(`${resolvedRoot}/`);
}

function sessionStartedOutsideRepo(events, repoRoot) {
  const startEvent = events.find((event) => event.eventType === "session_start" || event.eventType === "session_touch");
  const cwd = startEvent?.cwd ?? events.find((event) => event.cwd)?.cwd;
  return Boolean(cwd && !pathIsInside(cwd, repoRoot));
}

function repoBranchKey(pair) {
  return `${pair.gitRoot}::${pair.branch}`;
}

function buildSummary({ repoRoot, branch, headSha, baseRef, events }) {
  const directlyAttributedEvents = events.filter((event) => eventTouchesRepoBranch(event, repoRoot, branch));
  const sessionIds = unique(directlyAttributedEvents.map((event) => event.sessionId));
  const sessionEvents = events.filter((event) => sessionIds.includes(event.sessionId));
  const fullContextSessionIds = new Set(
    sessionIds.filter((id) => sessionStartedOutsideRepo(sessionEvents.filter((event) => event.sessionId === id), repoRoot))
  );
  const currentEvents = dedupeEvents([
    ...directlyAttributedEvents,
    ...sessionEvents.filter((event) => fullContextSessionIds.has(event.sessionId)),
  ]);

  const repoBranchPairs = unique(sessionEvents.flatMap((event) => eventRepoBranches(event).map(repoBranchKey)));
  const repos = unique(sessionEvents.flatMap(eventRepos));
  const branchNames = unique(sessionEvents.flatMap((event) => eventRepoBranches(event).map((pair) => pair.branch)));
  const branches = unique(sessionEvents.flatMap((event) => eventRepoBranches(event).map((pair) => `${pair.gitRoot} (${pair.branch})`)));

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
  const hasRuntimeTelemetry = currentEvents.some((event) => event.eventSource !== "pi-session");
  const hasRuntimeActionTelemetry = currentEvents.some((event) => event.eventSource !== "pi-session" && ["assistant_usage", "tool_call", "mcp_call", "skill_used"].includes(event.eventType));
  const hasSessionTelemetry = currentEvents.some((event) => event.eventSource === "pi-session");

  const classification = {
    multiRepoSession: repos.length > 1,
    multiBranchSession: branchNames.length > 1,
    multiPrSession: repoBranchPairs.length > 1 ? "likely" : false,
    method: hasRuntimeTelemetry && hasSessionTelemetry
      ? "runtime-telemetry+pi-session-log"
      : hasRuntimeTelemetry
        ? "runtime-telemetry:sessionId+gitRoot+branch"
        : hasSessionTelemetry
          ? "pi-session-log:sessionId+repo/branch-mentions"
          : "no-runtime-telemetry",
    confidence: hasRuntimeActionTelemetry ? "high" : hasSessionTelemetry || hasRuntimeTelemetry ? "medium" : "low",
    reposTouchedInSession: repos.length,
    fullSessionContext: fullContextSessionIds.size > 0,
    sessionsUsingFullContext: fullContextSessionIds.size,
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
    `| Full-session context used | ${classification.fullSessionContext ? "yes" : "no"} |\n` +
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
    `> Token and context attribution is exact for runtime telemetry events. When a contributing Pi session started outside this repo, the PR-attributed totals intentionally use the full session because that was the model context used to produce the PR. This duplicates shared-session totals across PRs from the same multi-repo session, but avoids pretending that shared context can be cleanly split per repo. When the exporter falls back to Pi session logs for a repo-started session, PR-attributed slices are heuristic based on repo/branch mentions in tool calls and results.\n`;

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
const localTelemetryPath = join(repoRoot, ".git", "pi-telemetry", "events.jsonl");
const telemetryPaths = unique([args.telemetry, localTelemetryPath, defaultGlobalTelemetryPath()]);

if (command !== "summarize") {
  console.error(`Unknown command: ${command}`);
  process.exit(1);
}

let events = telemetryPaths.flatMap(readJsonl);
if (args["no-session-scan"] !== true) {
  events = events.concat(synthesizeEventsFromPiSessions({ repoRoot, branch, sessionRoot: args["session-root"] || defaultSessionRoot(), cutoff: args.cutoff }));
}
events = dedupeEvents(events);

const summary = buildSummary({ repoRoot, branch, headSha, baseRef, events });

const outJson = args["out-json"] || join(repoRoot, ".pi", "pr-telemetry-summary.json");
const outMd = args["out-md"] || join(repoRoot, ".pi", "pr-telemetry-summary.md");
mkdirSync(dirname(outJson), { recursive: true });
mkdirSync(dirname(outMd), { recursive: true });
writeFileSync(outJson, JSON.stringify(summary.json, null, 2) + "\n");
writeFileSync(outMd, summary.md);
console.log(`Wrote ${outJson}`);
console.log(`Wrote ${outMd}`);
