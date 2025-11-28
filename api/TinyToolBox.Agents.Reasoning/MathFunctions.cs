using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace TinyToolBox.Agents.Reasoning;

internal static class MathFunctions
{
    public static IEnumerable<AIFunction> Create(JsonSerializerOptions? serializerOptions = null)
    {
        var options = new AIFunctionFactoryOptions
        {
            SerializerOptions = serializerOptions ?? AIJsonUtilities.DefaultOptions
        };

        yield return AIFunctionFactory.Create(Add, options);
        yield return AIFunctionFactory.Create(Multiply, options);
    }

    [Description("Add two numbers")]
    public static double Add(
        [Description("The first number to add")]
        double number1,
        [Description("The second number to add")]
        double number2
    )
    {
        return number1 + number2;
    }

    [Description("Multiply two numbers.")]
    public static double Multiply(
        [Description("The first number to multiply")]
        double number1,
        [Description("The second number to multiply")]
        double number2
    )
    {
        return number1 * number2;
    }
}