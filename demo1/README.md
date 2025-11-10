# Multi-Agent Workflow Demo 1

A simple .NET console application demonstrating a sequential multi-agent workflow using the Microsoft Agent Framework.

## Overview

This demo creates two agents that work together:

- **WriterAgent**: Generates a technology slogan
- **ReviewerAgent**: Provides feedback on the slogan

The agents execute in sequence, passing data between them through a workflow orchestration.

## Prerequisites

- .NET 8.0 SDK or later
- Internet connection for NuGet package downloads

## Setup

1. The project is already configured with the required packages:
   - `Microsoft.Agents.AI` (1.0.0-preview.251107.1)
   - `Microsoft.Agents.AI.Workflows` (1.0.0-preview.251107.1)

2. Navigate to the project directory:

   ```bash
   cd demo1
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

### Program.cs

- **Main Method**: Initializes agents, builds workflow, executes it
- **WriterAgent**: Executor that generates a slogan
- **ReviewerAgent**: Executor that provides feedback and yields final output

### Key Components

- **WorkflowBuilder**: Constructs the sequential workflow
- **StreamingRun**: Handles asynchronous workflow execution
- **Executor<TInput, TOutput>**: Base class for workflow steps

## Execution Flow

1. Workflow starts with input string
2. WriterAgent processes input → generates slogan → passes to ReviewerAgent
3. ReviewerAgent processes slogan → generates feedback → yields as output
4. Main loop captures and displays workflow events

## Expected Output

```text
Writer: Innovate Your Future!
Reviewer: Great slogan! Maybe consider adding a fun element.
```

## Extensions

This basic example can be extended with:

- Additional agents in the workflow chain
- Conditional branching logic
- Real AI service integration
- Parallel execution paths
- Error handling and retries

## Dependencies

- Microsoft.Agents.AI: Core agent framework
- Microsoft.Agents.AI.Workflows: Workflow orchestration</content>
