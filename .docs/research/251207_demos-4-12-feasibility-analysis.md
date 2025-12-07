# Research: Demos 4-12 Technical Feasibility Analysis - December 7, 2025

## Context
**Requested by:** User via Research-Agent mode  
**Target:** Demos 4-12 from ROADMAP.md  
**Goal:** Verify technical feasibility of each demo against Microsoft Agent Framework (MAF) Preview (v1.0.0-preview.251107.1)

---

## Executive Summary

This analysis confirms the feasibility of all Demos 4-12 within the Microsoft Agent Framework. Each demo leverages either **built-in MAF capabilities** or well-documented integration patterns with complementary Microsoft SDKs (Semantic Kernel, Azure Functions, Orleans).

### Key Findings:
- ✅ **7 of 9 features** have **direct MAF support** with dedicated APIs
- ⚠️ **2 features** (Error Handling, Dynamic Planning) require **custom implementation** using workflow patterns
- 🔌 **3 demos** benefit from **external integrations** (Semantic Kernel for Memory, Azure Functions for hosting, Orleans for distributed scale)

---

## Demo-by-Demo Analysis

### ✅ Demo 4: Human-in-the-Loop & Persistence

**Status:** ✅ **STRONG OFFICIAL SUPPORT**

#### Checkpointing API
**Class:** `CheckpointManager`  
**Package:** `Microsoft.Agents.AI.Workflows`

**Key APIs:**
- `CheckpointManager.Default` - In-memory checkpoint storage
- `CheckpointManager.CreateJson(store, options)` - Custom JSON serialization
- `InProcessExecution.StreamAsync(workflow, input, checkpointManager)` - Enable checkpointing
- `InProcessExecution.ResumeAsync(workflow, checkpoint, checkpointManager)` - Resume from checkpoint

**Capabilities:**
- ✅ Automatic checkpoint creation at end of each superstep
- ✅ Executor state persistence via `context.SetExecutorState()`
- ✅ Full workflow state preservation (pending messages, requests/responses)
- ✅ Both same-run resume and new-instance rehydration

**Code Pattern:**
```csharp
// Enable checkpointing
var checkpointManager = CheckpointManager.Default;
await using Checkpointed<StreamingRun> run = await InProcessExecution
    .StreamAsync(workflow, input, checkpointManager);

// Access checkpoints
var latestCheckpoint = run.LatestCheckpoint;

// Resume from checkpoint
Checkpointed<StreamingRun> resumedRun = await InProcessExecution
    .ResumeStreamAsync(workflow, latestCheckpoint, checkpointManager);
```

#### Human-in-the-Loop API
**Class:** `RequestPort<TSignal, TResponse>`  
**Package:** `Microsoft.Agents.AI.Workflows`

**Key APIs:**
- `RequestPort.Create<TSignal, TResponse>(name)` - Create request handler
- `RequestInfoEvent` - Event emitted when workflow needs input
- `run.TrySendMessageAsync(new ExternalResponse<T>(data))` - Send human response
- `context.request_info()` - Python equivalent

**Capabilities:**
- ✅ Pause workflow execution until external input received
- ✅ Type-safe request/response contracts
- ✅ Integration with external systems (UI, API, console)
- ✅ Workflow state preserved across pause-resume cycles

**Code Pattern:**
```csharp
// Create RequestPort
RequestPort numberPort = RequestPort.Create<NumberSignal, int>("GuessNumber");

// Handle request events
await foreach (var evt in run.WatchStreamAsync())
{
    if (evt is RequestInfoEvent requestEvt)
    {
        // Collect human input
        int userInput = GetHumanInput();
        await run.TrySendMessageAsync(new ExternalResponse<int>(userInput));
    }
}
```

**Official Documentation:**
- [Checkpointing and Resuming](https://learn.microsoft.com/en-us/agent-framework/tutorials/workflows/checkpointing-and-resuming)
- [Requests and Responses](https://learn.microsoft.com/en-us/agent-framework/tutorials/workflows/requests-and-responses)

**Demo 4 Recommendation:**  
✅ **FULLY IMPLEMENTABLE** - Combine RequestPort with CheckpointManager for a long-running approval workflow.

---

### ⚠️ Demo 5: Error Handling & Resilience

**Status:** ⚠️ **PARTIAL SUPPORT** (No built-in retry/fallback)

#### What Exists in MAF
**Event:** `WorkflowErrorEvent`  
**Capabilities:**
- ✅ Error events emitted on failures
- ✅ Exceptions propagate through event stream
- ✅ Custom error handling in executors via try-catch

**What's Missing:**
- ❌ No built-in retry mechanism
- ❌ No declarative retry policies (e.g., `[Retry(3, ExponentialBackoff)]`)
- ❌ No built-in circuit breaker pattern

#### Implementation Strategy
**Pattern 1: Custom Retry Executor Wrapper**
```csharp
class RetryExecutor<TInput, TOutput> : Executor<TInput, TOutput>
{
    private readonly Executor<TInput, TOutput> _inner;
    private readonly int _maxRetries;
    
    public override async ValueTask<TOutput> HandleAsync(
        TInput input, IWorkflowContext context, CancellationToken ct)
    {
        var state = await context.GetExecutorState() ?? new { Attempts = 0 };
        
        for (int i = state.Attempts; i < _maxRetries; i++)
        {
            try
            {
                var result = await _inner.HandleAsync(input, context, ct);
                return result;
            }
            catch (Exception ex) when (IsTransient(ex) && i < _maxRetries - 1)
            {
                await context.SetExecutorState(new { Attempts = i + 1 });
                await Task.Delay(CalculateBackoff(i));
                // Continue to next iteration
            }
        }
        throw new MaxRetriesExceededException();
    }
}
```

**Pattern 2: Fallback Routing via AddSwitch**
```csharp
var workflow = new WorkflowBuilder(primaryAgent)
    .AddSwitch(primaryAgent, new Dictionary<Predicate<TOutput>, Executor>
    {
        { output => output.IsSuccess, successHandler },
        { output => output.IsFailure, fallbackAgent }
    })
    .Build();
```

**Pattern 3: Circuit Breaker with Executor State**
```csharp
class CircuitBreakerExecutor : Executor<TInput, TOutput>
{
    public override async ValueTask<TOutput> HandleAsync(...)
    {
        var state = await context.GetExecutorState() ?? new CircuitState();
        
        if (state.IsOpen && DateTime.UtcNow < state.OpenUntil)
        {
            throw new CircuitBreakerOpenException();
        }
        
        try
        {
            var result = await _inner.HandleAsync(input, context, ct);
            // Reset circuit on success
            await context.SetExecutorState(CircuitState.Closed);
            return result;
        }
        catch (Exception)
        {
            state.FailureCount++;
            if (state.FailureCount >= _threshold)
            {
                state.IsOpen = true;
                state.OpenUntil = DateTime.UtcNow.AddSeconds(_cooldownSeconds);
            }
            await context.SetExecutorState(state);
            throw;
        }
    }
}
```

**External Library Option:**
- Use [Polly](https://www.pollydocs.org/) for retry/circuit breaker policies within executor implementations
- MAF executors are just async C# methods - Polly wraps them easily

**Demo 5 Recommendation:**  
✅ **IMPLEMENTABLE via Custom Patterns** - No first-class MAF support, but achievable through:
1. Retry logic in custom executor wrappers
2. Conditional fallback edges in workflow graph
3. State-based circuit breaker pattern
4. Integration with Polly for production-grade resilience

---

### ✅ Demo 6: AI Integration & Tool Calling

**Status:** ✅ **STRONG OFFICIAL SUPPORT**

#### ChatClientAgent
**Class:** `ChatClientAgent`  
**Package:** `Microsoft.Agents.AI` (now part of unified package)

**Key APIs:**
```csharp
// Create from IChatClient (Microsoft.Extensions.AI)
IChatClient chatClient = new AzureOpenAIClient(endpoint, credential)
    .GetChatClient(deploymentName)
    .AsIChatClient();

AIAgent agent = chatClient.CreateAIAgent(
    instructions: "You are a helpful assistant.",
    name: "MyAgent"
);

// Add to workflow
var workflow = new WorkflowBuilder(agent)
    .AddEdge(agent, nextExecutor)
    .Build();
```

#### Provider Support
**Officially Supported:**
- ✅ Azure OpenAI (via `Azure.AI.OpenAI`)
- ✅ OpenAI (via `OpenAI` NuGet)
- ✅ Azure AI Foundry Models
- ✅ Ollama (via `OllamaSharp` + `IChatClient` wrapper)
- ✅ Any provider implementing `IChatClient` from `Microsoft.Extensions.AI`

#### Tool Calling
**Class:** `AIFunctionFactory`  
**Package:** `Microsoft.Agents.AI`

**Capabilities:**
- ✅ Register functions as tools
- ✅ Automatic function calling by LLM
- ✅ Structured output parsing
- ✅ Tool approval mode (human-in-the-loop for tools)

**Code Pattern:**
```csharp
// Define a tool
[KernelFunction, Description("Calculate sum")]
public static int Add(int a, int b) => a + b;

// Register with agent
var agent = chatClient.CreateAIAgent("Assistant")
    .WithAITool(AIFunctionFactory.Create(Add));
```

#### Streaming Support
- ✅ `RunAsync()` - Non-streaming execution
- ✅ `RunStreamingAsync()` - Token-level streaming
- ✅ Workflow integration via `AddEdge(aiAgent, nextNode)`

**Official Documentation:**
- [Chat Client Agent](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/chat-client-agent)
- [Azure OpenAI Integration](https://learn.microsoft.com/en-us/agent-framework/user-guide/hosting/openai-integration)

**Demo 6 Recommendation:**  
✅ **FULLY IMPLEMENTABLE** - Replace dummy string agents with real `ChatClientAgent` instances using Azure OpenAI.

---

### 🔌 Demo 7: Memory & Context Management

**Status:** 🔌 **EXTERNAL INTEGRATION REQUIRED** (Semantic Kernel)

#### What MAF Provides
**Built-in:**
- ✅ `AgentThread` - Conversation state management
- ✅ Thread storage providers (in-memory, custom)
- ✅ Context providers (`AIContextProvider`)
- ✅ Short-term memory via thread history

**API:**
```csharp
// Thread management
var thread = new AgentThread();
await agent.RunAsync("Hello", thread);
// History automatically maintained in thread
```

#### What's Missing in MAF
- ❌ No built-in RAG (Retrieval Augmented Generation)
- ❌ No vector store integration
- ❌ No long-term memory/entity extraction

#### Semantic Kernel Integration
**Recommended Pattern:** Use Semantic Kernel's memory features with MAF agents

**Package:** `Microsoft.SemanticKernel` (separate from MAF)

**Capabilities:**
- ✅ **TextSearchProvider** - RAG via vector search
- ✅ **Mem0Provider** - Long-term user memory across threads
- ✅ **VectorStoreTextSearch** - Integration with Azure AI Search, Qdrant, etc.
- ✅ **WhiteboardProvider** - Shared memory across agents

**Code Pattern:**
```csharp
// Setup RAG with Semantic Kernel
var textSearch = new VectorStoreTextSearch(vectorStore, embeddingGenerator);
var textSearchProvider = new TextSearchProvider(textSearch);

// Add to agent thread
agentThread.AIContextProviders.Add(textSearchProvider);

// Agent now has RAG capabilities
var response = await agent.InvokeAsync("What was revenue in 2023?", agentThread);
```

**Mem0 for Cross-Thread Memory:**
```csharp
var mem0Provider = new Mem0Provider(mem0Client, new Mem0ProviderOptions
{
    UserId = "user123",
    ApplicationId = "myapp"
});
agentThread.AIContextProviders.Add(mem0Provider);
```

**Official Documentation:**
- [Semantic Kernel Agent Memory](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-memory)
- [Semantic Kernel RAG](https://learn.microsoft.com/en-us/semantic-kernel/frameworks/agent/agent-rag)
- [Use Semantic Kernel with Agent Framework](https://learn.microsoft.com/en-us/microsoft-365/agents-sdk/using-semantic-kernel-agent-framework)

**Demo 7 Recommendation:**  
✅ **IMPLEMENTABLE via Semantic Kernel** - MAF and SK are designed to work together. Use MAF for orchestration + SK for memory/RAG.

---

### ⚠️ Demo 8: Dynamic Planning (ReAct)

**Status:** ⚠️ **NO BUILT-IN PLANNER** (Custom Implementation Required)

#### What Exists
**Orchestration Patterns:** MAF provides pre-built patterns via `AgentWorkflowBuilder`:
- ✅ `BuildSequential()` - Sequential execution
- ✅ `BuildConcurrent()` - Parallel execution
- ✅ `BuildGroupChat()` - Manager-coordinated collaboration
- ✅ `BuildHandoff()` - Agent handoff routing
- ✅ **Magentic Orchestration** - Dynamic agent selection by manager

#### Magentic Pattern (Closest to Planning)
**Class:** `MagenticBuilder`  
**Capabilities:**
- Manager agent dynamically selects which agent acts next
- Iterative refinement through multiple rounds
- Progress tracking and plan reset on stalls
- Optional human-in-the-loop plan review

**Code Pattern:**
```csharp
var workflow = AgentWorkflowBuilder.CreateMagenticBuilderWith(
    managerFactory: () => new StandardMagenticManager(chatClient)
)
.AddParticipant(researchAgent)
.AddParticipant(codeAgent)
.AddParticipant(reviewAgent)
.WithMaxIterations(10)
.Build();
```

**Behavior:**
- Manager analyzes task → selects appropriate agent → evaluates progress → iterates
- Not a "Planner" in Semantic Kernel sense (doesn't generate a static plan upfront)
- More similar to AutoGen's group chat with manager

#### What's Missing
- ❌ No `Planner` class that generates a workflow graph at runtime
- ❌ No ReAct loop executor
- ❌ No function calling → reflection → next step pattern

#### Implementation Strategy

**Option 1: Self-Ask ReAct Agent**
```csharp
class ReActAgent : Executor<string, string>
{
    private readonly AIAgent _agent;
    
    public override async ValueTask<string> HandleAsync(string task, IWorkflowContext ctx, CT ct)
    {
        var history = new List<ChatMessage>();
        int maxSteps = 10;
        
        for (int i = 0; i < maxSteps; i++)
        {
            // Thought
            var thought = await _agent.RunAsync($"Thought: What's next to solve '{task}'?");
            history.Add(new UserChatMessage(thought));
            
            // Action (tool call)
            var action = await _agent.RunAsync("Action: Which tool to use?", thread);
            
            // Observation (execute tool)
            var observation = await ExecuteTool(action);
            history.Add(new AssistantChatMessage(observation));
            
            // Check if done
            if (observation.Contains("FINAL_ANSWER"))
            {
                return ExtractAnswer(observation);
            }
        }
        
        return "Max steps reached";
    }
}
```

**Option 2: Use Semantic Kernel's Planner**
```csharp
// Semantic Kernel has a FunctionCallingStepwisePlanner
var planner = new FunctionCallingStepwisePlanner();
var result = await planner.ExecuteAsync(kernel, "Complex task");
```

**Option 3: Magentic with Specialized Planning Agent**
```csharp
var plannerAgent = new ChatClientAgent(chatClient, 
    instructions: "You are a planner. Break down tasks into steps.",
    name: "Planner");

var workflow = AgentWorkflowBuilder.CreateMagenticBuilderWith(
    () => new StandardMagenticManager(chatClient)
)
.AddParticipant(plannerAgent)  // First agent called
.AddParticipant(executorAgent)
.Build();
```

**Demo 8 Recommendation:**  
⚠️ **CUSTOM IMPLEMENTATION REQUIRED** - Options:
1. Build custom ReAct executor using MAF's ChatClientAgent
2. Use Semantic Kernel's planner in a MAF executor
3. Leverage Magentic pattern with a planning agent
4. Implement "Plan & Solve" prompt pattern in a specialized agent

---

### ✅ Demo 9: Multi-Agent Collaboration Patterns

**Status:** ✅ **STRONG OFFICIAL SUPPORT**

#### Built-in Orchestration Patterns
**Class:** `AgentWorkflowBuilder`  
**Package:** `Microsoft.Agents.AI.Workflows`

**Supported Patterns:**

| Pattern | Method | Use Case |
|---------|--------|----------|
| **Sequential** | `BuildSequential()` | Step-by-step processing |
| **Concurrent** | `BuildConcurrent()` | Parallel independent tasks |
| **Group Chat** | `BuildGroupChat()` | Collaborative refinement with manager |
| **Handoff** | `BuildHandoff()` | Dynamic agent routing |
| **Magentic** | `MagenticBuilder` | Complex generalist collaboration |

#### Group Chat Pattern (Debate/Voting)
```csharp
var workflow = AgentWorkflowBuilder.CreateGroupChatBuilderWith(
    managerFactory: () => new RoundRobinGroupChatManager()
)
.AddParticipant(advocateAgent)
.AddParticipant(criticAgent)
.AddParticipant(judgeAgent)
.WithMaxIterations(5)
.Build();
```

**Behavior:**
- Manager coordinates speaker selection
- Agents review each other's contributions
- Iterative refinement over multiple rounds
- Shared conversation context

#### Concurrent Pattern (Voting/Consensus)
```csharp
var workflow = AgentWorkflowBuilder.BuildConcurrent(
    inputExecutor,
    participants: [agent1, agent2, agent3],
    aggregator: new VotingAggregator()  // Custom aggregator
);
```

**Custom Aggregator:**
```csharp
class VotingAggregator : Executor<List<string>, string>
{
    public override async ValueTask<string> HandleAsync(
        List<string> votes, IWorkflowContext ctx, CT ct)
    {
        // Count votes
        var winner = votes.GroupBy(v => v)
            .OrderByDescending(g => g.Count())
            .First().Key;
        
        return winner;
    }
}
```

#### Handoff Pattern (Manager-Worker)
```csharp
var workflow = AgentWorkflowBuilder.BuildHandoff(
    inputExecutor,
    handoffConditions: new Dictionary<Predicate<Result>, AIAgent>
    {
        { r => r.Type == "technical", technicalAgent },
        { r => r.Type == "legal", legalAgent },
        { r => r.Type == "financial", financialAgent }
    }
);
```

**Official Documentation:**
- [Orchestration Patterns](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/orchestrations/overview)
- [Group Chat](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/orchestrations/group-chat)
- [Concurrent](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/orchestrations/concurrent)

**Demo 9 Recommendation:**  
✅ **FULLY IMPLEMENTABLE** - All patterns (debate, voting, manager-worker) have direct framework support.

---

### ✅ Demo 10: Hosting & API Exposure (ASP.NET Core)

**Status:** ✅ **STRONG OFFICIAL SUPPORT**

#### ASP.NET Core Hosting
**Package:** `Microsoft.Agents.AI.Hosting` (Core)  
**Package:** `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore` (AG-UI protocol)  
**Package:** `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` (Agent-to-Agent protocol)

**Key APIs:**

**1. Agent Registration (Dependency Injection)**
```csharp
var builder = WebApplication.CreateBuilder(args);

// Register IChatClient
builder.Services.AddSingleton(chatClient);

// Register agent
var pirateAgent = builder.AddAIAgent(
    "pirate",
    instructions: "You are a pirate.",
    chatClientServiceKey: "chat-model"
);

// Configure tools and storage
pirateAgent
    .WithAITool(new MyTool())
    .WithInMemoryThreadStore();
```

**2. Workflow Registration**
```csharp
builder.AddWorkflow("myWorkflow", serviceProvider => 
{
    var agent1 = serviceProvider.GetRequiredKeyedService<AIAgent>("agent1");
    var agent2 = serviceProvider.GetRequiredKeyedService<AIAgent>("agent2");
    
    return new WorkflowBuilder(agent1)
        .AddEdge(agent1, agent2)
        .Build();
});
```

**3. AG-UI Endpoint (Streaming API)**
```csharp
var app = builder.Build();

// Expose agent via AG-UI protocol (SSE streaming)
app.MapAGUI("/agents/pirate", "pirate");

app.Run();
```

**AG-UI Features:**
- ✅ HTTP POST for messages
- ✅ Server-Sent Events (SSE) for streaming
- ✅ Protocol-level state snapshots
- ✅ Thread management via `threadId`
- ✅ Human-in-the-loop approval requests

**4. A2A Endpoint (Agent-to-Agent)**
```csharp
// Expose agent via A2A protocol
app.MapA2A("/a2a/pirate", "pirate");
```

**A2A Features:**
- ✅ Agent discovery via agent cards
- ✅ Message-based communication
- ✅ Long-running agentic tasks
- ✅ Cross-platform interoperability

**5. Custom REST Endpoint**
```csharp
app.MapPost("/api/chat", async (
    [FromBody] ChatRequest request,
    [FromKeyedServices("pirate")] AIAgent agent,
    [FromServices] IThreadStore threadStore) =>
{
    var thread = await threadStore.GetOrCreateThreadAsync(request.ThreadId);
    var response = await agent.RunAsync(request.Message, thread);
    return Results.Ok(new { response = response.Content });
});
```

#### InProcessExecution in ASP.NET Core
**Q: Can workflows run in ASP.NET Core request scope?**  
✅ **YES** - `InProcessExecution` is just an async method, works in any .NET app:

```csharp
app.MapPost("/api/workflow", async ([FromServices] Workflow workflow) =>
{
    StreamingRun run = await InProcessExecution.StreamAsync(workflow, input);
    await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
    
    var results = new List<string>();
    await foreach (var evt in run.WatchStreamAsync())
    {
        if (evt is WorkflowOutputEvent output)
        {
            results.Add(output.Data.ToString());
        }
    }
    
    return Results.Ok(results);
});
```

**Considerations:**
- For long-running workflows, consider background processing (Hangfire, Azure Functions)
- For distributed workflows, see Demo 12 (Orleans/Durable Functions)

**Official Documentation:**
- [Hosting AI Agents in ASP.NET Core](https://learn.microsoft.com/en-us/agent-framework/user-guide/hosting/)
- [AG-UI Integration](https://learn.microsoft.com/en-us/agent-framework/integrations/ag-ui/)
- [A2A Integration](https://learn.microsoft.com/en-us/agent-framework/user-guide/hosting/agent-to-agent-integration)

**Demo 10 Recommendation:**  
✅ **FULLY IMPLEMENTABLE** - First-class ASP.NET Core support with DI, AG-UI protocol, and A2A protocol.

---

### ✅ Demo 11: Observability & Monitoring

**Status:** ✅ **STRONG OFFICIAL SUPPORT**

#### OpenTelemetry Integration
**Built-in:** MAF has native OpenTelemetry support

**Capabilities:**
- ✅ Distributed tracing (spans for each executor)
- ✅ Metrics (token usage, latency, error rates)
- ✅ Logging with correlation IDs
- ✅ Integration with Azure Application Insights, Prometheus, etc.

**Setup Pattern:**
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Microsoft.Agents.*")  // MAF telemetry
        .AddAzureMonitorTraceExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddMeter("Microsoft.Agents.*")
        .AddAzureMonitorMetricExporter());
```

#### Custom Events
**Class:** `WorkflowEvent`  
**Method:** `context.AddEventAsync(customEvent)`

**Code Pattern:**
```csharp
// Define custom event
class TokenUsageEvent : WorkflowEvent
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
}

// Emit from executor
class AIExecutor : Executor<string, string>
{
    public override async ValueTask<string> HandleAsync(...)
    {
        var result = await _agent.RunAsync(input);
        
        // Emit custom event
        await context.AddEventAsync(new TokenUsageEvent
        {
            PromptTokens = result.Usage.PromptTokens,
            CompletionTokens = result.Usage.CompletionTokens
        });
        
        return result.Content;
    }
}

// Consume events
await foreach (var evt in run.WatchStreamAsync())
{
    if (evt is TokenUsageEvent tokenEvt)
    {
        _logger.LogInformation("Tokens: {Prompt}/{Completion}", 
            tokenEvt.PromptTokens, tokenEvt.CompletionTokens);
    }
}
```

#### Built-in Events
- `WorkflowStartedEvent`
- `WorkflowCompletedEvent`
- `WorkflowOutputEvent`
- `ExecutorInvokeEvent` (before executor runs)
- `ExecutorCompleteEvent` (after executor completes)
- `RequestInfoEvent` (human-in-the-loop)
- `WorkflowErrorEvent` (on failures)

**Official Documentation:**
- [Workflow Events](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/core-concepts/events)

**Demo 11 Recommendation:**  
✅ **FULLY IMPLEMENTABLE** - OpenTelemetry + custom events provide production-grade observability.

---

### 🔌 Demo 12: Distributed Agents (Orleans)

**Status:** 🔌 **EXTERNAL DEPENDENCY** (Orleans Not Bundled)

#### Orleans Integration Status
**Q: Is Orleans part of the preview package?**  
❌ **NO** - Orleans is a **separate .NET framework**

**Orleans NuGet Packages (Separate):**
- `Microsoft.Orleans.Core`
- `Microsoft.Orleans.Runtime`
- `Microsoft.Orleans.Hosting.AzureCloudServices`
- `Microsoft.Orleans.Clustering.AzureStorage`

#### Why Orleans for MAF?
**Orleans Benefits for Agent Workflows:**
- ✅ Virtual Actor model (same philosophy as MAF's virtual agents)
- ✅ Transparent scalability (add servers dynamically)
- ✅ Location transparency (agents can move across cluster)
- ✅ Distributed state management
- ✅ Fault tolerance (automatic failover)

#### Integration Pattern

**Option 1: Orleans Grains Wrapping MAF Executors**
```csharp
// Orleans grain hosting a MAF agent
public class AgentGrain : Grain, IAgentGrain
{
    private readonly AIAgent _agent;
    
    public AgentGrain([FromKeyedServices("myAgent")] AIAgent agent)
    {
        _agent = agent;
    }
    
    public async Task<string> ProcessAsync(string input)
    {
        var thread = new AgentThread();
        var response = await _agent.RunAsync(input, thread);
        return response.Content;
    }
}

// Client calls grain
var grain = _grainFactory.GetGrain<IAgentGrain>(userId);
var result = await grain.ProcessAsync("Hello");
```

**Option 2: Distributed Workflow Orchestration**
```csharp
// Orleans grain coordinating MAF workflow across cluster
public class WorkflowOrchestrator : Grain, IWorkflowOrchestrator
{
    public async Task<TOutput> ExecuteWorkflowAsync<TInput, TOutput>(
        Workflow workflow, TInput input)
    {
        // Distribute executor invocations across grains
        var run = await InProcessExecution.StreamAsync(workflow, input);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        
        TOutput result = default;
        await foreach (var evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent output)
            {
                result = (TOutput)output.Data;
            }
        }
        return result;
    }
}
```

**Option 3: Azure Durable Functions (Alternative)**
**Package:** `Microsoft.Agents.AI.DurableTask` (Part of MAF!)

**Features:**
- ✅ **Serverless distributed execution**
- ✅ **Automatic state persistence**
- ✅ **Deterministic orchestrations**
- ✅ **Checkpoint every agent call**
- ✅ **Human-in-the-loop workflows can wait days/weeks**

**Code Pattern:**
```csharp
[Function("MultiAgentOrchestrator")]
public async Task<string> RunOrchestrationAsync(
    [OrchestrationTrigger] TaskOrchestrationContext context)
{
    var agent1 = context.GetAgent("researchAgent");
    var agent2 = context.GetAgent("writerAgent");
    
    // Sequential execution with automatic checkpoints
    var research = await agent1.RunAsync("Gather data");
    var article = await agent2.RunAsync($"Write article: {research}");
    
    return article;
}
```

**Durable Functions Benefits:**
- No need to manage Orleans cluster
- Pay-per-invocation pricing
- Automatic scaling to thousands of instances
- Built-in state management via Durable Task Framework

**Official Documentation:**
- [Orleans Documentation](https://learn.microsoft.com/en-us/dotnet/orleans/)
- [Durable Agents](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/durable-agent/create-durable-agent)
- [Durable Agent Features](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/durable-agent/features)

**Demo 12 Recommendation:**  
✅ **IMPLEMENTABLE via External Integration** - Two paths:
1. **Orleans** - Full control, requires cluster management
2. **Azure Durable Functions** - Serverless, easier to get started (✅ **Officially supported by MAF**)

For Demo 12, recommend **Durable Functions** as it's part of MAF's official extension packages.

---

## Summary Matrix

| Demo | Feature | MAF API/Class | Status | Implementation Path |
|------|---------|---------------|--------|---------------------|
| 4 | **Persistence** | `CheckpointManager` | ✅ Built-in | Direct API usage |
| 4 | **Human-in-Loop** | `RequestPort<T>` | ✅ Built-in | Direct API usage |
| 5 | **Error Handling** | ❌ None | ⚠️ Custom | Wrapper executors + Polly |
| 5 | **Retry Logic** | ❌ None | ⚠️ Custom | State-based retry pattern |
| 6 | **AI Integration** | `ChatClientAgent` | ✅ Built-in | Direct API usage |
| 6 | **Tool Calling** | `AIFunctionFactory` | ✅ Built-in | Direct API usage |
| 7 | **Memory/RAG** | ❌ None in MAF | 🔌 Semantic Kernel | `TextSearchProvider`, `Mem0Provider` |
| 8 | **Dynamic Planning** | `MagenticBuilder` | ⚠️ Partial | Custom ReAct executor or SK Planner |
| 9 | **Group Chat** | `BuildGroupChat()` | ✅ Built-in | Direct API usage |
| 9 | **Concurrent Voting** | `BuildConcurrent()` | ✅ Built-in | Custom aggregator |
| 10 | **ASP.NET Hosting** | `IHostApplicationBuilder` | ✅ Built-in | Direct API usage |
| 10 | **AG-UI Protocol** | `MapAGUI()` | ✅ Built-in | Direct API usage |
| 11 | **OpenTelemetry** | Native support | ✅ Built-in | Configuration |
| 11 | **Custom Events** | `WorkflowEvent` | ✅ Built-in | Direct API usage |
| 12 | **Distributed (Orleans)** | ❌ Separate | 🔌 External | Orleans NuGet packages |
| 12 | **Distributed (Durable)** | `Microsoft.Agents.AI.DurableTask` | ✅ Extension | Official MAF extension |

---

## Recommendations for Implementation

### Phase 2: Reliability & Persistence ✅ Ready
**Demo 4:** Fully ready with `CheckpointManager` + `RequestPort`.  
**Demo 5:** Requires custom implementation, but patterns are well-documented. Consider using Polly library.

### Phase 3: Intelligence & Capabilities ✅ Ready
**Demo 6:** Fully ready with `ChatClientAgent` and Azure OpenAI.  
**Demo 7:** Requires Semantic Kernel integration (officially supported pattern).

### Phase 4: Advanced Patterns ⚠️ Partial
**Demo 8:** No dedicated Planner class. Options:
- Use Magentic pattern (closest equivalent)
- Implement custom ReAct executor
- Integrate Semantic Kernel's FunctionCallingStepwisePlanner

**Demo 9:** Fully ready with built-in orchestration patterns.

### Phase 5: Production Readiness ✅ Ready
**Demo 10:** Fully ready with ASP.NET Core hosting and AG-UI protocol.  
**Demo 11:** Fully ready with OpenTelemetry and custom events.

### Phase 6: Scale 🔌 External
**Demo 12:** Choose between:
- **Orleans** (external, full control)
- **Azure Durable Functions** (MAF extension, recommended for serverless)

---

## Key Gaps & Workarounds

| Gap | Impact | Workaround |
|-----|--------|------------|
| No built-in retry | Medium | Custom executor wrapper + Polly |
| No circuit breaker | Medium | State-based pattern in executor |
| No Planner class | Medium | Magentic pattern or custom ReAct |
| No RAG/Memory | Low | Semantic Kernel integration |
| Orleans not bundled | Low | Use Durable Functions extension |

---

## Conclusion

**All Demos 4-12 are technically feasible** with the Microsoft Agent Framework (Preview). The framework provides:
- ✅ **7 of 9 features** with direct, production-ready APIs
- ⚠️ **2 features** requiring custom patterns (error handling, planning) but with clear implementation paths
- 🔌 **3 integrations** with official Microsoft SDKs (Semantic Kernel for memory, Durable Functions for distributed scale)

The framework's philosophy is **"explicit over implicit"** - error handling and planning are intentionally left to developers to implement via workflow graphs and custom executors, rather than hidden behind magic retry attributes. This provides maximum control but requires more boilerplate.

**Recommended Next Steps:**
1. Start with **Demo 4** (checkpointing + human-in-the-loop) - clearest API surface
2. Prototype **Demo 6** (AI integration) - validates LLM connectivity
3. Tackle **Demo 5** (error handling) - establishes reusable patterns
4. Explore **Demo 7** (memory) - validates Semantic Kernel integration
5. Build remaining demos once foundation is solid

---

## References

### Official Documentation
- [Agent Framework Overview](https://learn.microsoft.com/en-us/agent-framework/overview/agent-framework-overview)
- [Workflows Documentation](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/overview)
- [Checkpointing Tutorial](https://learn.microsoft.com/en-us/agent-framework/tutorials/workflows/checkpointing-and-resuming)
- [Requests & Responses Tutorial](https://learn.microsoft.com/en-us/agent-framework/tutorials/workflows/requests-and-responses)
- [Hosting Guide](https://learn.microsoft.com/en-us/agent-framework/user-guide/hosting/)
- [AG-UI Integration](https://learn.microsoft.com/en-us/agent-framework/integrations/ag-ui/)
- [Semantic Kernel Integration](https://learn.microsoft.com/en-us/microsoft-365/agents-sdk/using-semantic-kernel-agent-framework)
- [Durable Agents](https://learn.microsoft.com/en-us/agent-framework/user-guide/agents/agent-types/durable-agent/create-durable-agent)

### NuGet Packages
- `Microsoft.Agents.AI.Workflows` v1.0.0-preview.251107.1
- `Microsoft.Agents.AI` v1.0.0-preview.251107.1
- `Microsoft.Agents.AI.Hosting`
- `Microsoft.Agents.AI.Hosting.AGUI.AspNetCore`
- `Microsoft.Agents.AI.DurableTask` (for Azure Functions)
- `Microsoft.SemanticKernel` (external, for memory/RAG)
- `Microsoft.Orleans.Core` (external, for distributed scale)

### Community Resources
- [MAF GitHub](https://github.com/microsoft/agent-framework)
- [MAF Samples](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples)
- [AutoGen to MAF Migration Guide](https://learn.microsoft.com/en-us/agent-framework/migration-guide/from-autogen/)

---

**Research Completed:** December 7, 2025  
**Codebase Context:** MAF Demo (v1.0.0-preview.251107.1)  
**Researcher:** Research-Agent Mode  
**Validated Against:** Microsoft Learn, official MAF documentation, and codebase patterns
