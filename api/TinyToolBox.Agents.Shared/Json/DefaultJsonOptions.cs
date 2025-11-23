using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyToolBox.Agents.Shared.Json;

public static class DefaultJsonOptions
{
    static DefaultJsonOptions()
    {
        Value = new JsonSerializerOptions();
        Value.Setup();
    }

    public static JsonSerializerOptions Value { get; }

    public static void Setup(this JsonSerializerOptions jsonSerializerOptions)
    {
        jsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        jsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        jsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        jsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    }
}