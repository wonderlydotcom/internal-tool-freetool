import type { ExtensionAPI, ExtensionContext } from "@mariozechner/pi-coding-agent";
import { Type } from "typebox";
import { appendFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, relative } from "node:path";
import { execFileSync } from "node:child_process";

// Project-local Pi extension that provides two capabilities:
// 1. A small MCP-over-HTTP bridge for servers declared in .mcp.json.
// 2. Runtime PR attribution telemetry used by scripts/signoff-pr.sh.
//
// Telemetry is aggregate metadata only. It intentionally avoids storing raw
// prompts, tool outputs, file contents, or MCP response bodies.

type McpServerConfig = {
  type?: string;
  url?: string;
};

type McpConfig = {
  mcpServers?: Record<string, McpServerConfig>;
};

type JsonRpcResponse = {
  jsonrpc?: string;
  id?: string | number;
  result?: any;
  error?: { code?: number; message?: string; data?: unknown };
};

type GitSnapshot = {
  cwd: string;
  gitRoot?: string;
  branch?: string;
  headSha?: string;
};

type EventBase = {
  timestamp: string;
  sessionId?: string;
  sessionFile?: string;
  eventType: string;
} & GitSnapshot;

const EXTENSION_NAME = "pi-mcp-telemetry";
const gitCache = new Map<string, { expiresAt: number; snapshot: GitSnapshot }>();
const mcpSessionIds = new Map<string, string>();
const initializedServers = new Set<string>();
let telemetryPath: string | undefined;
let sessionId: string | undefined;
let sessionFile: string | undefined;
let loadedMcpConfig: McpConfig = {};

function runGit(cwd: string, args: string[]): string | undefined {
  try {
    return execFileSync("git", args, {
      cwd,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
    }).trim() || undefined;
  } catch {
    return undefined;
  }
}

function getGitSnapshot(cwd: string): GitSnapshot {
  const cached = gitCache.get(cwd);
  const now = Date.now();
  if (cached && cached.expiresAt > now) return cached.snapshot;

  const gitRoot = runGit(cwd, ["rev-parse", "--show-toplevel"]);
  const snapshot: GitSnapshot = {
    cwd,
    gitRoot,
    branch: gitRoot ? runGit(gitRoot, ["branch", "--show-current"]) : undefined,
    headSha: gitRoot ? runGit(gitRoot, ["rev-parse", "HEAD"]) : undefined,
  };
  gitCache.set(cwd, { expiresAt: now + 1000, snapshot });
  return snapshot;
}

function ensureTelemetryPath(cwd: string): string | undefined {
  if (telemetryPath) return telemetryPath;
  const gitRoot = getGitSnapshot(cwd).gitRoot;
  if (!gitRoot) return undefined;
  telemetryPath = join(gitRoot, ".git", "pi-telemetry", "events.jsonl");
  mkdirSync(dirname(telemetryPath), { recursive: true });
  return telemetryPath;
}

function redactToolArgs(toolName: string, args: any): Record<string, unknown> {
  if (!args || typeof args !== "object") return {};
  if (toolName === "read") return { path: args.path };
  if (toolName === "write") return { path: args.path };
  if (toolName === "edit") return { path: args.path };
  if (toolName === "grep") return { pattern: args.pattern, path: args.path };
  if (toolName === "find") return { path: args.path, pattern: args.pattern };
  if (toolName === "ls") return { path: args.path };
  if (toolName === "bash") return { command: args.command, timeout: args.timeout };
  if (toolName.startsWith("mcp__")) return { mcpTool: toolName };
  return { keys: Object.keys(args) };
}

function extractPaths(toolName: string, args: any): string[] {
  if (!args || typeof args !== "object") return [];
  const paths = new Set<string>();
  for (const key of ["path", "file", "filePath", "root", "cwd"]) {
    const value = args[key];
    if (typeof value === "string" && value.length > 0) paths.add(value);
  }
  if (Array.isArray(args.paths)) {
    for (const value of args.paths) if (typeof value === "string") paths.add(value);
  }
  return [...paths];
}

function skillNameFromPath(pathValue: string): string | undefined {
  const normalized = pathValue.replaceAll("\\", "/");
  const match = normalized.match(/(?:^|\/)\.?(?:pi|agents)\/skills\/([^/]+)\/SKILL\.md$/);
  if (match) return match[1];
  return undefined;
}

function record(ctx: ExtensionContext | { cwd: string; sessionManager?: any } | undefined, event: Record<string, unknown>) {
  const cwd = ctx?.cwd ?? process.cwd();
  const path = ensureTelemetryPath(cwd);
  if (!path) return;

  const sm = (ctx as any)?.sessionManager;
  const base: EventBase = {
    timestamp: new Date().toISOString(),
    sessionId: sessionId ?? sm?.getSessionId?.(),
    sessionFile: sessionFile ?? sm?.getSessionFile?.(),
    eventType: String(event.eventType ?? "unknown"),
    ...getGitSnapshot(cwd),
  };
  appendFileSync(path, JSON.stringify({ ...base, ...event }) + "\n");
}

function loadMcpConfig(cwd: string): McpConfig {
  const gitRoot = getGitSnapshot(cwd).gitRoot ?? cwd;
  const configPath = join(gitRoot, ".mcp.json");
  if (!existsSync(configPath)) return {};
  try {
    return JSON.parse(readFileSync(configPath, "utf8")) as McpConfig;
  } catch (error) {
    record({ cwd }, { eventType: "mcp_config_error", configPath, error: String(error) });
    return {};
  }
}

function sanitizeName(value: string): string {
  return value.replace(/[^A-Za-z0-9_]/g, "_");
}

function mcpToolName(serverName: string, toolName: string): string {
  return `mcp__${sanitizeName(serverName)}__${sanitizeName(toolName)}`;
}

async function parseMcpResponse(response: Response): Promise<JsonRpcResponse> {
  const text = await response.text();
  const contentType = response.headers.get("content-type") ?? "";
  if (contentType.includes("text/event-stream")) {
    const dataLines = text
      .split(/\r?\n/)
      .filter((line) => line.startsWith("data:"))
      .map((line) => line.slice(5).trim())
      .filter((line) => line && line !== "[DONE]");
    const last = dataLines.at(-1);
    if (!last) return {};
    return JSON.parse(last) as JsonRpcResponse;
  }
  if (!text.trim()) return {};
  return JSON.parse(text) as JsonRpcResponse;
}

async function mcpRequest(serverName: string, method: string, params?: any): Promise<any> {
  const server = loadedMcpConfig.mcpServers?.[serverName];
  if (!server?.url) throw new Error(`MCP server '${serverName}' is not configured with a url.`);
  if (server.type && server.type !== "http") throw new Error(`MCP server '${serverName}' type '${server.type}' is not supported by this Pi bridge.`);

  const headers: Record<string, string> = {
    "content-type": "application/json",
    accept: "application/json, text/event-stream",
  };
  const existingSession = mcpSessionIds.get(serverName);
  if (existingSession) headers["mcp-session-id"] = existingSession;

  const response = await fetch(server.url, {
    method: "POST",
    headers,
    body: JSON.stringify({ jsonrpc: "2.0", id: `${Date.now()}-${Math.random()}`, method, params: params ?? {} }),
  });
  const responseSession = response.headers.get("mcp-session-id");
  if (responseSession) mcpSessionIds.set(serverName, responseSession);
  const payload = await parseMcpResponse(response);
  if (!response.ok) throw new Error(`MCP ${serverName}.${method} HTTP ${response.status}: ${JSON.stringify(payload.error ?? payload)}`);
  if (payload.error) throw new Error(`MCP ${serverName}.${method}: ${payload.error.message ?? JSON.stringify(payload.error)}`);
  return payload.result;
}

async function initializeMcpServer(serverName: string) {
  if (initializedServers.has(serverName)) return;
  await mcpRequest(serverName, "initialize", {
    protocolVersion: "2025-06-18",
    capabilities: {},
    clientInfo: { name: "pi-mcp-telemetry", version: "0.1.0" },
  });
  // Best-effort initialized notification. Some streamable HTTP servers accept it;
  // if they do not, the next tools/list call will surface the real compatibility issue.
  try {
    await mcpRequest(serverName, "notifications/initialized", {});
  } catch {
    // Ignore: notifications may not return JSON-RPC responses on every MCP server.
  }
  initializedServers.add(serverName);
}

async function listMcpTools(serverName: string): Promise<any[]> {
  await initializeMcpServer(serverName);
  const result = await mcpRequest(serverName, "tools/list", {});
  return Array.isArray(result?.tools) ? result.tools : [];
}

async function callMcpTool(serverName: string, toolName: string, args: any): Promise<any> {
  await initializeMcpServer(serverName);
  return await mcpRequest(serverName, "tools/call", { name: toolName, arguments: args ?? {} });
}

function summarizeMcpResult(result: any): string {
  const content = Array.isArray(result?.content) ? result.content : [];
  const text = content
    .map((item: any) => (item?.type === "text" && typeof item.text === "string" ? item.text : undefined))
    .filter(Boolean)
    .join("\n");
  if (text.trim()) return text;
  return JSON.stringify(result ?? {});
}

export default async function (pi: ExtensionAPI) {
  loadedMcpConfig = loadMcpConfig(process.cwd());

  pi.registerTool({
    name: "mcp_call",
    label: "MCP Call",
    description: "Call a tool on an MCP server configured in the repo's .mcp.json. Prefer dynamic mcp__server__tool tools when available.",
    parameters: Type.Object({
      server: Type.String({ description: "MCP server name from .mcp.json, for example internal-tools" }),
      tool: Type.String({ description: "MCP tool name, for example use_workflow" }),
      arguments: Type.Optional(Type.Record(Type.String(), Type.Any(), { description: "Arguments for the MCP tool" })),
    }),
    async execute(_toolCallId, params, _signal, _onUpdate, ctx) {
      record(ctx, { eventType: "mcp_call", server: params.server, tool: params.tool, source: "generic_tool" });
      const result = await callMcpTool(params.server, params.tool, params.arguments ?? {});
      record(ctx, { eventType: "mcp_result", server: params.server, tool: params.tool, isError: Boolean(result?.isError) });
      return { content: [{ type: "text", text: summarizeMcpResult(result) }], details: { server: params.server, tool: params.tool, isError: Boolean(result?.isError) }, isError: Boolean(result?.isError) };
    },
  });

  for (const serverName of Object.keys(loadedMcpConfig.mcpServers ?? {})) {
    try {
      const tools = await listMcpTools(serverName);
      record({ cwd: process.cwd() }, { eventType: "mcp_tools_discovered", server: serverName, tools: tools.map((tool) => tool.name) });
      for (const tool of tools) {
        if (!tool?.name) continue;
        const registeredName = mcpToolName(serverName, tool.name);
        pi.registerTool({
          name: registeredName,
          label: `${serverName}.${tool.name}`,
          description: `[MCP ${serverName}] ${tool.description ?? tool.name}`,
          parameters: tool.inputSchema ?? Type.Object({}),
          async execute(_toolCallId, params, _signal, _onUpdate, ctx) {
            record(ctx, { eventType: "mcp_call", server: serverName, tool: tool.name, registeredTool: registeredName, source: "dynamic_tool" });
            const result = await callMcpTool(serverName, tool.name, params ?? {});
            record(ctx, { eventType: "mcp_result", server: serverName, tool: tool.name, registeredTool: registeredName, isError: Boolean(result?.isError) });
            return { content: [{ type: "text", text: summarizeMcpResult(result) }], details: { server: serverName, tool: tool.name, isError: Boolean(result?.isError) }, isError: Boolean(result?.isError) };
          },
        });
      }
    } catch (error) {
      record({ cwd: process.cwd() }, { eventType: "mcp_discovery_error", server: serverName, error: String(error) });
    }
  }

  pi.on("session_start", async (_event, ctx) => {
    sessionId = ctx.sessionManager.getSessionId?.();
    sessionFile = ctx.sessionManager.getSessionFile?.();
    telemetryPath = undefined;
    loadedMcpConfig = loadMcpConfig(ctx.cwd);
    record(ctx, {
      eventType: "session_start",
      mcpServersConfigured: Object.keys(loadedMcpConfig.mcpServers ?? {}),
      telemetryVersion: 1,
    });
  });

  pi.on("input", async (event, ctx) => {
    const explicitSkill = event.text.match(/^\/skill:([^\s]+)/)?.[1];
    record(ctx, {
      eventType: "input",
      source: event.source,
      explicitSkill,
      textLength: event.text.length,
      hasImages: Boolean(event.images?.length),
    });
  });

  pi.on("before_agent_start", async (event, ctx) => {
    const skills = event.systemPromptOptions?.skills?.map((skill: any) => skill.name).filter(Boolean) ?? [];
    record(ctx, { eventType: "before_agent_start", availableSkills: skills });
  });

  pi.on("tool_call", async (event, ctx) => {
    const paths = extractPaths(event.toolName, event.input);
    const skillReads = paths.map(skillNameFromPath).filter(Boolean);
    record(ctx, {
      eventType: "tool_call",
      toolName: event.toolName,
      toolCallId: event.toolCallId,
      args: redactToolArgs(event.toolName, event.input),
      paths,
      skillReads,
    });
    for (const skillName of skillReads) {
      record(ctx, { eventType: "skill_used", kind: "skill_read", skillName, path: paths.find((path) => skillNameFromPath(path) === skillName) });
    }
  });

  pi.on("tool_execution_end", async (event, ctx) => {
    record(ctx, { eventType: "tool_execution_end", toolName: event.toolName, toolCallId: event.toolCallId, isError: event.isError });
  });

  pi.on("message_end", async (event, ctx) => {
    if (event.message.role !== "assistant") return;
    const usage = (event.message as any).usage;
    const context = ctx.getContextUsage?.();
    record(ctx, {
      eventType: "assistant_usage",
      provider: (event.message as any).provider,
      model: (event.message as any).model,
      usage,
      context,
    });
  });

  pi.on("turn_end", async (_event, ctx) => {
    const context = ctx.getContextUsage?.();
    if (context) record(ctx, { eventType: "context_usage", context });
  });

  pi.on("compaction_start", async (event, ctx) => {
    record(ctx, { eventType: "compaction_start", reason: event.reason });
  });

  pi.on("compaction_end", async (event, ctx) => {
    record(ctx, {
      eventType: "compaction_end",
      reason: event.reason,
      aborted: event.aborted,
      willRetry: event.willRetry,
      errorMessage: event.errorMessage,
      result: event.result
        ? { tokensBefore: (event.result as any).tokensBefore, firstKeptEntryId: (event.result as any).firstKeptEntryId }
        : undefined,
    });
  });

  pi.registerCommand("pr-telemetry-path", {
    description: "Show the local Pi PR telemetry event log path for this repository.",
    handler: async (_args, ctx) => {
      const path = ensureTelemetryPath(ctx.cwd);
      if (path) ctx.ui.notify(path, "info");
    },
  });
}
