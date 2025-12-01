using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using TinyToolBox.Agents.Reasoning.Prompts;

namespace TinyToolBox.Agents.Reasoning;

internal sealed class ReActContext
{
    private static readonly Regex finalAnswerPattern = new(@"Final Answer:\s*(?:```([\s\S]*?)```|([^\n]+))");
    private static readonly Regex actionPattern = new(@"Action:\s*```(?:json)?([\s\S]*?)```");
    private readonly string _actions;

    private readonly string _input;
    private readonly Kernel _kernel;
    private readonly KernelFunction _kernelFunction;
    private readonly ILogger _logger;
    private readonly List<ReActStep> _steps;
    private readonly string _tools;

    public ReActContext(string input, Kernel kernel, params List<ReActStep> stepsTaken)
    {
        _input = input;
        _kernel = kernel;
        _steps = [..stepsTaken];
        _logger = kernel.LoggerFactory.CreateLogger<ReActContext>();

        var templateConfig = Templates.LoadConfiguration();
        _kernelFunction = kernel.CreateFunctionFromPrompt(templateConfig);

        var tools = Format(kernel.Plugins).ToList();
        _tools = string.Join('\n', tools.Select(x => x.description));
        _actions = string.Join('\n', tools.Select(x => $"{x.description}, args {x.arguments}"));
    }

    public bool Completed()
    {
        return _steps.LastOrDefault()?.HasFinalAnswer() ?? false;
    }

    public async Task<ReActStep> Next(
        PromptExecutionSettings promptExecutionSettings,
        CancellationToken cancellationToken = default)
    {
        var step = _steps.LastOrDefault();
        if (step?.HasFinalAnswer() ?? false) return step;

        var arguments = new KernelArguments(promptExecutionSettings)
        {
            ["input"] = _input,
            ["tools"] = _tools,
            ["tool_actions"] = _actions,
            ["agent_scratchpad"] = BuildScratchpad()
        };

        var functionResult = await _kernelFunction.InvokeAsync(_kernel, arguments, cancellationToken);
        step = Parse(functionResult);
        if (!step.HasFinalAnswer())
        {
            Debug.Assert(step.Action != null, "Step action should not be null if final answer is not present.");
            _logger.LogInformation("Invoking action: {Action}", step.Action);
            var actionResult = await step.Action.Invoke(_kernel, cancellationToken);
            step.Observation = actionResult.ToString();
        }

        _steps.Add(step);
        return step;
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

            buffer.Insert(0, content);
        }

        return buffer.ToString();
    }

    private static ReActStep Parse(FunctionResult functionResult)
    {
        var input = functionResult.GetValue<string>()?.Trim() ?? string.Empty;

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

    private static IEnumerable<(string description, string arguments)> Format(KernelPluginCollection tools)
    {
        var functions = tools.GetFunctionsMetadata();
        foreach (var function in functions)
        {
            var summary = $"{function.PluginName}.{function.Name} : {function.Description}";
            var arguments = string.Join(',',
                function.Parameters
                    .Select(x =>
                    {
                        if (x.Schema?.RootElement.TryGetProperty("type", out var typeProperty) ?? false)
                            return $" \"{x.Name}\" : \"{typeProperty.GetString()}\" ";

                        return $" {x.Name} ";
                    }));
            yield return (summary, $"{{{arguments}}}");
        }
    }
}