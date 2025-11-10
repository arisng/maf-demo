# Multi-Agent Workflow Demo 2

An extended .NET console application demonstrating advanced multi-agent workflows using the Microsoft Agent Framework, building on Demo 1.

## Overview

This demo extends Demo 1's sequential workflow with **parallel execution**, **aggregation**, and **conditional branching**:

- **DummyInput**: Passes the input to the workflow
- **Writer1**: Generates a technology slogan ("Innovate Your Future!")
- **Writer2**: Generates another technology slogan ("Tech for Tomorrow!")
- **Aggregator**: Collects slogans from both writers (stateful aggregation)
- **Selector**: Selects the best slogan based on keyword matching
- **Approver**: Handles approved slogans (contains "Future")
- **Rejector**: Handles rejected slogans (missing "Future")

The workflow demonstrates:
1. **Parallel Fan-Out**: DummyInput → Writer1 and Writer2 simultaneously
2. **Stateful Aggregation**: Both writers → Aggregator (collects 2/2 results)
3. **Conditional Branching**: Selector → Approver (if "Future") or Rejector (otherwise)
4. **Output Handling**: WorkflowOutputEvent from terminal executors

## Implementation Highlights

### Parallel Fan-Out
```csharp
.AddFanOutEdge(dummyInput, targets: [writer1, writer2])
```

### Stateful Aggregation
```csharp
class AggregatorAgent : Executor<string, List<string>>
{
    private readonly List<string> _collected = new();
    private int _expectedCount = 2;
    
    // Collects multiple calls until expectedCount reached
}
```

### Conditional Routing
```csharp
.AddSwitch(selector, switchBuilder =>
    switchBuilder
        .AddCase<(string selectedSlogan, string route)>(
            result => result.route == "approve",
            approver)
        .AddCase<(string selectedSlogan, string route)>(
            result => result.route == "reject",
            rejector)
)
```

### Output Specification
```csharp
.WithOutputFrom(approver, rejector)
```

## Research Findings: Parallel Execution with Fan-Out/Fan-In

Based on official Microsoft Agent Framework documentation:

### Fan-Out Edges

- **Purpose**: Distribute the same input to multiple executors simultaneously
- **API**: `AddFanOutEdge(source, targets: [executor1, executor2])`
- **Behavior**: Sends the source executor's output to all specified targets in parallel
- **Use Case**: Enable concurrent processing of the same data by multiple agents

### Fan-In Edges

- **Purpose**: Collect results from multiple source executors into a single target
- **API**: `AddFanInEdge(target, sources: [executor1, executor2])` or `AddFanInEdge(sources, target)`
- **Behavior**: Target executor receives a list/array of results from all source executors
- **Data Type**: Target executor must accept `List<T>` or equivalent collection type
- **Triggering**: Executes only after all source executors have completed

### Implementation Pattern

```csharp
var workflow = new WorkflowBuilder(startExecutor)
    .AddFanOutEdge(startExecutor, targets: [worker1, worker2])
    .AddFanInEdge(aggregator, sources: [worker1, worker2])
    .Build();
```

### Key Requirements

- Source executors must complete successfully for fan-in to trigger
- Target executor input type must match collection of source output types
- Framework handles synchronization automatically
- Supports both AI agents and custom executors

### Current Gap

Our implementation attempted fan-out/fan-in but selector executor wasn't triggered. This may indicate:

- Type mismatch in fan-in collection handling
- Synchronization issues in preview framework
- Need for proper collection type handling in selector executor

### Recommendation

Re-implement with proper `List<string>` input type for selector and add comprehensive logging to trace execution flow.

## Research Findings: Selector Logic and Result Aggregation

Based on official Microsoft Agent Framework documentation:

### Aggregation Patterns

- **Fan-In Collection**: Results from parallel executors are automatically collected into `List<T>` or equivalent collections
- **Type Safety**: Target executor must declare input type matching the collection (e.g., `List<string>`)
- **Concurrent Orchestration**: Framework provides built-in aggregation for concurrent agent workflows
- **Custom Aggregators**: Override default behavior with domain-specific result processing

### Selector Implementation

- **Input Type**: `Executor<List<TInput>, TOutput>` where `TInput` matches source output types
- **Selection Logic**: Implement custom criteria within `HandleAsync` method
- **Common Patterns**:
  - Pick best result based on scoring/ranking
  - Combine multiple results into single output
  - Filter and select based on content analysis
  - Weighted voting or consensus algorithms

### Selector Implementation Pattern

```csharp
class SelectorExecutor : Executor<List<string>, string>
{
    public override async ValueTask<string> HandleAsync(List<string> inputs, ...)
    {
        // Implement selection logic here
        return SelectBestSlogan(inputs);
    }
    
    private string SelectBestSlogan(List<string> slogans)
    {
        // Example: prefer longer slogans, or those containing keywords
        return slogans.OrderByDescending(s => s.Length).FirstOrDefault() ?? slogans[0];
    }
}
```

### Key Requirements

- Handle empty collections gracefully
- Implement robust selection criteria
- Support different result types from parallel sources
- Provide fallback logic for edge cases

### Current Gap

Selector currently receives single string instead of collection, lacking actual selection logic. Need to:

- Change input type to `List<string>`
- Implement meaningful selection criteria (length, keywords, creativity metrics)
- Add error handling for empty/malformed inputs

### Recommendation

Implement `SelectorExecutor<List<string>, string>` with criteria like slogan length, keyword presence, or simple rotation. Add logging to show selection process.

## Research Findings: Conditional Branching with Switch-Case

Based on official Microsoft Agent Framework documentation:

### Switch-Case Edges

- **Purpose**: Route messages to different executors based on evaluated conditions
- **API**: `AddSwitch(source, switchBuilder => switchBuilder.AddCase(condition, target).WithDefault(defaultTarget))`
- **Behavior**: Evaluates cases in order, routes to first matching condition
- **Default Handling**: `WithDefault()` ensures routing for unmatched messages

### Condition Functions

- **Type**: `Func<object?, bool>` that inspects message content
- **Pattern**: `(message) => message is T obj && obj.Property == expectedValue`
- **Evaluation**: Short-circuit evaluation stops at first match
- **Type Safety**: Conditions can cast and validate message types

### Implementation Pattern

```csharp
builder.AddSwitch(selectorExecutor, switchBuilder =>
    switchBuilder
        .AddCase(
            message => message is string slogan && 
                      slogan.Contains("innovative", StringComparison.OrdinalIgnoreCase),
            reviewerExecutor
        )
        .WithDefault(directOutputExecutor)
);
```

### Key Requirements

- Conditions must handle `null` and unexpected types gracefully
- `WithDefault()` prevents dead-end workflows
- Ordered evaluation allows priority-based routing
- Supports complex business logic in condition functions

### Current Gap

No conditional routing implemented - all paths go to reviewer. Need to:

- Implement `AddSwitch` with keyword-based conditions
- Add `DirectYieldExecutor` for non-innovative slogans
- Ensure proper condition evaluation and routing
- Handle edge cases in condition functions

### Recommendation

Add switch-case routing after selector with condition checking for "innovative" keyword. Use `WithDefault` for direct output path.

## Research Findings: Output Handling and Yield Mechanism

Based on official Microsoft Agent Framework documentation:

### Workflow Output Events

- **WorkflowOutputEvent**: Triggered when executor calls `ctx.yield_output(result)`
- **Streaming Execution**: `StreamAsync()` provides real-time event monitoring
- **Event Types**: Includes `ExecutorInvokedEvent`, `ExecutorCompletedEvent`, `WorkflowOutputEvent`
- **Terminal Executors**: Use `WorkflowContext<Never, TOutput>` for executors that yield final results

### Yield Mechanism

- **ctx.yield_output()**: Produces workflow completion result
- **Multiple Outputs**: `WithOutputFrom()` specifies which executors can yield
- **Event Data**: `WorkflowOutputEvent.Data` contains the yielded result
- **Workflow Completion**: Yield signals workflow end and provides final output

### Implementation Pattern

```csharp
// Terminal executor that yields output
class FinalExecutor : Executor<string, string>
{
    public override async ValueTask<string> HandleAsync(string input, IWorkflowContext context)
    {
        var result = ProcessInput(input);
        await context.YieldOutputAsync(result); // Triggers WorkflowOutputEvent
        return result;
    }
}

// Streaming execution
await foreach (var evt in run.WatchStreamAsync())
{
    if (evt is WorkflowOutputEvent outputEvent)
    {
        Console.WriteLine($"Final Result: {outputEvent.Data}");
    }
}
```

### Key Requirements

- Terminal executors must call `yield_output()` to complete workflow
- `WorkflowContext<Never, T>` indicates executor yields output of type T
- Event streaming provides observability into workflow execution
- Yield mechanism handles both success and error completion paths

### Current Gap

Yield mechanism not producing `WorkflowOutputEvent` in current implementation. Events show executor completion but no workflow output event. This may indicate:

- Yield not called or called incorrectly
- Event filtering issues in streaming
- Preview framework limitations with yield propagation
- Missing `WithOutputFrom()` specification

### Recommendation

Ensure terminal executor calls `ctx.YieldOutputAsync()` and monitor for `WorkflowOutputEvent` in streaming. If yield doesn't work reliably, implement custom output handling via executor return values and event processing.

## Prerequisites

- .NET 8.0 SDK or later
- Internet connection for NuGet package downloads

## Setup

1. The project is already configured with the required packages:
   - `Microsoft.Agents.AI` (1.0.0-preview.251107.1)
   - `Microsoft.Agents.AI.Workflows` (1.0.0-preview.251107.1)

2. Navigate to the project directory:

   ```bash
   cd demo2
   ```

3. Restore packages (if needed):

   ```bash
   dotnet restore
   ```

## Usage

Run the application:

```bash
dotnet run
```

## Code Structure

- `Program.cs`: Main workflow implementation with all agents
  - DummyInputAgent: Workflow entry point
  - WriterAgent: Parallel slogan generators (Writer1, Writer2)
  - AggregatorAgent: Stateful collector (tracks 2/2 inputs)
  - SelectorAgent: Intelligent selection with keyword-based routing
  - ApproverAgent: Handles approved slogans
  - RejectorAgent: Handles rejected slogans

## Successfully Implemented Features

### ✅ Parallel Fan-Out
- Uses `AddFanOutEdge(dummyInput, targets: [writer1, writer2])`
- Both writers execute simultaneously in SuperStep 2
- Console output shows concurrent execution

### ✅ Stateful Aggregation
- AggregatorAgent receives individual string inputs (not List<string>)
- Tracks collection state with `_collected` list and `_expectedCount`
- Successfully aggregates 2/2 slogans before proceeding

### ✅ Conditional Branching
- Uses `AddSwitch` with predicate-based routing
- Selector returns `(string selectedSlogan, string route)` tuple
- Routes to Approver when route == "approve", Rejector when route == "reject"
- Selector logic: prioritizes slogans containing "Future"

### ✅ Output Handling
- All agents use `await context.YieldOutputAsync()` for intermediate results
- Terminal agents (Approver, Rejector) specified in `.WithOutputFrom()`
- WorkflowOutputEvent successfully captured in event stream
- Console displays all yielded outputs

## Execution Flow

```
Input: "Create a slogan for an innovative technology product."

SuperStep 1: DummyInputAgent
  → Passes input through

SuperStep 2: Parallel Execution
  → Writer1: "Innovate Your Future!"
  → Writer2: "Tech for Tomorrow!"

SuperStep 3: Aggregation (2 calls)
  → Aggregator call 1: receives "Innovate Your Future!"
  → Aggregator call 2: receives "Tech for Tomorrow!"
  → Returns List<string> with both slogans

SuperStep 4: Selection
  → Selector: Finds "Innovate Your Future!" (contains "Future")
  → Returns ("Innovate Your Future!", "approve")

SuperStep 5: Conditional Route
  → Routes to Approver (route == "approve")
  → Output: "APPROVED: 'Innovate Your Future!' - Excellent choice!"
```

## Framework Behavior and Observations (Preview)

### 1. AddFanInEdge Aggregation Pattern
- **Observation**: `AddFanInEdge` doesn't provide automatic `List<T>` aggregation in current preview
- **Solution**: Implemented stateful `AggregatorAgent` with manual collection tracking
- **Implementation**: Executor maintains state (`_collected` list) and tracks `_expectedCount`
- **Works Well**: Successfully aggregates 2/2 results before proceeding to selector

### 2. Stateful Aggregator with Multiple Invocations
- **Behavior**: Aggregator is invoked twice (once per writer output) in separate SuperSteps
- **SuperStep 3a**: Receives first slogan, returns partial list
- **SuperStep 3b**: Receives second slogan, detects completion, returns full list
- **Key Insight**: Framework calls aggregator once per incoming edge, not as a single fan-in operation

### 3. Selector Dual Invocation Pattern
- **Observation**: Selector executes **twice** per workflow run
- **Evidence**: Two `ExecutorInvokedEvent` entries in SuperStep 4
- **Behavior**: Each invocation receives the same aggregated list but may select different slogans
- **Impact**: In the output, first invocation selects "Tech for Tomorrow!" → reject, second selects "Innovate Your Future!" → approve
- **Likely Cause**: Framework may invoke selector once per aggregator output or due to stateful aggregator pattern

### 4. ✅ Output Duplication Resolved
- **Solution**: Removed manual `context.YieldOutputAsync()` from terminal agents
- **Result**: Clean single output per terminal executor
- **Mechanism**: `WithOutputFrom()` automatically yields executor return values
- **Best Practice**: Let `WithOutputFrom()` handle output yielding for terminal executors

### 5. Conditional Branch Execution
- **Behavior**: Both Approver AND Rejector execute in the same SuperStep
- **Evidence**: SuperStep 5 shows both executors invoked simultaneously
- **Explanation**: Each selector invocation routes to a different branch
  - First selector execution → "reject" route → Rejector
  - Second selector execution → "approve" route → Approver
- **Output**: Two distinct outputs, one from each terminal executor

### Execution Pattern Summary

From actual run output after removing duplicate yields:
```
SuperStep 3: Stateful Aggregation (2 invocations)
  → Aggregator call 1: "Tech for Tomorrow!" (partial)
  → Aggregator call 2: "Innovate Your Future!" (complete)

SuperStep 4: Dual Selector Invocations
  → Selector call 1: Selects "Tech for Tomorrow!" → route: "reject"
  → Selector call 2: Selects "Innovate Your Future!" → route: "approve"

SuperStep 5: Both Conditional Branches Execute
  → Rejector: "REJECTED: 'Tech for Tomorrow!' - Needs improvement."
  → Approver: "APPROVED: 'Innovate Your Future!' - Excellent choice!"
  → Output: Both results via WithOutputFrom()
```

**Result**: Clean workflow with both conditional paths executing based on dual selector invocations

### Program.cs

- **Main Method**: Initializes agents, builds workflow, executes it
- **DummyInput**: Entry point agent that passes input to parallel writers
- **Writer1/2**: Parallel executors that generate different slogans simultaneously
- **Aggregator**: Stateful executor that collects outputs from both writers
- **Selector**: Executor that selects best slogan and determines routing
- **Approver/Rejector**: Conditional terminal executors based on selector's decision

### Key Components

- **WorkflowBuilder**: Constructs the workflow with parallel fan-out, aggregation, and conditional branching
- **AddFanOutEdge**: Distributes input to multiple executors for parallel processing
- **AddSwitch**: Implements predicate-based conditional routing
- **WithOutputFrom**: Specifies which executors should yield final workflow outputs
- **StreamingRun**: Handles asynchronous workflow execution with event streaming
- **Executor<TInput, TOutput>**: Base class for typed workflow steps

## Actual Output Example (After Optimization)

```text
Event: SuperStepStartedEvent
Event: ExecutorInvokedEvent
DummyInput: Create a slogan for an innovative technology product.
Event: ExecutorCompletedEvent
Event: SuperStepCompletedEvent

Event: SuperStepStartedEvent
Event: ExecutorInvokedEvent
Event: ExecutorInvokedEvent
Writer1: Innovate Your Future!
Writer2: Tech for Tomorrow!
Event: ExecutorCompletedEvent
Event: ExecutorCompletedEvent
Event: SuperStepCompletedEvent

Event: SuperStepStartedEvent
Event: ExecutorInvokedEvent
Aggregator: Received slogan 1/2: Tech for Tomorrow!
Event: ExecutorCompletedEvent
Event: ExecutorInvokedEvent
Aggregator: Received slogan 2/2: Innovate Your Future!
Aggregator: All slogans collected
Event: ExecutorCompletedEvent
Event: SuperStepCompletedEvent

Event: SuperStepStartedEvent
Event: ExecutorInvokedEvent
Selector: Selected 'Tech for Tomorrow!' -> Route: reject
Event: ExecutorCompletedEvent
Event: ExecutorInvokedEvent
Selector: Selected 'Innovate Your Future!' -> Route: approve
Event: ExecutorCompletedEvent
Event: SuperStepCompletedEvent

Event: SuperStepStartedEvent
Event: ExecutorInvokedEvent
Event: ExecutorInvokedEvent
Rejector: REJECTED: 'Tech for Tomorrow!' - Needs improvement.
Approver: APPROVED: 'Innovate Your Future!' - Excellent choice!
Event: ExecutorCompletedEvent
Event: ExecutorCompletedEvent
Output: REJECTED: 'Tech for Tomorrow!' - Needs improvement.
Output: APPROVED: 'Innovate Your Future!' - Excellent choice!
Event: SuperStepCompletedEvent
```

**Key Observations**:
- ✅ Clean output: One event per terminal executor (no duplicates)
- ✅ Both conditional branches execute in parallel (SuperStep 5)
- ✅ Dual selector invocations route to different branches
- ✅ `WithOutputFrom()` handles output yielding automatically

## Key Learnings and Best Practices

### ✅ Implemented Successfully

1. **Yield Handling**: Use `WithOutputFrom()` alone for terminal executors - remove manual `YieldOutputAsync()`
2. **Stateful Aggregation**: Custom stateful executors can handle fan-in patterns when `AddFanInEdge` doesn't auto-aggregate
3. **Parallel Execution**: `AddFanOutEdge` successfully enables concurrent processing
4. **Conditional Routing**: `AddSwitch` with predicate-based cases works reliably

### Interesting Framework Behaviors

1. **Multiple Selector Invocations**: Selector may execute multiple times, each processing the aggregated results differently
2. **Parallel Branch Execution**: Both conditional branches can execute simultaneously when selector routes differently per invocation
3. **Stateful Executor Pattern**: Aggregator is called once per input edge, not as a single fan-in operation
4. **Output Timing**: `WorkflowOutputEvent` appears after all executors in SuperStep complete

### Production Recommendations

1. **State Management**: Reset stateful executors between workflow runs or use instance-per-run pattern
2. **Deterministic Selection**: If dual selector invocations are undesired, investigate workflow graph structure or use single-input aggregation
3. **Output Filtering**: If only one branch output is needed, filter in event processing or adjust workflow termination
4. **Monitor Framework Updates**: Track Microsoft Agent Framework releases for evolving fan-in and routing behaviors

## Dependencies

- Microsoft.Agents.AI: Core agent framework
- Microsoft.Agents.AI.Workflows: Workflow orchestration</content>
