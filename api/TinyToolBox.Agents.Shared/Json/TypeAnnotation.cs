using System.Text.Json;
using Microsoft.Extensions.AI;

namespace TinyToolBox.Agents.Shared.Json;

public static class TypeAnnotation
{
    /*
     "object": Represents a JSON object ({}).
     "array": Represents a JSON array ([]).
     "string": Represents a string.
     "number": Represents any number (integer or floating-point).
     "integer": Represents an integer. It is a more specific kind of "number".
     "boolean": Represents a boolean value (true or false).
     "null": Represents the null literal.
     */
    public static string From(Type type, JsonSerializerOptions? serializerOptions = null)
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
                        ? $"must be one of: {string.Join("; ", Enum.GetNames(type))}" 
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