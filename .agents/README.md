# Freetool Agent Skills

Shared internal-tools skills are now served by the deployed `internal-tools-mcp` server configured in [`.codex/config.toml`](../.codex/config.toml) and [`.mcp.json`](../.mcp.json).

## Local Skills

- `freetool-controller-authoring`: repo-specific controller authoring guidance
- `freetool-iap-auth-architecture`: repo-specific IAP architecture guidance
- `freetool-openfga-hexagonal-architecture`: repo-specific OpenFGA architecture guidance

Reserve `.agents/skills` for repo-specific capabilities like the items above. Shared skills should come from MCP.
