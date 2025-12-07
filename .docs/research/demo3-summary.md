# Demo 3 Implementation Summary

## Research Questions - ANSWERED ✅

### 1. How do we define a cyclic edge in `WorkflowBuilder`?

**Answer:** Yes, cyclic edges are defined using standard `AddEdge(source, target)` just like any other edge. MAF explicitly supports cyclic graphs.

```csharp
.AddEdge(feedbackAdapter, writer)  // This creates the cycle
```

**Key Finding:** No special API is needed. The framework validates the graph but allows cycles. From Microsoft docs (Request/Response pattern):

```csharp
return new WorkflowBuilder(numberRequestPort)
    .AddEdge(numberRequestPort, judgeExecutor)
    .AddEdge(judgeExecutor, numberRequestPort)  // ← Cyclic edge!
    .BuildAsync<NumberSignal>();
```

---

### 2. How do we prevent infinite loops? Where should the state be stored?

**Answer:** Loop termination is YOUR responsibility. Store iteration state in the executor that makes the termination decision.

**Implementation Pattern:**

```csharp
class ReviewerAgent : Executor<string, (string content, string decision)>
{
    private int _iterationCount = 0;  // ← State here
    private const int MAX_ITERATIONS = 3;  // ← Safety limit

    public override async ValueTask<(string, string)> HandleAsync(...)
    {
        _iterationCount++;
        
        // Force termination at max iterations
        if (_iterationCount >= MAX_ITERATIONS)
        {
            return (input, "Approved");  // Break the loop
        }
        
        // Business logic determines continuation
        bool needsRevision = EvaluateDraft(input);
        return needsRevision 
            ? (feedback, "Needs Revision")  // → loops back
            : (input, "Approved");           // → terminates
    }
}
```

**Critical Points:**
- Framework does NOT automatically detect/prevent infinite loops
- Must implement explicit termination conditions
- State persists across invocations (executors are singletons in the workflow)
- Always have a `MAX_ITERATIONS` safety net

---

### 3. Does MAF support this out of the box or do we need a specific "Router" agent?

**Answer:** MAF's `AddSwitch` provides built-in routing - no separate Router agent is architecturally required.

**However:** In our implementation, we use a **FeedbackAdapterAgent** - but NOT for routing logic. It's for **type conversion**.

```csharp
.AddSwitch(reviewer, switchBuilder =>
    switchBuilder
        .AddCase<(string content, string decision)>(
            result => result.decision == "Needs Revision",
            feedbackAdapter)  // ← Adapter, not router
        .AddCase<(string content, string decision)>(
            result => result.decision == "Approved",
            publisher)
)
.AddEdge(feedbackAdapter, writer)  // ← The actual cycle
```

**Why the Adapter?**

- **ReviewerAgent** outputs: `(string, string)` tuple
- **WriterAgent** expects: `string` input
- **Type mismatch!** MAF's strong typing prevents direct connection
- **FeedbackAdapterAgent** bridges: `(string, string) → string`

```csharp
class FeedbackAdapterAgent : Executor<(string, string), string>
{
    public override async ValueTask<string> HandleAsync(
        (string content, string decision) input, ...)
    {
        return input.content;  // Extract just the string
    }
}
```

---

### 4. Code Skeleton for `Program.cs`

**Complete Working Implementation:**

```csharp
using System;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;

class Program
{
    static async Task Main(string[] args)
    {
        // Initialize executors
        var writer = new WriterAgent();
        var reviewer = new ReviewerAgent();
        var feedbackAdapter = new FeedbackAdapterAgent();  // Type converter
        var publisher = new PublisherAgent();

        // Build the workflow with feedback loop
        var workflow = new WorkflowBuilder(writer)
            .AddEdge(writer, reviewer)
            .AddSwitch(reviewer, switchBuilder =>
                switchBuilder
                    .AddCase<(string, string)>(
                        result => result.decision == "Needs Revision",
                        feedbackAdapter)  // Revision path
                    .AddCase<(string, string)>(
                        result => result.decision == "Approved",
                        publisher)  // Terminal path
            )
            .AddEdge(feedbackAdapter, writer)  // ← CYCLIC EDGE
            .WithOutputFrom(publisher)
            .Build();

        // Execute
        StreamingRun run = await InProcessExecution.StreamAsync(
            workflow, 
            "Write announcement for CodeFlow editor");
        
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        
        await foreach (var evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent outputEvent)
            {
                Console.WriteLine($"Final: {outputEvent.Data}");
            }
        }
    }
}

// Agent signatures
class WriterAgent : Executor<string, string> { ... }
class ReviewerAgent : Executor<string, (string content, string decision)> { ... }
class FeedbackAdapterAgent : Executor<(string, string), string> { ... }
class PublisherAgent : Executor<(string, string), string> { ... }
```

---

## Key Architectural Lessons

### ✅ Cycles Are Supported

No special APIs. Use regular `AddEdge(source, target)` to create cycles.

### ✅ Type Safety Is Enforced

Cannot directly connect executors with incompatible input/output types. Use adapter executors to bridge type gaps.

### ⚠️ Loop Termination Is Manual

You MUST implement:
1. Iteration counters
2. Termination conditions
3. Safety maximum iterations

### ✅ State Lives in Executors

Executors are instantiated once and maintain state across invocations. This is by design for workflow patterns.

### ✅ Conditional Routing Built-In

`AddSwitch` with predicates provides powerful routing without separate router agents. Use it for loop exit conditions.

---

## Execution Flow

**Iteration 1:**
1. User Prompt → WriterAgent → Draft v1
2. Draft v1 → ReviewerAgent → `(feedback, "Needs Revision")`
3. Switch evaluates → Routes to FeedbackAdapter
4. FeedbackAdapter → extracts feedback string → WriterAgent

**Iteration 2:**
1. Feedback → WriterAgent → Draft v2
2. Draft v2 → ReviewerAgent → `(draft, "Approved")`
3. Switch evaluates → Routes to PublisherAgent
4. PublisherAgent → Final output (workflow terminates)

---

## Files Created

- `demo3/Program.cs` - Complete implementation
- `demo3/demo3.csproj` - Project file
- `demo3/README.md` - Comprehensive documentation
- `.docs/research/251207_iterative-feedback-loops.md` - Research findings

---

## Verification

```bash
cd demo3
dotnet build  # ✅ Builds successfully
dotnet run    # ✅ Executes with 2 iterations, then terminates
```

**Output shows:**
- Initial draft (v1)
- Reviewer feedback → Needs Revision
- Adapter routing message
- Revised draft (v2)
- Reviewer approval
- Publisher final output

---

## Next Steps (Demo 4)

Based on this foundation, Demo 4 will explore:
- **Human-in-the-Loop:** Using `RequestPort` pattern for external approval
- **Checkpointing:** Persisting workflow state across restarts
- **Durable Execution:** Long-running workflows that survive process crashes
