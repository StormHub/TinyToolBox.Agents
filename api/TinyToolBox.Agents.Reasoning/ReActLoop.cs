using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TinyToolBox.Agents.Reasoning.Prompts;

namespace TinyToolBox.Agents.Reasoning;

public sealed class ReActLoop
{
    private static readonly Regex finalAnswerPattern = new(@"Final Answer:\s*(?:```([\s\S]*?)```|([^\n]+))");
    private static readonly Regex actionPattern = new(@"Action:\s*```(?:json)?([\s\S]*?)```");

    private const string TemplateVariablePattern = @"\{\{\$(.*?)\}\}";

    private readonly IChatClient _chatClient;
    private readonly ChatOptions _chatOptions;
    private readonly AIFunction[] _tools;
    private readonly string _templateContent;
    private readonly Dictionary<string, string> _arguments;
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
        
        _templateContent = Templates.LoadContent("template.txt");
        var list = Format(tools).ToList();
        _arguments = new Dictionary<string, string>
        {
            { "input", input },
            { "tools", string.Join('\n', list.Select(x => x.description)) },
            { "tool_actions", string.Join('\n', list.Select(x => $"{x.description}, args: {x.arguments}")) },
            { "agent_scratchpad", "" }
        };
        _steps = [];
    }

    public bool Completed() => _steps.LastOrDefault()?.HasFinalAnswer() ?? false;
    
    public async Task<ReActStep> Next(CancellationToken cancellationToken = default)
    {
        _arguments["agent_scratchpad"] = BuildScratchpad();
        var message = Regex.Replace(
            _templateContent, 
            TemplateVariablePattern, 
            match =>
            {
                var key = match.Groups[1].Value;
                return _arguments.TryGetValue(key, out var value) ? value : match.Value;
            });

        var options = _chatOptions.Clone();
        options.Tools = default; // Manual tool handling in ReActLoop

        var response = await _chatClient.GetResponseAsync(message, options, cancellationToken);
        var step = Parse(response.Text);

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

    private static ReActStep Parse(string input)
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