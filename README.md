# Praetorium.Bridge

> **Turn any AI agent into a callable MCP tool — with configurable signaling, session management, and prompt templates.**

Praetorium.Bridge is a .NET 10 bridge that exposes user-defined [Model Context Protocol](https://modelcontextprotocol.io) tools over HTTP, routes each call to a pooled AI agent session, and gives you a Blazor dashboard for monitoring and live configuration. Tools are declared in JSON — no code changes, no redeploys.

> **Status:** `0.1.0` — prerelease. Interfaces, schema, and UI are stabilizing; expect breaking changes.

---

## What it does

```
External MCP caller ──HTTP──▶ Bridge MCP server ──▶ Tool dispatcher
                                                         │
                                                         ▼
                                                Session manager
                                                         │
                                                         ▼
                                      Agent provider (GitHub Copilot SDK)
                                                         │
                                                         ▼
                                     Agent session  ◀──▶  Signaling tools
                                                         │
                                                         ▼
                              Agent-side MCP tools (filesystem, git, …)
```

Each external MCP tool is backed by an AI agent. The bridge handles the parts everyone re-invents:

- **Sessions** — spawn, pool, health-monitor, and recover crashed agents
- **Signaling** — block the external caller until the agent signals a structured reply
- **Configuration** — tool definitions, prompts, signaling contracts, session modes, timeouts — all in one JSON file, hot-reloaded
- **Monitoring** — live dashboard of active sessions, activity logs, and a Config Agent that edits the configuration conversationally

---

## Projects

| Project | Purpose |
|---|---|
| `Praetorium.Bridge` | Core library: session lifecycle, signaling, tool dispatch, MCP server builder |
| `Praetorium.Bridge.CopilotProvider` | `IAgentProvider` implementation backed by the GitHub Copilot SDK |
| `Praetorium.Bridge.Web` | ASP.NET Core + Blazor Server dashboard, configuration/prompt editor, SignalR session monitoring, Config Agent |

---

## Getting started

### Prerequisites

- .NET 10 SDK
- Access to a GitHub Copilot–compatible endpoint (the bundled provider uses the [GitHub Copilot SDK](https://github.com/github/copilot-sdk))

### Build and run

```bash
# Build
dotnet build

# Run the web app (dashboard + MCP HTTP endpoint on port 5100)
dotnet run --project src/Praetorium.Bridge.Web
```

Open <http://localhost:5100> for the dashboard. The MCP HTTP endpoint is served under the `basePath` defined in `praetorium-bridge.json` (default `/mcp`).

### Point an MCP client at the bridge

Configure your MCP caller (Claude Desktop, Copilot, or another bridge) to connect to:

```
http://localhost:5100/mcp
```

Tools defined in `praetorium-bridge.json` appear automatically. Adding or removing a tool in the JSON (or via the dashboard) is picked up without a restart.

---

## Configuration

Everything is declared in `praetorium-bridge.json` at the repo root. A JSON Schema (`praetorium-bridge.schema.json`) is shipped alongside it so editors can validate and autocomplete.

Top-level sections:

| Section | Purpose |
|---|---|
| `server` | HTTP port, base path, bind address |
| `defaults` | Fallback agent, session, and signaling config inherited by every tool |
| `agentToolSources` | Named MCP tool sources (stdio command or HTTP URL) that agents can use |
| `tools` | The external MCP tools exposed to callers, each mapped to an agent + prompt + signaling contract |

### Session modes

| Mode | Behavior |
|---|---|
| `per-connection` | A new session per MCP transport connection (default) |
| `per-reference` | Sessions are pooled and reused by a caller-supplied reference ID |
| `global` | A single shared session for all callers |

### Prompt templates

Prompts live as Markdown files under `prompts/`. They support `{{PLACEHOLDER}}`, `{{#if}}`, `{{#each}}`, and `{{ENV:VAR}}` substitutions and are validated on load. The dashboard's prompt editor highlights unknown placeholders.

See `praetorium-bridge-design.md` (1100+ lines) for the full design reference.

---

## Dashboard features

Served from the web project at `/`.

- **Live dashboard** — active sessions, spawn/crash events, per-tool counters
- **Activity log** — streaming view of tool calls and agent events via SignalR
- **Configuration editor** — create/edit/delete tools, agent sources, and defaults with a form UI backed by the JSON schema
- **Prompt editor** — edit prompt templates with placeholder detection and inline parameter insertion
- **Signaling tool editor** — wire up default and custom signaling contracts per tool
- **Config Agent** — chat with an AI agent that stages configuration changes for you; diffs are shown before you apply them

---

## Repository layout

```
praetorium-bridge/
├── praetorium-bridge.json         # Runtime configuration
├── praetorium-bridge.schema.json  # JSON schema for the config
├── praetorium-bridge-design.md    # Full design document
├── prompts/                       # Markdown prompt templates
└── src/
    ├── Praetorium.Bridge/                # Core library
    ├── Praetorium.Bridge.CopilotProvider/ # Copilot-backed IAgentProvider
    └── Praetorium.Bridge.Web/            # Blazor dashboard + MCP HTTP host
```

---

## Extension points

The core library is built around a handful of abstractions you can replace:

| Interface | Default | Replace to… |
|---|---|---|
| `IAgentProvider` | `CopilotProvider` | plug in a different LLM backend |
| `IAgentSession` | provider-specific | customize per-session behavior |
| `ISessionStore` | in-memory | persist sessions across restarts |
| `ISignalRegistry` | `ConcurrentDictionary` | change how agent↔bridge signals are routed |
| `IConfigurationProvider` | file-watched JSON | source config from a database, KMS, etc. |
| `IBridgeHooks` | no-op | observe 11 lifecycle events (spawned, pooled, crashed, input_requested, …) |

---

## Roadmap / future work

Short list of things on the radar for `0.2+`:

- **Additional `IAgentProvider` implementations** beyond GitHub Copilot — Anthropic, OpenAI, Azure OpenAI, local models
- **Test projects** — the repo currently ships no unit/integration tests
- **Linting / formatting** — add an `.editorconfig` + analyzer package set
- **Authentication** — dashboard is currently unauthenticated; add SSO / API-key auth
- **Remote API access** — expose a read/write HTTP API for configuration so the dashboard can be detached from the bridge host
- **Docker & deployment** — Dockerfile, compose example, Helm chart
- **Enhanced hot-reload** — currently JSON-driven; extend to prompt-only and signaling-only reloads without replaying sessions
- **Streaming improvements** — partial/streamed tool results from agents back to callers
- **Persistent session store** — Redis/SQL-backed `ISessionStore` for multi-node deployments
- **Crash-replay** — replay a crashed session's inbound calls against a freshly spawned session

Contributions, bug reports, and design feedback are welcome.

---

## License

See `LICENSE.txt`.
