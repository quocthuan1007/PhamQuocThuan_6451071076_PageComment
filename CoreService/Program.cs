using CoreService.Data;
using CoreService.Services;
using CoreService.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Configure Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration["ConnectionStrings:DefaultConnection"]));

// Configure Memory Cache for Spam Detection
builder.Services.AddMemoryCache();

// Register Services
builder.Services.AddSingleton<ISpamDetectionService, SpamDetectionService>();
builder.Services.AddTransient<IAiClassificationService, MockAiClassificationService>();
builder.Services.AddTransient<IDecisionMakerService, DecisionMakerService>();
builder.Services.AddTransient<IActionExecutorService, ActionExecutorService>();

// Register Worker
builder.Services.AddHostedService<KafkaConsumerWorker>();

var host = builder.Build();

// Ensure DB is created
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

host.Run();
