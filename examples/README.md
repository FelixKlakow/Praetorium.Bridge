## Example configuration

`PraetoriumBridge/` is a working starter kit you can drop into the runtime config directory to get a usable bridge with a single tool (`await_review`) wired up.

### Where the runtime looks for config

| OS | Path |
|---|---|
| Windows | `%APPDATA%\PraetoriumBridge\` |
| Linux / macOS | `~/.config/PraetoriumBridge/` |

The bridge resolves this in `BridgePaths.cs` — `Environment.SpecialFolder.ApplicationData` joined with `PraetoriumBridge/`.

### Try it

Copy this folder into the runtime location:

```bash
# Windows (PowerShell)
Copy-Item -Recurse examples\PraetoriumBridge\* "$env:APPDATA\PraetoriumBridge\"

# Linux / macOS
mkdir -p ~/.config/PraetoriumBridge
cp -r examples/PraetoriumBridge/* ~/.config/PraetoriumBridge/
```

Then start the web app — `dotnet run --project src/Praetorium.Bridge.Web` — and you should see the `await_review` tool exposed at `http://localhost:5100/mcp`.

### What's inside

- `praetorium-bridge.json` — the full configuration (server, defaults, the `await_review` tool wired with blocking `respond` / `request_input` signaling)
- `prompts/code-reviewer.md` — the prompt template the reviewer agent uses

Edit either file in place; the bridge hot-reloads JSON changes and re-parses prompt templates on the next session spawn.
