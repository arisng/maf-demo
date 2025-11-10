using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;

class Program
{
    static async Task Main(string[] args)
    {
        // Initialize executors
        var dummyInput = new DummyInputAgent();
        var writer1 = new WriterAgent("Writer1");
        var writer2 = new WriterAgent("Writer2");
        var aggregator = new AggregatorAgent();
        var selector = new SelectorAgent();
        var approver = new ApproverAgent();
        var rejector = new RejectorAgent();

        // Build the workflow: fan-out to parallel writers, sequential through aggregator
        var workflow = new WorkflowBuilder(dummyInput)
            // Fan-out: DummyInput to both writers in parallel
            .AddFanOutEdge(dummyInput, targets: [writer1, writer2])
            // Sequential: Writer1 -> Aggregator (will receive both writer outputs)
            .AddEdge(writer1, aggregator)
            .AddEdge(writer2, aggregator)
            // Aggregator to selector
            .AddEdge(aggregator, selector)
            // Conditional branching: Selector to either Approver or Rejector
            .AddSwitch(selector, switchBuilder =>
                switchBuilder
                    .AddCase<(string selectedSlogan, string route)>(
                        result => result.route == "approve",
                        approver)
                    .AddCase<(string selectedSlogan, string route)>(
                        result => result.route == "reject",
                        rejector)
            )
            // Specify output sources
            .WithOutputFrom(approver) // only get output from approver
            // .WithOutputFrom(approver, rejector) // to get output from both
            .Build();

        // Run the workflow with a task
        StreamingRun run = await InProcessExecution.StreamAsync(workflow, "Create a slogan for an innovative technology product.");
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        await foreach (var evt in run.WatchStreamAsync().ConfigureAwait(false))
        {
            if (evt is WorkflowOutputEvent outputEvent)
            {
                Console.WriteLine($"Output: {outputEvent.Data}");
            }
            else
            {
                Console.WriteLine($"Event: {evt.GetType().Name}");
            }
        }
    }
}

// Dummy input agent to start the fan-out
class DummyInputAgent : Executor<string, string>
{
    public DummyInputAgent() : base("DummyInput") {}

    public override async ValueTask<string> HandleAsync(string input, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"DummyInput: {input}");
        // Just pass through the input
        return input;
    }
}

// Writer agent implementation (reusable for both)
class WriterAgent : Executor<string, string>
{
    private readonly string _name;

    public WriterAgent(string name) : base(name)
    {
        _name = name;
    }

    public override async ValueTask<string> HandleAsync(string input, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Simulate generating different slogans
        await Task.Delay(100); // Simulate processing time
        string slogan = _name == "Writer1" ? "Innovate Your Future!" : "Tech for Tomorrow!";
        Console.WriteLine($"{_name}: {slogan}");
        await context.YieldOutputAsync($"{_name} output: {slogan}");
        return slogan;
    }
}

// Aggregator agent to collect results from parallel writers
class AggregatorAgent : Executor<string, List<string>>
{
    private readonly List<string> _collected = new();
    private int _expectedCount = 2;

    public AggregatorAgent() : base("Aggregator") {}

    public override async ValueTask<List<string>> HandleAsync(string input, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        _collected.Add(input);
        Console.WriteLine($"Aggregator: Received slogan {_collected.Count}/{_expectedCount}: {input}");
        
        if (_collected.Count >= _expectedCount)
        {
            Console.WriteLine($"Aggregator: All slogans collected");
            await context.YieldOutputAsync($"Aggregated {_collected.Count} slogans");
            return new List<string>(_collected);
        }
        
        // Return partial list if not complete
        return new List<string>(_collected);
    }
}

// Selector agent implementation (selects best slogan and decides route)
class SelectorAgent : Executor<List<string>, (string selectedSlogan, string route)>
{
    public SelectorAgent() : base("Selector") {}

    public override async ValueTask<(string, string)> HandleAsync(List<string> input, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Select slogan with "Future" keyword, otherwise first one
        string selected = input.FirstOrDefault(s => s.Contains("Future")) ?? input.FirstOrDefault() ?? "No slogan";
        string route = selected.Contains("Future") ? "approve" : "reject";
        
        Console.WriteLine($"Selector: Selected '{selected}' -> Route: {route}");
        await context.YieldOutputAsync($"Selected: {selected}, Route: {route}");
        
        return (selected, route);
    }
}

// Approver agent implementation (conditional route)
class ApproverAgent : Executor<(string selectedSlogan, string route), string>
{
    public ApproverAgent() : base("Approver") {}

    public override async ValueTask<string> HandleAsync((string selectedSlogan, string route) input, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(50);
        string result = $"APPROVED: '{input.selectedSlogan}' - Excellent choice!";
        Console.WriteLine($"Approver: {result}");
        // await context.YieldOutputAsync(result);
        return result;
    }
}

// Rejector agent implementation (conditional route)
class RejectorAgent : Executor<(string selectedSlogan, string route), string>
{
    public RejectorAgent() : base("Rejector") {}

    public override async ValueTask<string> HandleAsync((string selectedSlogan, string route) input, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        await Task.Delay(50);
        string result = $"REJECTED: '{input.selectedSlogan}' - Needs improvement.";
        Console.WriteLine($"Rejector: {result}");
        // await context.YieldOutputAsync(result);
        return result;
    }
}
