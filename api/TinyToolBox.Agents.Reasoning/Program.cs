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
                .AddHttpClient("phi4")
                .AddHttpMessageHandler<TraceHttpHandler>()
                .ConfigureHttpClient(client => { client.BaseAddress = new Uri("http://localhost:11434"); });
            services.AddKeyedTransient<IChatClient>("phi4",
                (provider, _) =>
                {
                    var factory = provider.GetRequiredService<IHttpClientFactory>();
                    var httpClient = factory.CreateClient(nameof(OllamaApiClient));
                    var ollamaApiClient = new OllamaApiClient(httpClient, "phi4");
                    return ollamaApiClient;
                });

            // Bedrock
            var bedrockConfig = builderContext.Configuration
                                    .GetSection(nameof(BedrockConfiguration))
                                    .Get<BedrockConfiguration>()
                                ?? throw new InvalidOperationException(
                                    $"AWS {nameof(BedrockConfiguration)} configuration required");
            services
                .AddHttpClient(bedrockConfig.ModelId)
                .AddHttpMessageHandler<TraceHttpHandler>();

            services.AddKeyedTransient<IChatClient>(bedrockConfig.ModelId,
                (provider, _) =>
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
                        HttpClientFactory = new BedrockHttpClientFactory(factory, bedrockConfig.ModelId)
                    };

                    var client = new AmazonBedrockRuntimeClient(credentials, runtimeConfig);
                    return client.AsIChatClient();
                });

            // Semantic Kernel
            services.AddTransient<IKernelBuilder>(provider =>
            {
                var builder = Kernel.CreateBuilder();

                var chatClient = provider.GetRequiredKeyedService<IChatClient>(bedrockConfig.ModelId);
                builder.Services.AddKeyedChatClient(bedrockConfig.ModelId, chatClient);
                builder.Services.AddSingleton(provider.GetRequiredService<ILoggerFactory>());

                return builder;
            });

            // ReAct loop
            services.AddTransient<ReActLoop>(provider =>
            {
                var builder = provider.GetRequiredService<IKernelBuilder>();
                var factory = provider.GetRequiredService<ILoggerFactory>();

                var kernel = builder.Build();
                return new ReActLoop(kernel, bedrockConfig.ModelId, 10, factory.CreateLogger<ReActLoop>());
            });
        })
        .UseConsoleLifetime()
        .Build();

    await host.StartAsync();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

    await using var scope = host.Services.CreateAsyncScope();
    var loop = scope.ServiceProvider.GetRequiredService<ReActLoop>();
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