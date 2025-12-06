using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace TinyToolBox.Agents.Reasoning.Prompts;

internal sealed class StringTemplate(string template, JsonSerializerOptions? jsonSerializerOptions = null)
{
    private const string TemplateVariablePattern = @"\{\{\$(.*?)\}\}";

    public string Format(Dictionary<string, object> arguments)
    {
        return Regex.Replace(
            template,
            TemplateVariablePattern,
            match =>
            {
                var key = match.Groups[1].Value;
                if (arguments.TryGetValue(key, out var value))
                {
                    if (value is string stringValue)
                    {
                        return stringValue;
                    }

                    var json = JsonSerializer.Serialize(value, jsonSerializerOptions);
                    return HttpUtility.HtmlEncode(json);
                }

                return match.Value;
            });
    }
}