using WebhookService.Services;
using SharedDomain;

EnvFileLoader.LoadFromRepoRoot();

var builder = WebApplication.CreateBuilder(args);
ConfigPlaceholderResolver.Apply(builder.Configuration);

// Add services to the container.
builder.Services.AddControllers();

// Configure Services
builder.Services.AddSingleton<KafkaTopicInitializer>();
builder.Services.AddSingleton<IFacebookSignatureValidator, FacebookSignatureValidator>();
builder.Services.AddSingleton<IPayloadParserService, PayloadParserService>();
builder.Services.AddSingleton<IKafkaProducerService, KafkaProducerService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var topicInitializer = scope.ServiceProvider.GetRequiredService<KafkaTopicInitializer>();
    await topicInitializer.EnsureTopicsExistAsync();
}

// Configure the HTTP request pipeline.
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new
{
    service = "webhook-service",
    status = "healthy",
    port = 3001
}));

app.Run("http://localhost:3001");
