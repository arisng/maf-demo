# Microsoft Agent Framework Demo - Copilot Instructions

This repository demonstrates the **Microsoft Agent Framework** (Preview) using .NET. It contains progressive demos (`demo1`, `demo2`, etc.) showcasing workflow patterns.

## 🏗️ Architecture & Core Concepts

- **Executors**: The fundamental building block is `Executor<TInput, TOutput>`. All agents must inherit from this.
- **Workflows**: Constructed using `WorkflowBuilder`.
  - **Edges**: Define data flow (`AddEdge`, `AddFanOutEdge`).
  - **Branching**: Use `AddSwitch` with predicate-based routing.
  - **Parallelism**: Achieved via `AddFanOutEdge`.
- **Execution**:
  - Workflows are executed using `InProcessExecution.StreamAsync`.
  - **TurnToken**: You MUST send a `TurnToken` to kick off execution after starting the stream.
  - **Events**: Consume `WorkflowOutputEvent` from `run.WatchStreamAsync()` to get results.

## 💻 Coding Conventions

### Agent Implementation
- Inherit from `Executor<TInput, TOutput>`.
- Implement `HandleAsync`.
- Use `context.YieldOutputAsync(data)` to emit data to the workflow stream (visible to the runner).
- Return value of `HandleAsync` is passed to the next node in the graph.

```csharp
class MyAgent : Executor<string, string>
{
    public MyAgent() : base("MyAgentName") {}

    public override async ValueTask<string> HandleAsync(string input, IWorkflowContext context, CancellationToken ct)
    {
        // Logic here
        await context.YieldOutputAsync("Visible to stream");
        return "Passed to next node";
    }
}
```

### Workflow Construction
- Always start with `new WorkflowBuilder(startNode)`.
- Use `AddFanOutEdge` for parallel execution.
- Use `AddSwitch` for conditional logic.
- Use `WithOutputFrom` to specify which nodes' outputs are final workflow outputs.

```csharp
var workflow = new WorkflowBuilder(startNode)
    .AddFanOutEdge(startNode, [parallel1, parallel2])
    .AddEdge(parallel1, aggregator)
    .AddEdge(parallel2, aggregator)
    .Build();
```

### Stateful Aggregation
- Aggregators in this framework currently manage their own state.
- They receive inputs one by one.
- Logic often involves checking if a collection count matches an expected number before proceeding.

## 🚀 Workflows & Commands

- **Build**: `dotnet build`
- **Run**: Navigate to the specific demo folder (e.g., `cd demo1`) and run `dotnet run`.
- **Dependencies**:
  - `Microsoft.Agents.AI`
  - `Microsoft.Agents.AI.Workflows`
  - *Note: These are preview packages.*

## ⚠️ Common Pitfalls
- **Forgetting TurnToken**: The workflow won't start processing until `run.TrySendMessageAsync(new TurnToken(emitEvents: true))` is called.
- **Yielding vs Returning**: `YieldOutputAsync` sends data to the *user/stream*. The return value sends data to the *next agent*.
- **State Persistence**: In-process execution holds state in memory.
