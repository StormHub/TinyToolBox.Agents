using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using OllamaSharp;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddAGUI();

var httpClientBuilder = builder.Services
    .AddHttpClient(nameof(OllamaApiClient))
    .ConfigureHttpClient(client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");
});
httpClientBuilder.Services.AddLogging();
builder.Services.AddTransient<IChatClient>(provider =>
{
    var factory = provider.GetRequiredService<IHttpClientFactory>();
    var httpClient = factory.CreateClient(nameof(OllamaApiClient));
    var ollamaApiClient = new OllamaApiClient(httpClient, "phi4");
    return ollamaApiClient;
});

var app = builder.Build();

var chatClient = app.Services.GetRequiredService<IChatClient>();
var agent = chatClient.CreateAIAgent(
    name: "Local Assistant", 
    description: "An AI agent on local Ollama.");

app.MapAGUI("/", agent);
await app.RunAsync();