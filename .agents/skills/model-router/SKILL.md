---
name: model-router
description: Optimizes token usage and cost by routing subtasks to appropriate subagent tiers (flash_lite, flash, pro) and advising when to switch main UI models (Argon, Claude Opus, Flash).
---

# Token Efficiency & Model Routing Skill

This skill governs how tasks are triaged and delegated to maximize token efficiency, reduce cost, and keep latency low across both UI model selection and background subagents.

## 1. Main UI Model Guidance (Argon, Claude Opus, Flash)

The user selects their primary model via the UI selector. The agent monitors task complexity and proactively advises when to switch:

- **Flagship / Deep Reasoning (Gemini Argon, Claude Opus, Gemini Pro)**:
  - Advise switching to these models when tackling:
    - Large-scale architectural decisions and system design.
    - Highly subtle bug diagnostics, race conditions, or complex refactorings spanning multiple coupled files.
    - Security-sensitive logic and critical algorithm design.
- **High Speed / Cost-Effective (Gemini Flash, Flash-Lite)**:
  - Advise stepping down to Flash when:
    - Implementing straightforward features, CRUD logic, or standard edits.
    - Writing unit tests, boilerplate, documentation, or regex.
    - Routine Q&A and interactive code reviews.

## 2. Background Subagent Offloading (Zero Context Bloat)

Regardless of the active model in the main UI thread (even when running flagship models like Argon or Claude Opus):

1. **Lightweight Tier (`flash_lite`)**:
   - Short snippet transformations, docstrings, schema boilerplate.
   - Low-complexity unit test skeletons.
2. **Standard Exploration Tier (`flash`)**:
   - Codebase search, repository grep, and directory reconnaissance (`research` subagent).
   - Log parsing and build output analysis.
   - Isolating bulk raw file reads and dumps away from the primary conversation context.
3. **Synthesis**:
   - Subagents must return distilled summaries or targeted diffs rather than entire file dumps.

## 3. Strict Context Hygiene

- **Targeted File Reads**: Use line-bounded reads (`StartLine`/`EndLine`) instead of ingesting entire multi-hundred-line files into the main chat context.
- **Compaction Reminder**: Recommend `/compact` after reaching major milestones in long-running threads.
