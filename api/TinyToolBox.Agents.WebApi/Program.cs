using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.Extensions.AI;
using OllamaSharp;
using TinyToolBox.Agents.Shared.Http;
using TinyToolBox.Agents.Shared.Json;
using TinyToolBox.Agents.WebApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options => { options.SerializerOptions.Setup(); });
builder.Services.AddAGUI();

// Http client
builder.Services.AddTransient<TraceHttpHandler>();
builder.Services
    .AddHttpClient(nameof(OllamaApiClient))
    .AddHttpMessageHandler<TraceHttpHandler>()
    .ConfigureHttpClient(client => { client.BaseAddress = new Uri("http://localhost:11434"); });

// Ollama
builder.Services.AddTransient<IChatClient>(provider =>
{
    var factory = provider.GetRequiredService<IHttpClientFactory>();
    var httpClient = factory.CreateClient(nameof(OllamaApiClient));
    var ollamaApiClient = new OllamaApiClient(httpClient, "phi4");
    return new OllamaChatClient(ollamaApiClient);
});

// AI Agent
builder.Services.AddKeyedTransient<ChatClientAgent>(
    "local-ollama-agent",
    (provider, key) =>
    {
        var agentOption = new ChatClientAgentOptions
        {
            Id = key.ToString(),
            Name = "Local Assistant",
            Description = "An AI agent on local Ollama.",
            ChatOptions = new ChatOptions
            {
                Temperature = 0
            }
        };

        var agent = provider.GetRequiredService<IChatClient>()
            .CreateAIAgent(agentOption, provider.GetRequiredService<ILoggerFactory>());
        return agent;
    });
var app = builder.Build();

var agent = app.Services.GetRequiredKeyedService<ChatClientAgent>("local-ollama-agent");
app.MapAGUI("/", agent);

await app.RunAsync();