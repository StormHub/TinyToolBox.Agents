using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace TinyToolBox.Agents.Reasoning;

internal sealed class ReActLoop
{
    private const string FINAL_ANSWER_ACTION = "Final Answer:";

    private const string SystemPromptTemplate =
        """
        Answer the following questions as best you can. You have access to the following tools:

        {{$tools}}

        Use the following format:

        Question: the input question you must answer
        Thought: you should always think about what to do
        Action: the action to take, should be one of [{{$tool_names}}]
        Action Input: the input to the action
        Observation: the result of the action
        ... (this Thought/Action/Action Input/Observation can repeat N times)
        Thought: I now know the final answer
        Final Answer: the final answer to the original input question

        Begin!

        Question: {{$input}}
        Thought:{{$agent_scratchpad}}
        """;

    private static readonly Regex actionRegex =
        new(@"Action\s*\d*\s*:[\s]*(.*?)[\s]*Action\s*\d*\s*Input\s*\d*\s*:[\s]*([^\r\n]*)", RegexOptions.Singleline);

    private readonly Kernel _kernel;
    private readonly ILogger _logger;
    private readonly int _maxIterations;
    private readonly KernelFunction _reactFunction;

    public ReActLoop(Kernel kernel, string? modelId, int maxIterations, ILogger<ReActLoop> logger)
    {
        _kernel = kernel;
        _maxIterations = maxIterations;
        _logger = logger;

        var executionSettings = new PromptExecutionSettings
        {
            ModelId = modelId,
            ExtensionData = new Dictionary<string, object>
            {
                ["temperature"] = 0
            }
        };
        _reactFunction = kernel.CreateFunctionFromPrompt(SystemPromptTemplate, executionSettings);
    }

    public async Task<ReActResult> Execute(
        string question,
        IEnumerable<AIFunction> tools,
        CancellationToken cancellationToken = default)
    {
        var actions = tools.ToList();

        var result = new ReActResult();
        try
        {
            // Main ReAct loop
            for (var iteration = 0; iteration < _maxIterations; iteration++)
            {
                var arguments = new KernelArguments
                {
                    ["tools"] = Format(actions),
                    ["input"] = question,
                    ["$tool_names"] = string.Join(", ", actions.Select(t => t.Name)),
                    ["agent_scratchpad"] = "" // TODO: accumulate previous steps
                };

                // Step 1: Reasoning - Get the model's thought and action
                var functionResult = await _reactFunction.InvokeAsync(_kernel, arguments, cancellationToken);
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("ReAct iteration {Iteration} prompt: {Prompt}",
                        iteration,
                        functionResult.RenderedPrompt);

                var step = ParseResult(functionResult);
                result.Steps.Add(step);

                // Check if we have a final answer
                if (!string.IsNullOrEmpty(step.FinalAnswer))
                {
                    result.Success = true;
                    break;
                }

                // Step 2: Acting - Execute the action if provided
                if (!string.IsNullOrEmpty(step.Action))
                {
                    var tool = actions.FirstOrDefault(x =>
                        x.Name.Equals(step.Action, StringComparison.OrdinalIgnoreCase));
                    if (tool is not null)
                    {
                        var actionResult = await tool.InvokeAsync(step.ActionInput, cancellationToken);
                        step.Observation = actionResult?.ToString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    private static ReActStep ParseResult(FunctionResult functionResult)
    {
        var input = functionResult.GetValue<string>()?.Trim() ?? string.Empty;
        var includesAnswer = input.Contains(FINAL_ANSWER_ACTION, StringComparison.OrdinalIgnoreCase);

        var actionMatch = actionRegex.Match(input);
        if (actionMatch.Success)
        {
            if (includesAnswer)
                throw new InvalidOperationException(
                    $"Parsing LLM output produced both a final answer and a parse-able action:\n {input}");
            var action = actionMatch.Groups[1].Value;
            var actionInput = actionMatch.Groups[2].Value;

            action = action.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            var arguments = new AIFunctionArguments();
            try
            {
                var dictionary = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(actionInput);
                if (dictionary is not null)
                    foreach (var (key, value) in dictionary)
                        if (value.ValueKind == JsonValueKind.Object)
                            arguments.TryAdd(key, JsonSerializer.Serialize(value, AIJsonUtilities.DefaultOptions));
                        else
                            arguments.TryAdd(key, value);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Error parsing action input JSON: ${ex.Message} {actionInput}");
            }

            return new ReActStep
            {
                OriginalResponse = input,
                Action = action,
                ActionInput = arguments
            };
        }

        if (includesAnswer)
        {
            var finalAnswerIndex = input.IndexOf(FINAL_ANSWER_ACTION, StringComparison.OrdinalIgnoreCase);
            var finalAnswer = input[(finalAnswerIndex + FINAL_ANSWER_ACTION.Length)..].Trim();

            return new ReActStep
            {
                OriginalResponse = input,
                FinalAnswer = finalAnswer
            };
        }

        throw new InvalidOperationException($"Could not parse LLM output: ${input}");
    }

    private static string Format(IReadOnlyCollection<AIFunction> tools)
    {
        var buffer = new StringBuilder();
        foreach (var tool in tools)
        {
            buffer.AppendLine($"{tool.Name} : {tool.Description}");
            if (tool.JsonSchema.TryGetProperty("properties", out var properties))
            {
                var arguments = new List<string>();
                foreach (var property in properties.EnumerateObject())
                {
                    var propName = property.Name;
                    var propDescription = string.Empty;
                    if (property.Value.TryGetProperty("description", out var descProperty))
                        propDescription = descProperty.GetString();
                    if (string.IsNullOrEmpty(propDescription)
                        && property.Value.TryGetProperty("type", out var typeProperty))
                        propDescription = typeProperty.GetString();

                    arguments.Add(
                        !string.IsNullOrEmpty(propDescription)
                            ? $" \"{propName}\" : \"{propDescription}\" "
                            : $" \"{propName}\" ");
                }

                buffer.AppendLine($" - {nameof(arguments)} : {{{string.Join(',', arguments)}}}\n");
            }
        }

        return buffer.ToString();
    }
}