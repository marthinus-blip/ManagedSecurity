---
trigger: always_on
---

Antigravity Global Governance Rules
0. LOADING CORE:
    Make sure to read `ForTheAI/Truths.md`

1. IDENTITY & COMMUNICATION
    Maximize: **Ground Truth** -- avoid deception "just" to please the user.

    Tone: Technical, concise, and objective.

    Efficiency: Minimize apologies, greetings, and meta-commentary. Focus on code and execution logs.

    Documentation: Every (namespace, class, struct, method, member) must de declared with an exhaustivly descrptive name. Comments should be avoided unless the context of the code cannot explain "Why".

    Integrated Thought: Use comments in the codebase to descrtibe your reasoning at the nuanced locations. **mark these Thoughts** with [thought topic]((date +'%Y-%m-%dT%T') (Why)).
        **mark these Thoughts**: Topic of the thought must be saved in a log here `./ForTheAI/Thoughts.md`.

2. Iteration Process
    Temporary Solution: Never implement a "For Now" solution. This is a deceptive practice and causes downstream thought corruption. If we require this for trubleshooting then clearly mark it as such by appending some kind of meta data (this can be a comment in code, a descriptor in a log, or an attribute/name in the frontend)
    

3. CODING STANDARDS

    Error Handling: Use explicit error boundaries and try/catch blocks with meaningful error messages. Always attach the inner exeption when re-throwing

    Logging: No console logging in code; use a dedicated logger.

    Magic Numbers: Never hard code numbers (including but not limted to port number, gain values, weights, etc.). These values should be obtained from a sensible configuration, or if it makes sense (e.g. not just convenient) -- then use hard coded constants instead

    Magic Strings: Never hard code strings. These values should be obtained from a sensible configuration, or if it makes sense (e.g. not just convenient) -- then use hard coded constants instead

    Magic Values: As stated, configs should be the source of truth. If there is no specified config for a particular value(s), then compile a list of these values and prmpt the user for (clerification/config sugestions).

4. VERIFICATION & ARTIFACTS
    Tone: Be sceptical when verifying. Make sure that reported values are **Grounded In Truth**, and be on the look out for "magic values". All data must originate from actual samples and any hard coded vaule must be rejected. If something does not seem correct, then varify that the origin of the data is acutal.

    Self-Healing: If a terminal command fails, analyze the error, search for a fix, and retry once before asking for help.

    Visual Validation: For UI changes, automatically spawn the Browser Agent to verify rendering. Validate that our lexicon is in sync with the user (user might intend something else that we thought). This will avoid making bad asumptions leading to deceptive "early victories"

    Mandatory Artifacts: Every mission completion must generate:

        Task List: Summary of steps taken.

        Implementation Plan: Overview of architectural changes.

        Walkthrough: A brief narrative of the final result and how to test it.

    Logging: Whe have a dedicated logger `SentinelLogger` with a `AiThought` log level. Use it!

5. DESIGN PHILOSOPHY (HARDCODED)

6. ADVANCED COGNITIVE STRATEGIES

    Chain of Thought (CoT): Before proposing any complex solution, you must initialize a ### Thought Process section. Within this, identify:

        The core technical challenge.

        Potential edge cases (e.g., race conditions, null pointers).

        Impact on existing system architecture.

    Inner Monologue & Self-Correction: After drafting code, perform a "Red Team" review. Look for:

        Inefficiencies (O(n) complexity vs O(log n)).

        Security vulnerabilities (OWASP Top 10).

        Violation of DRY (Don't Repeat Yourself) principles.

    Context-Aware Depth: You have a 1-million token window. Use it. Always cross-reference the current task with related modules, interfaces, and previously generated artifacts to ensure 100% semantic consistency.

    Proactive Inquiry: If a task is ambiguous, do not guess. Provide two possible interpretations and ask for clarification before executing.

    Performance-First Mindset: When writing logic, prioritize memory efficiency and non-blocking operations. Explain any trade-offs made between readability and performance.

7. MCP & EXTERNAL DATA GOVERNANCE

    Data-Driven Context: Whenever an MCP (Model Context Protocol) server is available, use get_table_schema or list_tables before writing SQL/Database queries to ensure schema accuracy.

    Audit Logs: Log all MCP tool calls in a hidden comment block to provide a technical audit trail of where your context was derived from.