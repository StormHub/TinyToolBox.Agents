using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TinyToolBox.Agents.Reasoning;

public sealed class ReActStep
{
    public string? Thought { get; init; }

    public StepAction? Action { get; init; }

    public string? Observation { get; set; }

    public string? FinalAnswer { get; init; }

    public required string OriginalResponse { get; init; }

    public bool HasFinalAnswer() => !string.IsNullOrEmpty(FinalAnswer);

    private static readonly Regex finalAnswerPattern = new(@"Final Answer:\s*(?:```([\s\S]*?)```|([^\n]+))");
    private static readonly Regex actionPattern = new(@"Action:\s*```(?:json)?([\s\S]*?)```");

    internal static ReActStep Parse(string input)
    {
        // Looking for final answer first
        var finalAnswerMatch = finalAnswerPattern.Match(input);
        var finalAnswer = finalAnswerMatch.Success
            ? finalAnswerMatch.Groups[2].Value.Trim()
            : default;

        // Then looking for action
        var actionMatch = actionPattern.Match(input);
        if (actionMatch.Success)
        {
            if (!string.IsNullOrEmpty(finalAnswer))
                throw new InvalidOperationException($"Both Final Answer and Action found in the output. \n {input}");

            var json = actionMatch.Groups[1].Value.Trim();
            var stepAction = StepAction.Parse(json);
            return new ReActStep
            {
                Thought = input[..(actionMatch.Index - 1)],
                Action = stepAction,
                OriginalResponse = input
            };
        }

        if (!string.IsNullOrEmpty(finalAnswer))
            return new ReActStep
            {
                Thought = input[..(finalAnswerMatch.Index - 1)],
                FinalAnswer = finalAnswer,
                OriginalResponse = input
            };

        throw new InvalidOperationException($"Could not parse response output: ${input}");
    }
}

public sealed class StepAction
{
    [JsonPropertyName("action")] public required string Action { get; init; }

    [JsonPropertyName("action_input")] public Dictionary<string, object?>? ActionInput { get; init; }

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