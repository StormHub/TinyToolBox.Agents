using Amazon.BedrockRuntime;
using Amazon.Runtime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using OllamaSharp;
using TinyToolBox.Agents.Reasoning;
using TinyToolBox.Agents.Shared.Http;

IHost? host = default;
string[] models = [
    "phi4",
    "anthropic.claude-3-5-sonnet-20241022-v2:0"
];
try
{
    host = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((builderContext, builder) =>
        {
            builder.AddJsonFile("appsettings.json", false);
            builder.AddJsonFile($"appsettings.{builderContext.HostingEnvironment.EnvironmentName}.json", true);

            if (builderContext.HostingEnvironment.IsDevelopment()) builder.AddUserSecrets<Program>();

            builder.AddEnvironmentVariables();
        })
        .ConfigureServices((builderContext, services) =>
        {
            services.AddTransient<TraceHttpHandler>();

            // Ollama
            
            services
                .AddHttpClient(models[0])
                .AddHttpMessageHandler<TraceHttpHandler>()
                .ConfigureHttpClient(client => { client.BaseAddress = new Uri("http://localhost:11434"); });
            services.AddKeyedTransient<IChatClient>(models[0],
                (provider, _) =>
                {
                    var factory = provider.GetRequiredService<IHttpClientFactory>();
                    var httpClient = factory.CreateClient(nameof(OllamaApiClient));
                    var ollamaApiClient = new OllamaApiClient(httpClient, models[0]);
                    return ollamaApiClient;
                });

            // Bedrock
            var bedrockConfig = 
                builderContext.Configuration
                    .GetSection(nameof(BedrockConfiguration))
                    .Get<BedrockConfiguration>()
                ?? throw new InvalidOperationException($"AWS {nameof(BedrockConfiguration)} configuration required");
            services
                .AddHttpClient(models[1])
                .AddHttpMessageHandler<TraceHttpHandler>();

            services.AddKeyedTransient<IChatClient>(models[1],
                (provider, key) =>
                {
                    var credentials = new SessionAWSCredentials(
                        bedrockConfig.KeyId,
                        bedrockConfig.AccessKey,
                        bedrockConfig.Token);

                    var regionEndpoint = bedrockConfig.RequireRegionEndpoint();
                    var factory = provider.GetRequiredService<IHttpClientFactory>();

                    var runtimeConfig = new AmazonBedrockRuntimeConfig
                    {
                        RegionEndpoint = regionEndpoint,
                        HttpClientFactory = new BedrockHttpClientFactory(factory, key?.ToString()!)
                    };

                    var client = new AmazonBedrockRuntimeClient(credentials, runtimeConfig);
                    return client.AsIChatClient();
                });

            foreach (var model in models)
            {
                // Semantic Kernel
                services.AddKeyedTransient<IKernelBuilder>(model, (provider, key) =>
                {
                    var builder = Kernel.CreateBuilder();

                    var keyId = key?.ToString()!;
                    
                    var chatClient = provider.GetRequiredKeyedService<IChatClient>(keyId);
                    builder.Services.AddKeyedChatClient(keyId, chatClient);
                    builder.Services.AddSingleton(provider.GetRequiredService<ILoggerFactory>());

                    return builder;
                });
                
                // ReAct loop
                services.AddKeyedTransient<ReActLoop>(model, (provider, key) =>
                {
                    var keyId = key?.ToString()!;
                    
                    var builder = provider.GetRequiredKeyedService<IKernelBuilder>(keyId);
                    var factory = provider.GetRequiredService<ILoggerFactory>();

                    var kernel = builder.Build();
                    return new ReActLoop(kernel, keyId, 10, factory.CreateLogger<ReActLoop>());
                });
            }
        })
        .UseConsoleLifetime()
        .Build();

    await host.StartAsync();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

    await using var scope = host.Services.CreateAsyncScope();
    var loop = scope.ServiceProvider.GetRequiredKeyedService<ReActLoop>(models[1]); // Bedrock
    var tools = MathFunctions.Create();
    var result = await loop.Execute("How many seconds are in 1:23:45", tools, lifetime.ApplicationStopping);
    Console.WriteLine(result.Success ? $"Final Answer: {result.FinalAnswer}" : $"Error: {result.Error}");
    lifetime.StopApplication();

    await host.WaitForShutdownAsync(lifetime.ApplicationStopping);
}
catch (Exception ex)
{
    Console.WriteLine($"Host terminated unexpectedly! \n{ex}");
}
finally
{
    host?.Dispose();
}