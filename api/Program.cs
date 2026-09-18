using RagExample.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactDev", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var ollamaBaseUrl = new Uri(builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434");

builder.Services.AddHttpClient<OllamaEmbeddingService>(client =>
{
    client.BaseAddress = ollamaBaseUrl;
    client.Timeout = TimeSpan.FromMinutes(2);
});

builder.Services.AddHttpClient<OllamaChatService>(client =>
{
    client.BaseAddress = ollamaBaseUrl;
    client.Timeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddSingleton<VectorStore>();
builder.Services.AddSingleton<ConversationStore>();
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<AgentTools>();
builder.Services.AddScoped<RagChatService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowReactDev");
app.MapControllers();

app.Run();
