using BackendApi.Data;
using BackendApi.Services;
using BackendApi.Workers;
using Microsoft.EntityFrameworkCore;
using SharedDomain;

EnvFileLoader.LoadFromRepoRoot();

var builder = WebApplication.CreateBuilder(args);
ConfigPlaceholderResolver.Apply(builder.Configuration);

builder.Services.AddDbContext<BackendDbContext>(options =>
    options.UseSqlite(builder.Configuration["ConnectionStrings:DefaultConnection"]));

builder.Services.AddSingleton<KafkaTopicInitializer>();
builder.Services.AddSingleton<IFacebookGraphApiService, FacebookGraphApiService>();
builder.Services.AddHostedService<BackendCommandWorker>();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

using (var topicScope = app.Services.CreateScope())
{
    var topicInitializer = topicScope.ServiceProvider.GetRequiredService<KafkaTopicInitializer>();
    await topicInitializer.EnsureTopicsExistAsync();
}

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BackendDbContext>();
    dbContext.Database.EnsureCreated();
}

app.MapGet("/health", () => Results.Ok(new
{
    service = "backend-api",
    status = "healthy",
    port = 3000
}));

app.MapGet("/api/processed-commands", async (BackendDbContext dbContext) =>
{
    var commands = await dbContext.ProcessedCommands
        .OrderByDescending(x => x.ProcessedAtUtc)
        .Take(50)
        .ToListAsync();

    return Results.Ok(commands);
});

app.Run("http://localhost:3000");
