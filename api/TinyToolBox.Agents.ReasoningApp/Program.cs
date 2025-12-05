using Amazon.BedrockRuntime;
using Amazon.Runtime;
// using Microsoft.Agents.AI;
// using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using OllamaSharp;
using TinyToolBox.Agents.Reasoning;
using TinyToolBox.Agents.ReasoningApp;
using TinyToolBox.Agents.Shared.Http;

IHost? host = default;
try
{
    const string localModel = "phi4";
    const string bedrockModel = "anthropic.claude-3-5-sonnet-20241022-v2:0";
    string[] models =
    [
        localModel,
        bedrockModel
    ];

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
                .AddHttpClient(localModel)
                .AddHttpMessageHandler<TraceHttpHandler>()
                .ConfigureHttpClient(client => { client.BaseAddress = new Uri("http://localhost:11434"); });
            services.AddKeyedTransient<IChatClient>(localModel,
                (provider, _) =>
                {
                    var factory = provider.GetRequiredService<IHttpClientFactory>();
                    var httpClient = factory.CreateClient(localModel);
                    var ollamaApiClient = new OllamaApiClient(httpClient, localModel);
                    return ollamaApiClient;
                });

            // Bedrock
            var bedrockConfig =
                builderContext.Configuration
                    .GetSection(nameof(BedrockConfiguration))
                    .Get<BedrockConfiguration>()
                ?? throw new InvalidOperationException($"AWS {nameof(BedrockConfiguration)} configuration required");
            services
                .AddHttpClient(bedrockModel)
                .AddHttpMessageHandler<TraceHttpHandler>();

            services.AddKeyedTransient<IChatClient>(bedrockModel,
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
                        HttpClientFactory = new BedrockHttpClientFactory(factory, bedrockModel)
                    };

                    var client = new AmazonBedrockRuntimeClient(credentials, runtimeConfig);
                    return client.AsIChatClient();
                });

            // Semantic Kernel
            services.AddTransient<IKernelBuilder>(provider =>
            {
                var builder = Kernel.CreateBuilder();
                foreach (var model in models)
                {
                    var chatClient = provider.GetRequiredKeyedService<IChatClient>(model);
                    builder.Services.AddKeyedChatClient(model, chatClient);
                }

                builder.Services.AddSingleton(provider.GetRequiredService<ILoggerFactory>());
                builder.Plugins.AddFromType<MathFunctions>();

                return builder;
            });
        })
        .UseConsoleLifetime()
        .Build();

    await host.StartAsync();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    await using var scope = host.Services.CreateAsyncScope();

    var chatClient = scope.ServiceProvider.GetRequiredKeyedService<IChatClient>(localModel);
    var loop = new ReActLoop(
        input: "What is 12 multiplied by 15, plus 7?",
        chatClient, 
        new ChatOptions
        {
            ModelId = localModel,
            Temperature = 0,
            StopSequences = ["Observation:"],
            Tools = [ 
                AIFunctionFactory.Create(MathFunctions.Add),
                AIFunctionFactory.Create(MathFunctions.Multiply)
            ],
        },  
        loggerFactory: scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

    ReActStep? currentStep = default;
    while (!loop.Completed())
    {
        currentStep = await loop.Next(lifetime.ApplicationStopping);
    }

    if (currentStep is not null)
    {
        Console.WriteLine(currentStep.FinalAnswer);
    }

    /*
    var builder = scope.ServiceProvider.GetRequiredService<IKernelBuilder>();
    var promptExecutionSettings = new PromptExecutionSettings
    {
        ModelId = localModel,
        ExtensionData = new Dictionary<string, object>
        {
            ["temperature"] = 0,
            ["stop_sequences"] = new[] { "Observation:" } // Prevent the model from generating answers directly
        }
    };
    
    var kernel = builder.Build();
    var context = new ReActContext(
        "What is 12 multiplied by 15, plus 7?",
        kernel);

    while (!context.Completed())
    {
        await context.Next(promptExecutionSettings, lifetime.ApplicationStopping);
    }

    foreach (var step in context.Steps)
    {
        Console.WriteLine($"{step.Thought} {step.Observation}");
        if (step.HasFinalAnswer()) Console.WriteLine($"Final Answer: {step.FinalAnswer}");
    }
    */

    /*
    var startExecutor = new ChatForwardingExecutor("Start",
        new ChatForwardingExecutorOptions
        {
            StringMessageChatRole = ChatRole.User
        });
    var reactExecutor = new ReActExecutor("ReAct", builder, promptExecutionSettings);
    var workflow = new WorkflowBuilder(startExecutor)
        .AddEdge(startExecutor, reactExecutor)
        .WithOutputFrom(reactExecutor)
        .Build();

    await using var run = await InProcessExecution.StreamAsync(workflow,
        "What is 12 multiplied by 15, plus 7?",
        cancellationToken: lifetime.ApplicationStopping);
    await foreach (var workFlowEvent in run.WatchStreamAsync(lifetime.ApplicationStopping))
    {
        switch (workFlowEvent)
        {
            case StepCompleted stepCompleted:
                Console.WriteLine(stepCompleted.AsReActStep().Thought);
                break;

            case WorkflowOutputEvent outputEvent:
                Console.WriteLine($"{outputEvent}");
                break;
        }
    }
    */

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