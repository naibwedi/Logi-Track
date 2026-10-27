# Universal Token Efficiency & Model Routing Rules

## Behavioral Invariants

1. **Subagent Offloading for Research**:
   - Never dump raw multi-file grep or file search results directly into the main conversation.
   - Always delegate broad exploration, repository searches, and log analysis to a background subagent (using `flash` or `flash_lite`) so that only the distilled conclusion or precise diff enters the main context.

2. **Main Model Advisory (Argon, Claude Opus, Flash)**:
   - When the user is tackling complex architectural changes, intricate multi-file refactoring, or elusive debugging, recommend switching the UI model selector to flagship reasoning models (e.g., Gemini Argon, Claude Opus).
   - When shifting to routine implementation, unit testing, or boilerplate, recommend stepping down to Gemini Flash to conserve tokens and cost.

3. **Context Hygiene**:
   - Use line-bounded reading (`StartLine`/`EndLine`) when examining files over 100 lines.
   - Proactively suggest `/compact` when the conversation history grows long.
