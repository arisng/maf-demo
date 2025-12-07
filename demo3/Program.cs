using System;
using System.Threading.Tasks;
using Microsoft.Agents.AI.Workflows;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Demo 3: Iterative Feedback Loop ===\n");

        // Initialize executors
        var writer = new WriterAgent();
        var reviewer = new ReviewerAgent();
        var feedbackAdapter = new FeedbackAdapterAgent();
        var publisher = new PublisherAgent();

        // Build the workflow with a feedback loop
        var workflow = new WorkflowBuilder(writer)
            .AddEdge(writer, reviewer)
            // Conditional branching: loop back through adapter or proceed to publisher
            .AddSwitch(reviewer, switchBuilder =>
                switchBuilder
                    .AddCase<(string content, string decision)>(
                        result => result.decision == "Needs Revision",
                        feedbackAdapter)  // Route to adapter to convert tuple → string
                    .AddCase<(string content, string decision)>(
                        result => result.decision == "Approved",
                        publisher)  // Terminal path
            )
            .AddEdge(feedbackAdapter, writer)  // Cyclic edge: back to writer
            .WithOutputFrom(publisher)
            .Build();

        // Run the workflow with a task
        StreamingRun run = await InProcessExecution.StreamAsync(
            workflow, 
            "Write a product announcement for a new AI-powered code editor named 'CodeFlow'.");
        
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        
        Console.WriteLine("--- Workflow Execution ---\n");
        
        await foreach (var evt in run.WatchStreamAsync().ConfigureAwait(false))
        {
            if (evt is WorkflowOutputEvent outputEvent)
            {
                Console.WriteLine($"\n[Final Output]\n{outputEvent.Data}\n");
            }
            else
            {
                // Uncomment to see other event types
                // Console.WriteLine($"[Event: {evt.GetType().Name}]");
            }
        }

        Console.WriteLine("\n=== Workflow Complete ===");
    }
}

// Writer agent implementation - manages drafts and revisions
class WriterAgent : Executor<string, string>
{
    private string? _lastDraft = null;
    private int _revisionCount = 0;

    public WriterAgent() : base("WriterAgent") {}

    public override async ValueTask<string> HandleAsync(
        string input, 
        IWorkflowContext context, 
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(150); // Simulate processing time

        if (_lastDraft == null)
        {
            // Initial draft generation
            _lastDraft = GenerateInitialDraft(input);
            _revisionCount++;
            Console.WriteLine($"\n[Writer v{_revisionCount}] Generated initial draft:");
            Console.WriteLine($"  \"{_lastDraft}\"");
        }
        else
        {
            // Revision based on feedback
            _lastDraft = ReviseDraft(_lastDraft, input);
            _revisionCount++;
            Console.WriteLine($"\n[Writer v{_revisionCount}] Revised draft based on feedback:");
            Console.WriteLine($"  \"{_lastDraft}\"");
        }

        await context.YieldOutputAsync($"Writer v{_revisionCount}: {_lastDraft}");
        return _lastDraft;
    }

    private string GenerateInitialDraft(string prompt)
    {
        // Simulate initial draft (intentionally basic)
        return "CodeFlow is a new code editor. It uses AI. It helps you code.";
    }

    private string ReviseDraft(string currentDraft, string feedback)
    {
        // Simulate improvement based on feedback
        if (_revisionCount == 1)
        {
            return "CodeFlow is an innovative AI-powered code editor that revolutionizes your development workflow. " +
                   "It features intelligent code completion, real-time error detection, and context-aware suggestions.";
        }
        else
        {
            return "Introducing CodeFlow: The AI-Powered Code Editor That Thinks Like You Do. " +
                   "CodeFlow revolutionizes software development with cutting-edge AI that understands your intent, " +
                   "suggests optimal solutions, and learns from your coding style. Experience unprecedented productivity " +
                   "with intelligent autocomplete, predictive error prevention, and seamless integration with your favorite tools.";
        }
    }
}

// Reviewer agent implementation - provides feedback and makes approval decisions
class ReviewerAgent : Executor<string, (string content, string decision)>
{
    private int _iterationCount = 0;
    private const int MAX_ITERATIONS = 3;

    public ReviewerAgent() : base("ReviewerAgent") {}

    public override async ValueTask<(string, string)> HandleAsync(
        string input, 
        IWorkflowContext context, 
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(100); // Simulate review time
        
        _iterationCount++;
        Console.WriteLine($"\n[Reviewer - Iteration {_iterationCount}] Reviewing content...");

        // Termination safety: force approval after max iterations
        if (_iterationCount >= MAX_ITERATIONS)
        {
            Console.WriteLine("  Decision: APPROVED (max iterations reached)");
            await context.YieldOutputAsync($"Reviewer: Approved (iteration {_iterationCount})");
            return (input, "Approved");
        }

        // Simulated review logic: needs revision on first iteration, approve on second
        bool needsRevision = _iterationCount == 1;

        if (needsRevision)
        {
            string feedback = "Needs more detail and excitement. Make it compelling!";
            Console.WriteLine($"  Decision: NEEDS REVISION");
            Console.WriteLine($"  Feedback: {feedback}");
            await context.YieldOutputAsync($"Reviewer: Needs Revision - {feedback}");
            return (feedback, "Needs Revision");  // Feedback sent back to writer
        }
        else
        {
            Console.WriteLine("  Decision: APPROVED");
            await context.YieldOutputAsync($"Reviewer: Approved (iteration {_iterationCount})");
            return (input, "Approved");
        }
    }
}

// Feedback adapter - converts reviewer tuple back to string for writer
class FeedbackAdapterAgent : Executor<(string content, string decision), string>
{
    public FeedbackAdapterAgent() : base("FeedbackAdapter") {}

    public override async ValueTask<string> HandleAsync(
        (string content, string decision) input, 
        IWorkflowContext context, 
        CancellationToken cancellationToken = default)
    {
        // Extract the feedback content and pass it back to writer
        Console.WriteLine($"[Adapter] Routing feedback back to writer for revision");
        return input.content;  // Just the feedback string
    }
}

// Publisher agent implementation - formats and publishes the final approved content
class PublisherAgent : Executor<(string content, string decision), string>
{
    public PublisherAgent() : base("PublisherAgent") {}

    public override async ValueTask<string> HandleAsync(
        (string content, string decision) input, 
        IWorkflowContext context, 
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(100); // Simulate publishing time
        
        Console.WriteLine("\n[Publisher] Publishing approved content...");
        
        string publishedContent = FormatForPublication(input.content);
        
        // Note: WithOutputFrom(publisher) will auto-yield the return value
        // So we don't need context.YieldOutputAsync here
        return publishedContent;
    }

    private string FormatForPublication(string content)
    {
        return $"✓ PUBLISHED ✓\n\n{content}\n\n--- Published on {DateTime.Now:yyyy-MM-dd HH:mm} ---";
    }
}
