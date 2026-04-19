# Copilot Instructions

## General Guidelines
- This is a **greenfield** project. Do not add backwards-compatibility shims, migration bridges, or deprecation paths unless explicitly requested.
- When a restart or hot-reload is needed to apply code changes, ask the user to do it instead of attempting workarounds (like loading SQLite DLLs from NuGet cache to directly modify the database).
- Fix all build errors encountered, even if they were not caused by the current change. Do not leave the codebase in a broken state.

## Debugging Notes

**Exit code 0xffffffff (-1):** This exit code appears when:
- The application is stopped via the debugger (Stop Debugging button)
- The application is terminated without graceful shutdown

This is **NOT a crash** - it's expected behavior when debugging is interrupted manually.

## Hard Rules (No Exceptions)

1. Never guess **business logic, architecture, or behavioral outcomes**.
   If behavior, values, formats, or domain rules are unknown → fail fast in code or ask.
   **Safe inferences from existing code** (naming conventions, patterns, style) are allowed when the codebase provides clear precedent.

2. No assumptions about **requirements, APIs, or configuration**.
   If business requirements, external APIs, or configuration schemas are unclear → ask or fail fast.
   **Inferring implementation patterns** from existing sibling code in the same project is allowed.

3. Deterministic behavior.
   Same inputs must produce the same outputs.
   Do not introduce randomness, environment-dependent branching, implicit caching, or time-based logic unless explicitly requested.

4. No magic numbers.
   If a literal value is required, define a named constant and document where that value comes from.

5. Maintain unit-testability.
   Avoid hidden global state, mutable static state, implicit I/O, sleeps, or side effects in core logic.

6. Input validation is required.
   Reject invalid or inconsistent states immediately with a clear diagnostic message.

7. No silent fallbacks.
   Do not change libraries, implementations, or patterns automatically.
   If something is missing, conflicting, or impossible → fail fast or ask.

8. Minimal output.
   Provide only the minimal necessary edits or the minimal full file needed to compile.
   No placeholders, stubs, or auto-filled logic unless explicitly requested.

9. If Copilot cannot pause to ask (for example in inline suggestions):
   It must avoid guess-based behavior by generating fail-fast behavior instead of assumptions.

10. Do not generate new documentation files or examples unless explicitly asked.
    Always update existing documentation if needed.

11. No unsolicited examples.
    Do not create example files, usage demonstrations, or sample code unless the user explicitly requests them.
    When implementing features, provide only the production code, tests, and essential documentation updates.

12. No additional documentation.
    Do not create README files, guides, tutorials, or supplementary documentation unless explicitly requested.
    Only update existing documentation when changes directly impact documented behavior.

## Documentation Guidelines

### When User Requests Documentation

**Diagrams:**
- **ALWAYS use Mermaid syntax** for all diagrams (flowcharts, sequence diagrams, architecture diagrams, etc.)
- **NEVER use ASCII art, text-based diagrams, or external diagram tools**
- Use appropriate Mermaid diagram types:
  - `graph TD` or `graph LR` for architecture/flow diagrams
  - `sequenceDiagram` for interaction flows
  - `classDiagram` for class relationships
  - `stateDiagram-v2` for state machines
  - `erDiagram` for data models

**Documentation Updates:**
- Prefer updating existing documentation over creating new files (Rule 10)
- If new documentation is needed, ask for explicit approval first
- Use markdown with proper headings, code blocks, and inline code formatting
- Keep diagrams inline using Mermaid code blocks

## General Coding Guidelines

- Prefer configuration or option objects instead of long parameter lists.
- Validate configuration at the boundary before using it.
- If a behavior is unspecified, unclear, or contradictory:
  - Document what detail is missing
  - Fail fast where that detail is required

### Prefer Objects Over IDs

**Use typed objects instead of string/Guid IDs for method parameters and return types.**

| Prefer | Avoid |
|--------|-------|
| `void RemovePolicy(PolicyInfo policy)` | `void RemovePolicy(string policyId)` |
| `Task<PolicySetDetails> Clone(PolicySet source)` | `Task<PolicySetDetails> Clone(string sourceId)` |
| `IReadOnlyList<PolicyInfo> Policies { get; }` | `IReadOnlyList<string> PolicyIds { get; }` |

**When to use IDs:**
- Database persistence layer (entity IDs in tables)
- URL routing and query parameters
- Cross-process communication (APIs, serialization boundaries)
- User-facing identifiers (display, logs)

**When to use Objects:**
- Method parameters in service/domain layer
- ViewModel properties and bindings
- Collections that will be iterated/displayed
- Anything that needs the object's properties shortly after

**Rationale:**
- Type safety: Compiler catches errors, not runtime
- No "lookup" code: Avoid repetitive `repository.GetById(id)` calls
- Discoverability: IDE shows available properties and methods
- Testability: No need to mock repositories just to resolve IDs

**Example - Bad:** public Task RemovePolicyAsync(string policySetId, string policyName, PolicyType type);
// Caller must know IDs, method must look up objects internally
**Example - Good:** public Task RemovePolicyAsync(PolicySetDetails policySet, PolicyInfo policy);
// Caller has objects, method operates directly on them

### C# Notes
- Prefer constructor injection and avoid mutable static state.
- Use ArgumentException or InvalidOperationException for fail-fast behavior.
- In async code, use async/await and propagate CancellationToken where applicable.
- Use readonly fields for immutable state.
- Respect and maintain nullable reference type annotations if present.

## Testing Guidelines

### Performance Testing
- **ALWAYS use `System.Diagnostics.Stopwatch`** for performance measurements
- **NEVER use `DateTime.Now` or `DateTime.UtcNow`** for measuring elapsed time
  - DateTime has insufficient precision (typically 10-16ms resolution)
  - Stopwatch provides high-precision timing (sub-microsecond on most hardware)
- Performance test pattern:// Warmup phase (exclude from measurements)
for (int i = 0; i < warmupIterations; i++)
{
    // Execute operation
}

// Measurement phase
var sw = Stopwatch.StartNew();
for (int i = 0; i < measurementIterations; i++)
{
    // Execute operation
}
sw.Stop();

// Calculate average
var avgMicroseconds = (sw.Elapsed.TotalMilliseconds * 1000) / measurementIterations;- Always include warmup phase to exclude JIT compilation and cache initialization
- Use sufficient iterations to average out system noise (1000+ for microsecond-scale operations)
- Document expected performance targets in test comments

### General Testing Rules
- Test names should clearly describe what is being tested and expected outcome
- Use AAA pattern: Arrange, Act, Assert
- Each test should verify one specific behavior
- Use deterministic test data (no random values unless testing randomness)
- Mock external dependencies (files, databases, networks)
- Tests must be isolated and order-independent

### MCP Tool Description Rules
- **No duplication between tool `[Description]` and parameter `[Description]`.**  
  Each piece of information should appear exactly once — either on the tool or on the parameter, not both.  
  Duplicated text wastes tokens for every LLM call.
- Tool-level descriptions: purpose, behavioral semantics, supported modes (batch, help).
- Parameter-level descriptions: what this parameter is, valid values, constraints specific to this parameter.
- If a valid-values list (e.g., link types) fits naturally on the parameter, keep it there only.
- Keep descriptions concise — omit "Returns X as JSON", "Set to true to…", parameter name echoing, and other boilerplate the LLM can infer from the schema.

## Document Creation Guidelines
- When the user says "draft a document" or "create a md document," they mean a real file in the workspace (using `create_file`), NOT a workflow document (using `workflow_doc_create`). 
- Workflow documents are for project knowledge management. Standalone files are for content the user wants to manage independently, especially content intended for separate repositories