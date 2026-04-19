# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run the web app (serves on port 5100)
dotnet run --project src/Praetorium.Bridge.Web

# Build a specific project
dotnet build src/Praetorium.Bridge
```

No test projects or linting configuration currently exist.

## Architecture

**Praetorium.Bridge** is a .NET 10 bridge that exposes external MCP tool calls over HTTP, routes them to AI agent sessions, and provides a Blazor dashboard for monitoring and configuration.

### Request Flow

```
External MCP Caller → HTTP → Bridge MCP Server → Tool Dispatcher →
Session Manager → Agent Provider → AI Agent Session → Tools (filesystem, git, code-search)
```

### Project Structure

| Project | Purpose |
|---|---|
| `Praetorium.Bridge` | Core library: session lifecycle, signaling, tool dispatch, MCP server builder |
| `Praetorium.Bridge.CopilotProvider` | Implements `IAgentProvider` using the GitHub Copilot SDK |
| `Praetorium.Bridge.Web` | ASP.NET Core + Blazor Server dashboard; config/prompt editor; SignalR session monitoring |

### Core Abstractions (in `Praetorium.Bridge`)

- **`IAgentProvider`** — provider-agnostic agent spawning; `CopilotProvider` is the only implementation
- **`IAgentSession`** — wraps a running agent; health-monitored via MS Agent Framework
- **`ISessionStore`** — pluggable session persistence; default is in-memory
- **`ISignalRegistry`** — async wait/signal for agent↔bridge communication (ConcurrentDictionary-backed)
- **`IConfigurationProvider`** — pluggable config source with hot-reload events; default reads `praetorium-bridge.json`
- **`IBridgeHooks`** — 11 event hooks (spawned, pooled, crashed, input_requested, etc.) awaited with timeout

### Key Design Patterns

- **Configuration-driven**: all behavior (tool definitions, session modes, agent tool sources, signaling tools, prompt paths, timeouts) lives in `praetorium-bridge.json`
- **Hot-reload**: a file watcher re-reads config and re-registers MCP tools on the fly; removed tools survive until their last session disconnects
- **Session pooling**: sessions can be `per-connection`, `per-reference`, or `global`; crash recovery and orphan detection are built in
- **Signaling tools**: local MCP tools implemented as closures (`SignalingToolFactory`) that block until the agent signals back
- **Prompt templating**: Markdown files with `{{PLACEHOLDER}}`, `{{#if}}`, `{{#each}}`, `{{ENV:VAR}}` processed by `PlaceholderEngine`; validated on load

### Configuration Schema

`praetorium-bridge.json` at the repo root is the primary config file. Its JSON Schema is embedded in `src/Praetorium.Bridge/Configuration/`. See `praetorium-bridge-design.md` (1100+ lines) for the full design rationale and configuration reference.
