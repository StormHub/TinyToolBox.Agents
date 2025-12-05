using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TinyToolBox.Agents.Reasoning.Prompts;

namespace TinyToolBox.Agents.Reasoning;

public sealed class ReActLoop
{
    private readonly IChatClient _chatClient;
    private readonly ChatOptions _chatOptions;
    private readonly AIFunction[] _tools;

    private readonly StringTemplate _template;
    private readonly List<(string key, object value)> _arguments;

    private readonly List<ReActStep> _steps;
    private readonly ILogger _logger;

    public ReActLoop(string input, IChatClient chatClient, ChatOptions chatOptions, ILoggerFactory? loggerFactory = null)
    {
        var tools = chatOptions.Tools?.OfType<AIFunction>().ToArray();
        if (tools is null || tools.Length == 0)
        {
            throw new ArgumentException("ReActLoop requires at least one AIFunction tool to operate.", nameof(chatOptions));
        }

        _chatClient = chatClient;
        _chatOptions = chatOptions;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ReActLoop>();
        _tools = tools;

        var templateContent = Templates.LoadContent("template.txt");
        _template = new StringTemplate(templateContent);

        var list = Format(tools).ToList();
        _arguments =
        [
            ("input", input),
            ("tools", string.Join('\n', list.Select(x => x.description))),
            ("tool_actions", string.Join('\n', list.Select(x => $"{x.description}, args: {x.arguments}"))),
        ];
        _steps = [];
    }

    public bool Completed() => _steps.LastOrDefault()?.HasFinalAnswer() ?? false;

    public async Task<ReActStep> Next(CancellationToken cancellationToken = default)
    {
        var step = _steps.LastOrDefault();
        if (step != null && step.HasFinalAnswer())
        {
            return step;
        }

        var scratchpad = BuildScratchpad();
        var message = _template.Format([.._arguments, ("agent_scratchpad", scratchpad)]);
        var options = _chatOptions.Clone();
        options.Tools = default; // Manual tool handling in ReActLoop

        var response = await _chatClient.GetResponseAsync(message, options, cancellationToken);
        step = ReActStep.Parse(response.Text);
        if (!step.HasFinalAnswer())
        {
            step.Observation = await InvokeAction(step, cancellationToken);
        }

        _steps.Add(step);
        return step;
    }

    private async Task<string> InvokeAction(ReActStep step, CancellationToken cancellationToken)
    {
        var stepAction = step.Action ?? throw new InvalidOperationException("Action does not exit on step");

        var tool = _tools.FirstOrDefault(x =>
                       x.Name.Equals(stepAction.Action, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Tool '{step.Action!.Action}' not found among available tools.");

        var arguments = stepAction.ActionInput is not null ? new AIFunctionArguments(stepAction.ActionInput) : default;

        _logger.LogInformation("Invoking action: {Action}", stepAction.Action);

        var result = await tool.InvokeAsync(arguments, cancellationToken);
        if (result is JsonElement element)
        {
            return element.GetRawText();
        }

        return result?.ToString() ?? string.Empty;
    }

    private string BuildScratchpad()
    {
        if (_steps.Count == 0) return string.Empty;

        var buffer = new StringBuilder();
        for (var i = _steps.Count - 1; i >= 0; i--)
        {
            var step = _steps[i];
            var thought = (i > 0 ? "Thought:" : string.Empty) + $" {step.Thought}";
            var action = step.Action?.Format() ?? string.Empty;
            var observation = $"Observation: {step.Observation}";
            var content = $"{thought}\n{action}\n{observation}\n";

            content = HttpUtility.HtmlEncode(content);
            buffer.Insert(0, content);
        }

        return buffer.ToString();
    }

    private static IEnumerable<(string description, string arguments)> Format(IEnumerable<AIFunction> tools)
    {
        foreach (var tool in tools)
        {
            var description = $"{tool.Name} : {tool.Description}";
            var arguments = new List<string>();
            if (tool.JsonSchema.TryGetProperty("properties", out var properties))
            {
                arguments.AddRange(properties.EnumerateObject()
                    .Select(jsonProperty => jsonProperty.Value.TryGetProperty("type", out var type)
                        ? $"\"{jsonProperty.Name}\" : {{ \"type:\" : {type.GetRawText()} }}"
                        : $"\"{jsonProperty.Name}\" "));
            }

            yield return (description, $"{{ {string.Join(", ", arguments)} }}");
        }
    }
}