using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;

namespace TinyToolBox.Agents.Reasoning;

public sealed class StepCompleted(ReActStep step) : WorkflowEvent(step)
{
    public ReActStep AsReActStep() => Data as ReActStep ?? throw new InvalidOperationException($"{nameof(ReActStep)} data required.");
}

public sealed class ReActExecutor : Executor<ChatMessage>
{
    private readonly IKernelBuilder _builder;
    private readonly PromptExecutionSettings _promptExecutionSettings;

    public ReActExecutor(
        string id,
        IKernelBuilder builder,
        PromptExecutionSettings promptExecutionSettings,
        ExecutorOptions? options = null,
        bool declareCrossRunShareable = false)
        : base(id, options, declareCrossRunShareable)
    {
        _builder = builder;
        _promptExecutionSettings = promptExecutionSettings;
    }

    public override async ValueTask HandleAsync(
        ChatMessage message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var kernel = _builder.Build();
        var reActContext = new ReActContext(message.Text, kernel);
        while (!reActContext.Completed())
        {
            var step = await reActContext.Next(_promptExecutionSettings, cancellationToken);
            await context.AddEventAsync(new StepCompleted(step), cancellationToken);
        }

        await context.YieldOutputAsync(reActContext.Steps, cancellationToken);
    }
}