using System.Text.Json;
using Microsoft.Extensions.AI;

namespace TinyToolBox.Agents.Shared.Json;

public static class TypeAnnotation
{
    public static string Describe(Type type, JsonSerializerOptions? serializerOptions = null)
    {
        var jsonSchema = AIJsonUtilities.CreateJsonSchema(type, serializerOptions: serializerOptions);
        var typeProperty = jsonSchema.GetProperty("type");
        var value = typeProperty.GetString();
        if (value is not null)
        {
            switch (value)
            {
                case "string":
                    return type.IsEnum 
                        ? $"must be one of: {string.Join("; ", Enum.GetNames(type).Select(x => $"'{x.ToLower()}'"))}" 
                        : string.Empty; // Simple string does not need annotation

                case "number" or "integer":
                    return "must be a single 'number' value";

                case "array":
                    return "must adhere to the JSON schema for an array";
                
                case "boolean":
                    return "must be True or False";
            }
        }
        
        // Assume complex object or other types
        return $"must adhere to the JSON schema: {jsonSchema.GetRawText()}";
    }
}