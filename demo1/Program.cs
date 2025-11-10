using System;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;

class Program
{
    static async Task Main(string[] args)
    {
        // Initialize executors
        var writerAgent = new WriterAgent();
        var reviewerAgent = new ReviewerAgent();

        // Build the workflow
        var workflow = new WorkflowBuilder(writerAgent)
            .AddEdge(writerAgent, reviewerAgent)
            .Build();

        // Run the workflow with a task
        StreamingRun run = await InProcessExecution.StreamAsync(workflow, "Create a slogan for an innovative technology product.");
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        await foreach (var evt in run.WatchStreamAsync().ConfigureAwait(false))
        {
            if (evt is WorkflowOutputEvent outputEvent)
            {
                Console.WriteLine(outputEvent.Data);
            }
        }
    }
}

// Writer agent implementation
class WriterAgent : Executor<string, string>
{
    public WriterAgent() : base("WriterAgent") {}

    public override async ValueTask<string> HandleAsync(string input, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Simulates generating a slogan
        await Task.Delay(100); // Simulate processing time
        string slogan = "Innovate Your Future!";
        Console.WriteLine($"Writer: {slogan}");
        return slogan;
    }
}

// Reviewer agent implementation
class ReviewerAgent : Executor<string, string>
{
    public ReviewerAgent() : base("ReviewerAgent") {}

    public override async ValueTask<string> HandleAsync(string input, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        // Simulates providing feedback
        await Task.Delay(100); // Simulate processing time
        string feedback = "Great slogan! Maybe consider adding a fun element.";
        Console.WriteLine($"Reviewer: {feedback}");
        await context.YieldOutputAsync(feedback);
        return feedback;
    }
}
