using Microsoft.Extensions.AI;

namespace TinyToolBox.Agents.Reasoning;

public sealed class ReActStep
{
    public string? Thought { get; set; }

    public string? Action { get; set; }

    public AIFunctionArguments? ActionInput { get; set; }

    public string? Observation { get; set; }

    public string? FinalAnswer { get; set; }

    public string? OriginalResponse { get; set; }
}

public sealed class ReActResult
{
    public string FinalAnswer => Steps.Count > 0 ? Steps[^1].FinalAnswer ?? string.Empty : string.Empty;

    public List<ReActStep> Steps { get; } = [];

    public bool Success { get; set; }

    public string? Error { get; set; }
}