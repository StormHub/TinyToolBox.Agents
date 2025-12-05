using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace TinyToolBox.Agents.Reasoning;

public sealed class StepCompleted(ReActStep step) : WorkflowEvent(step)
{
    public ReActStep AsReActStep() =>
        Data as ReActStep ?? throw new InvalidOperationException($"{nameof(ReActStep)} data required.");
}

public sealed class ReActExecutor : Executor<ChatMessage>
{
    private readonly IChatClient _chatClient;
    private readonly ChatOptions _chatOptions;

    public ReActExecutor(
        IChatClient chatClient,
        ChatOptions chatOptions,
        ExecutorOptions? options = null,
        bool declareCrossRunShareable = false)
        : base("ReAct", options, declareCrossRunShareable)
    {
        _chatClient = chatClient;
        _chatOptions = chatOptions;
    }

    public override async ValueTask HandleAsync(
        ChatMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var reActLoop = new ReActLoop(message.Text, _chatClient, _chatOptions);
        while (!reActLoop.Completed())
        {
            var step = await reActLoop.Next(cancellationToken);
            await context.AddEventAsync(new StepCompleted(step), cancellationToken);
        }

        await context.YieldOutputAsync(reActLoop.Steps, cancellationToken);
    }
}