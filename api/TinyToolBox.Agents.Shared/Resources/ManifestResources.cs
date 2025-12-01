using System.Reflection;

namespace TinyToolBox.Agents.Shared.Resources;

public static class ManifestResources
{
    public static Stream Load<T>(string name)
    {
        var typeInfo = typeof(T).GetTypeInfo();
        return typeInfo.Assembly.GetManifestResourceStream($"{typeInfo.Namespace}." + name)
               ?? throw new InvalidOperationException($"Resource '{name}' not found in assembly '{typeInfo.Assembly.FullName}'.");
    }
}