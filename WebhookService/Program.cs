using WebhookService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Configure Services
builder.Services.AddSingleton<IFacebookSignatureValidator, FacebookSignatureValidator>();
builder.Services.AddSingleton<IPayloadParserService, PayloadParserService>();
builder.Services.AddSingleton<IKafkaProducerService, KafkaProducerService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseAuthorization();

app.MapControllers();

app.Run("http://localhost:3001");
