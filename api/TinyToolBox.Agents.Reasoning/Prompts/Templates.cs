using Microsoft.SemanticKernel;
using TinyToolBox.Agents.Shared.Resources;

namespace TinyToolBox.Agents.Reasoning.Prompts;

internal sealed class Templates
{
    internal static PromptTemplateConfig LoadConfiguration(string name = "template.txt")
    {
        using var stream = ManifestResources.Load<Templates>(name);
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();
        return new PromptTemplateConfig(content)
        {
            Description = "Default ReAct prompt template"
        };
    }
}