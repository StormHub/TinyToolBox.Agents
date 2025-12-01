using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;

namespace TinyToolBox.Agents.Reasoning;

internal sealed class ReActStep
{
    public string? Thought { get; init; }

    public StepAction? Action { get; init; }

    public string? Observation { get; set; }

    public string? FinalAnswer { get; init; }

    public required string OriginalResponse { get; init; }

    public bool HasFinalAnswer() => !string.IsNullOrEmpty(FinalAnswer);
}

internal sealed class StepAction
{
    [JsonPropertyName("action")] public required string Action { get; init; }

    [JsonPropertyName("action_input")] public Dictionary<string, object?>? ActionInput { get; init; }

    internal async Task<FunctionResult> Invoke(Kernel kernel, CancellationToken cancellationToken = default)
    {
        var metadata = kernel.Plugins
                           .GetFunctionsMetadata()
                           .FirstOrDefault(x =>
                               string.Equals(Action, $"{x.PluginName}.{x.Name}", StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException($"Action '{Action}' not found.");

        var function = kernel.Plugins.GetFunction(metadata.PluginName, metadata.Name)
                       ?? throw new InvalidOperationException(
                           $"Action plugin '{metadata.PluginName}.{metadata.Name}' not found.");

        var arguments = ActionInput is not null ? new KernelArguments(ActionInput) : default;
        var functionResult = await function.InvokeAsync(kernel, arguments, cancellationToken);

        return functionResult;
    }

    internal static StepAction Parse(string json, JsonSerializerOptions? options = default)
    {
        StepAction? stepAction;
        try
        {
            stepAction = JsonSerializer.Deserialize<StepAction>(json, options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Unable to parse action JSON: ${ex.Message} {json}");
        }

        if (stepAction is null || string.IsNullOrEmpty(stepAction.Action))
            throw new InvalidOperationException($"Invalid action JSON: {json}");

        return stepAction;
    }

    internal string Format() => $"Action:\n```{JsonSerializer.Serialize(this)}```";
}