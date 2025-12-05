using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyToolBox.Agents.Reasoning;

public sealed class ReActStep
{
    public string? Thought { get; init; }

    public StepAction? Action { get; init; }

    public string? Observation { get; set; }

    public string? FinalAnswer { get; init; }

    public required string OriginalResponse { get; init; }

    public bool HasFinalAnswer() => !string.IsNullOrEmpty(FinalAnswer);
}

public sealed class StepAction
{
    [JsonPropertyName("action")] 
    public required string Action { get; init; }

    [JsonPropertyName("action_input")] 
    public Dictionary<string, object?>? ActionInput { get; init; }

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