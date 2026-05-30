using Microsoft.Extensions.Hosting;
using RetryService.Services;
using RetryService.Workers;
using SharedDomain;

EnvFileLoader.LoadFromRepoRoot();

var builder = WebApplication.CreateBuilder(args);
ConfigPlaceholderResolver.Apply(builder.Configuration);
builder.Services.AddSingleton<KafkaTopicInitializer>();
builder.Services.AddHostedService<RetryWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var topicInitializer = scope.ServiceProvider.GetRequiredService<KafkaTopicInitializer>();
    await topicInitializer.EnsureTopicsExistAsync();
}

app.MapGet("/health", () => Results.Ok(new
{
    service = "retry-service",
    status = "healthy",
    port = 3003
}));

app.Run("http://localhost:3003");
