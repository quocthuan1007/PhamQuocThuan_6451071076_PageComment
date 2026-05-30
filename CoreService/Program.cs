using CoreService.Data;
using CoreService.Services;
using CoreService.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedDomain;

EnvFileLoader.LoadFromRepoRoot();

var builder = WebApplication.CreateBuilder(args);
ConfigPlaceholderResolver.Apply(builder.Configuration);

// Configure Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration["ConnectionStrings:DefaultConnection"]));

// Configure Memory Cache for Spam Detection
builder.Services.AddMemoryCache();

// Register Services
builder.Services.AddSingleton<KafkaTopicInitializer>();
builder.Services.AddSingleton<ISpamDetectionService, SpamDetectionService>();
builder.Services.AddTransient<KeywordFallbackClassificationService>();
builder.Services.AddTransient<IAiClassificationService, GeminiClassificationService>();
builder.Services.AddTransient<IReplyGenerationService, GeminiReplyGenerationService>();
builder.Services.AddTransient<IDecisionMakerService, DecisionMakerService>();
builder.Services.AddSingleton<IActionExecutorService, ActionExecutorService>();

// Register Worker
builder.Services.AddHostedService<KafkaConsumerWorker>();

var app = builder.Build();

using (var topicScope = app.Services.CreateScope())
{
    var topicInitializer = topicScope.ServiceProvider.GetRequiredService<KafkaTopicInitializer>();
    await topicInitializer.EnsureTopicsExistAsync();
}

// Ensure DB is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/health", () => Results.Ok(new
{
    service = "core-service",
    status = "healthy",
    port = 3002
}));

app.Run("http://localhost:3002");
