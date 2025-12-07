# Research: Iterative Feedback Loops - December 7, 2025

## Context
**Requested by:** User
**Target:** demo3
**Goal:** Demonstrate how to implement iterative feedback loops with the Writer-Reviewer-Publisher pattern in MAF

## Key Findings

### 1. Framework Concept: Cyclic Edges in Workflows

**Class/Interface:** `WorkflowBuilder.AddEdge(source, target)`

**Purpose:** MAF **explicitly supports cyclic graphs**. You can add edges that create loops by simply connecting nodes back to previous executors.

**Pattern:** Found in official Microsoft documentation for Request/Response workflow:

```csharp
// From: https://learn.microsoft.com/en-us/agent-framework/tutorials/workflows/requests-and-responses
internal static class WorkflowHelper
{
    internal static ValueTask<Workflow<NumberSignal>> GetWorkflowAsync()
    {
        RequestPort numberRequestPort = RequestPort.Create<NumberSignal, int>("GuessNumber");
        JudgeExecutor judgeExecutor = new(42);

        // Build the workflow by connecting executors in a loop
        return new WorkflowBuilder(numberRequestPort)
            .AddEdge(numberRequestPort, judgeExecutor)
            .AddEdge(judgeExecutor, numberRequestPort)  // <-- CYCLIC EDGE!
            .WithOutputFrom(judgeExecutor)
            .BuildAsync<NumberSignal>();
    }
}
```

**Key Insight:** 
- Cyclic edges are created just like regular edges: `AddEdge(reviewer, writer)` creates a feedback loop.
- No special API is needed for cycles.

### 2. Loop Termination Pattern

**Where to Store State:** In the executor that makes the termination decision (typically the "Router" or "Reviewer").

**Pattern:** Use internal state within executors to track iteration count and make routing decisions:

```csharp
class ReviewerAgent : Executor<string, (string content, string decision)>
{
    private int _iterationCount = 0;
    private const int MAX_ITERATIONS = 3;

    public override async ValueTask<(string, string)> HandleAsync(
        string input, 
        IWorkflowContext context, 
        CancellationToken cancellationToken = default)
    {
        _iterationCount++;
        
        // Simulated review logic
        bool needsRevision = _iterationCount < 2; // Revise first time, approve second
        
        if (_iterationCount >= MAX_ITERATIONS)
        {
            // Force approval if max iterations reached
            return (input, "Approved");
        }
        
        if (needsRevision)
        {
            return (input, "Needs Revision");
        }
        
        return (input, "Approved");
    }
}
```

### 3. Conditional Routing for Loops

**Framework Support:** MAF supports conditional edges using predicates.

**Implementation Pattern:**

```csharp
// Option 1: Using conditional edges
builder.AddEdge(reviewer, writer, 
    condition: result => result.decision == "Needs Revision");
    
builder.AddEdge(reviewer, publisher, 
    condition: result => result.decision == "Approved");
```

**Alternative: Using AddSwitch (Recommended):**

```csharp
builder.AddSwitch(reviewer, switchBuilder =>
    switchBuilder
        .AddCase<(string content, string decision)>(
            result => result.decision == "Needs Revision",
            writer)
        .AddCase<(string content, string decision)>(
            result => result.decision == "Approved",
            publisher)
);
```

### 4. Writer Agent State Management

**Challenge:** When WriterAgent receives feedback, it needs context about what to revise.

**Pattern from demo2:** Stateful executors manage their own state across invocations:

```csharp
class WriterAgent : Executor<string, string>
{
    private string? _lastDraft = null;
    private int _revisionCount = 0;

    public override async ValueTask<string> HandleAsync(
        string input, 
        IWorkflowContext context, 
        CancellationToken cancellationToken = default)
    {
        if (_lastDraft == null)
        {
            // Initial draft
            _lastDraft = GenerateInitialDraft(input);
            Console.WriteLine($"Writer: Created initial draft (v{++_revisionCount})");
        }
        else
        {
            // Revision based on feedback
            _lastDraft = ReviseDraft(_lastDraft, input);
            Console.WriteLine($"Writer: Revised draft (v{++_revisionCount})");
        }
        
        await context.YieldOutputAsync($"Draft v{_revisionCount}: {_lastDraft}");
        return _lastDraft;
    }
}
```

### 5. Complete Workflow Graph Structure

**Architecture Decision:** 
- **Start Node:** WriterAgent (receives initial prompt)
- **Feedback Loop:** Writer → Reviewer → (conditional) → Writer
- **Terminal Node:** Publisher (when approved)

**Workflow Builder Pattern:**

```csharp
var workflow = new WorkflowBuilder(writer)
    .AddEdge(writer, reviewer)
    .AddSwitch(reviewer, switchBuilder =>
        switchBuilder
            .AddCase<(string content, string decision)>(
                result => result.decision == "Needs Revision",
                writer)  // Cyclic edge back to writer
            .AddCase<(string content, string decision)>(
                result => result.decision == "Approved",
                publisher)  // Terminal path
    )
    .WithOutputFrom(publisher)
    .Build();
```

## Constraints & Gotchas

### ⚠️ Infinite Loop Prevention
- **Must implement:** Termination logic in the reviewer (e.g., max iterations counter)
- **Framework limitation:** MAF does not automatically detect or prevent infinite loops
- **Best practice:** Always have a max iteration count as a safety net

### ⚠️ State Management
- **Pattern:** Executors are instantiated once and maintain state across invocations
- **Consideration:** In multi-threaded scenarios, state should be thread-safe
- **Current scope:** Demo uses in-memory state (sufficient for proof-of-concept)

### ⚠️ Data Flow in Cycles
- **First invocation:** Writer receives the initial user prompt (string)
- **Subsequent invocations:** Writer receives feedback from reviewer (also string in this design)
- **Type consistency:** Input type must match across loop iterations

### ⚠️ Output Streaming
- Use `context.YieldOutputAsync()` to show intermediate steps (drafts, reviews)
- Use `WithOutputFrom(publisher)` to specify final workflow output
- Each iteration through the loop can produce observable events

## Recommendations for Implementation

### Architecture Decision: Router Agent Not Required

MAF's `AddSwitch` with predicates provides built-in routing. No separate "Router" agent is needed.

### Code Changes Required

#### 1. New Agent Classes
- **WriterAgent**: `Executor<string, string>`
  - Tracks revision count
  - Generates initial draft or revises based on feedback
  - State: `_lastDraft`, `_revisionCount`

- **ReviewerAgent**: `Executor<string, (string content, string decision)>`
  - Tracks iteration count
  - Returns tuple: (content, decision)
  - Decision: "Needs Revision" or "Approved"
  - Safety: Force approval after MAX_ITERATIONS

- **PublisherAgent**: `Executor<(string content, string decision), string>`
  - Terminal node
  - Formats final published output
  - Returns value auto-yielded by `WithOutputFrom()`

#### 2. Workflow Structure

```csharp
var writer = new WriterAgent();
var reviewer = new ReviewerAgent();
var publisher = new PublisherAgent();

var workflow = new WorkflowBuilder(writer)
    .AddEdge(writer, reviewer)
    .AddSwitch(reviewer, switchBuilder =>
        switchBuilder
            .AddCase<(string content, string decision)>(
                result => result.decision == "Needs Revision",
                writer)
            .AddCase<(string content, string decision)>(
                result => result.decision == "Approved",
                publisher)
    )
    .WithOutputFrom(publisher)
    .Build();
```

#### 3. Execution Pattern (same as demo1/demo2)

```csharp
StreamingRun run = await InProcessExecution.StreamAsync(
    workflow, 
    "Write a product announcement for a new AI-powered code editor");
    
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

await foreach (var evt in run.WatchStreamAsync().ConfigureAwait(false))
{
    if (evt is WorkflowOutputEvent outputEvent)
    {
        Console.WriteLine($"[Event] {outputEvent.Data}");
    }
}
```

### Expected Execution Flow

1. **Initial:** User prompt → Writer → Draft v1
2. **Loop 1:** Draft v1 → Reviewer → "Needs Revision" → Writer → Draft v2
3. **Loop 2:** Draft v2 → Reviewer → "Approved" → Publisher → Final Output
4. **Output:** Published content visible via `WithOutputFrom(publisher)`

### Visualization

```
[Start] User Prompt
    ↓
WriterAgent (Draft v1)
    ↓
ReviewerAgent
    ↓
    ├─ "Needs Revision" → (loop back to WriterAgent) → Draft v2 → ReviewerAgent
    │
    └─ "Approved" → PublisherAgent [Terminal]
```

## References

- [MAF Workflows Documentation](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/overview)
- [Cyclic Workflow Example - Request/Response Pattern](https://learn.microsoft.com/en-us/agent-framework/tutorials/workflows/requests-and-responses)
- [Conditional Edges Documentation](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/core-concepts/edges)
- [AddSwitch API - demo2 Reference](c:\Workplace\Demo\maf-demo\demo2\Program.cs)

## Additional Notes

### Difference from Group Chat Pattern
- **Group Chat:** Uses `AgentGroupChat` with `RoundRobinGroupChatManager` or custom termination strategies
- **Workflow Pattern (Our Approach):** Direct graph control with explicit edges
- **Trade-off:** Workflows give more explicit control; Group Chat is higher-level abstraction

### Future Enhancements (Post-Demo3)
- **Checkpointing:** Save state between runs (Demo 4)
- **External Review:** Human-in-the-loop approval (Demo 4)
- **Dynamic Iteration Limits:** Pass max iterations as workflow parameter
- **LLM-based Review:** Use actual AI model instead of simulation
