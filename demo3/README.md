# Demo 3: Iterative Feedback Loop

## Overview

This demo demonstrates **cyclic workflows** in the Microsoft Agent Framework, implementing an iterative feedback loop pattern where content goes through multiple revision cycles until approved.

## Scenario

A content creation workflow with quality control:

1. **WriterAgent** generates an initial draft
2. **ReviewerAgent** reviews the draft
3. If review says "Needs Revision" → loop back to WriterAgent (cyclic edge)
4. If review says "Approved" → proceed to PublisherAgent (terminal)
5. **PublisherAgent** formats and publishes the final approved content

## Key Concepts Demonstrated

### ✅ Cyclic Edges
- Created using standard `AddEdge(reviewer, writer)` - no special API needed
- Forms a feedback loop in the workflow graph

### ✅ Conditional Routing with AddSwitch
- Uses `AddSwitch` with predicate-based routing
- Routes based on the reviewer's decision field

### ✅ Stateful Executors
- **WriterAgent** maintains `_lastDraft` and `_revisionCount` across invocations
- **ReviewerAgent** tracks `_iterationCount` for termination logic
- Executors are instantiated once and preserve state throughout the workflow

### ✅ Loop Termination
- Safety mechanism: `MAX_ITERATIONS` prevents infinite loops
- Conditional logic determines when to exit the loop

### ✅ Tuple Return Types
- ReviewerAgent returns `(string content, string decision)` tuple
- Enables routing based on specific fields (decision) while passing data (content)

## Architecture

```
User Prompt
    ↓
WriterAgent (v1)
    ↓
ReviewerAgent
    ↓
    ├─ "Needs Revision" → FeedbackAdapter → WriterAgent (v2)  (Cyclic Path)
    │                           ↑                 ↓
    │                           └─────────────────┘
    │
    └─ "Approved" → PublisherAgent (Terminal)
```

**Key Design Decision:** The `FeedbackAdapterAgent` is required because of MAF's strong typing. The ReviewerAgent returns `(string content, string decision)` tuple, but WriterAgent expects `string` input. The adapter extracts just the content string to maintain type consistency in the cycle.

## Code Structure

### WriterAgent

```csharp
class WriterAgent : Executor<string, string>
```

- **Input:** Initial prompt (first time) or feedback (revisions)
- **Output:** Draft content
- **State:** `_lastDraft`, `_revisionCount`
- **Logic:** Generate initial draft or revise based on feedback

### ReviewerAgent

```csharp
class ReviewerAgent : Executor<string, (string content, string decision)>
```

- **Input:** Draft content
- **Output:** Tuple of (content/feedback, decision)
- **State:** `_iterationCount`
- **Logic:**
  - Iteration 1: "Needs Revision" → loops back
  - Iteration 2+: "Approved" → proceeds to publisher
  - Safety: Force approval at MAX_ITERATIONS

### FeedbackAdapterAgent

```csharp
class FeedbackAdapterAgent : Executor<(string content, string decision), string>
```

- **Input:** Tuple from reviewer `(content, decision)`
- **Output:** Just the content string
- **Purpose:** Type adapter to convert tuple → string for WriterAgent
- **Why needed:** MAF's strong typing requires input/output type consistency

### PublisherAgent

```csharp
class PublisherAgent : Executor<(string content, string decision), string>
```

- **Input:** Tuple from reviewer
- **Output:** Formatted published content
- **Logic:** Format final output for publication

## Workflow Builder

```csharp
var workflow = new WorkflowBuilder(writer)
    .AddEdge(writer, reviewer)
    // Conditional branching based on reviewer decision
    .AddSwitch(reviewer, switchBuilder =>
        switchBuilder
            .AddCase<(string content, string decision)>(
                result => result.decision == "Needs Revision",
                feedbackAdapter)  // Route to adapter first
            .AddCase<(string content, string decision)>(
                result => result.decision == "Approved",
                publisher)  // Terminal path
    )
    .AddEdge(feedbackAdapter, writer)  // Cyclic edge: adapter → writer
    .WithOutputFrom(publisher)
    .Build();
```

**Architecture Note:** The cyclic edge is `feedbackAdapter → writer`, not directly `reviewer → writer`. This is because:

1. ReviewerAgent outputs: `(string, string)` tuple
2. WriterAgent expects: `string` input
3. FeedbackAdapterAgent bridges the type gap: `(string, string) → string`

This demonstrates MAF's **strong type safety** - you cannot directly connect executors with incompatible types.

## Expected Output

```
=== Demo 3: Iterative Feedback Loop ===

--- Workflow Execution ---

[Writer v1] Generated initial draft:
  "CodeFlow is a new code editor. It uses AI. It helps you code."

[Reviewer - Iteration 1] Reviewing content...
  Decision: NEEDS REVISION
  Feedback: Needs more detail and excitement. Make it compelling!

[Writer v2] Revised draft based on feedback:
  "CodeFlow is an innovative AI-powered code editor..."

[Reviewer - Iteration 2] Reviewing content...
  Decision: APPROVED

[Publisher] Publishing approved content...

[Final Output]
✓ PUBLISHED ✓

[Final approved content here]

--- Published on 2025-12-07 14:30 ---

=== Workflow Complete ===
```

## Key Learnings

### 1. Cycles Are First-Class Citizens
MAF explicitly supports cyclic graphs. You don't need special APIs or workarounds.

### 2. State Management Pattern
- Executors persist across invocations
- Use private fields to maintain state
- Thread safety considerations for production (not covered in this demo)

### 3. Loop Termination Is Your Responsibility
- Framework does **not** automatically detect or prevent infinite loops
- Always implement explicit termination logic
- Use max iteration counters as safety nets

### 4. Data Flow in Loops
- First invocation: Input from workflow start
- Subsequent invocations: Input from previous node in the cycle
- Type must match across iterations (string → string in Writer's case)

### 5. Multiple Output Strategies

- `context.YieldOutputAsync()`: Streams intermediate steps (visible during execution)
- `WithOutputFrom(publisher)`: Defines final workflow output (auto-yields return value)
- Both can be used together for observability

### 6. Type Adapter Pattern (⭐ Critical)

- **Problem:** Executor A outputs `TypeX`, Executor B expects `TypeY` as input
- **Solution:** Insert an adapter executor: `A → Adapter(TypeX → TypeY) → B`
- **Demo 3 Example:** `Reviewer → FeedbackAdapter(tuple → string) → Writer`
- **Why:** MAF enforces compile-time type safety - incompatible types cannot be directly connected
- **Best Practice:** Keep adapters simple and stateless (pure transformation)

## Differences from Demo 2

| Aspect | Demo 2 | Demo 3 |
|--------|--------|--------|
| **Graph Type** | DAG (Directed Acyclic Graph) | Cyclic Graph |
| **Routing** | AddSwitch for branching | AddSwitch for loop exit |
| **State** | Aggregator collects parallel results | Agents track revision state |
| **Termination** | Natural (all paths end) | Explicit (max iterations) |
| **Pattern** | Fan-out → Aggregate → Branch | Sequential → Loop → Terminate |

## Running the Demo

```bash
cd demo3
dotnet build
dotnet run
```

## Related Official Documentation

- [Workflows Overview](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/overview)
- [Cyclic Workflow Example - Request/Response Pattern](https://learn.microsoft.com/en-us/agent-framework/tutorials/workflows/requests-and-responses)
- [Conditional Edges](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/core-concepts/edges)
- [Executors](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/core-concepts/executors)

## Next Steps

**Demo 4** will introduce:
- Human-in-the-Loop (wait for external approval)
- Checkpointing and State Persistence (resume workflows after restart)
- Durable execution patterns
