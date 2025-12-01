using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace TinyToolBox.Agents.ReasoningApp;

internal class MathFunctions
{
    [KernelFunction]
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

    [KernelFunction]
    [Description("Multiply two numbers")]
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