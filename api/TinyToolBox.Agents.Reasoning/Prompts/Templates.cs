using Microsoft.SemanticKernel;
using TinyToolBox.Agents.Shared.Resources;

namespace TinyToolBox.Agents.Reasoning.Prompts;

internal sealed class Templates
{
    internal static string LoadContent(string name)
    {
        using var stream = ManifestResources.Load<Templates>(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    
    internal static PromptTemplateConfig LoadConfiguration(string name = "template.txt")
    {
        var content = LoadContent(name);
        return new PromptTemplateConfig(content)
        {
            Description = "Default ReAct prompt template"
        };
    }
}