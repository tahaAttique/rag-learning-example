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

var chatBaseUrl = new Uri(builder.Configuration["Chat:BaseUrl"]
    ?? throw new InvalidOperationException("Chat:BaseUrl is not configured."));

// Chat and embeddings both go through Gemini's OpenAI-compatible endpoint, so both typed
// clients share the same base URL; OpenAiCompatibleChatService also works unchanged against
// Groq, Cerebras, OpenRouter, etc. if Chat:BaseUrl/Model/ApiKey are pointed elsewhere.
// Both clients retry rate-limit and overload responses. The 2-minute timeout covers the
// whole call including retries, so a provider that stays down fails in bounded time.
builder.Services.AddTransient<RetryTransientHandler>();

builder.Services.AddHttpClient<IChatService, OpenAiCompatibleChatService>(client =>
{
    client.BaseAddress = chatBaseUrl;
    client.Timeout = TimeSpan.FromMinutes(2);
}).AddHttpMessageHandler<RetryTransientHandler>();

builder.Services.AddHttpClient<OpenAiCompatibleEmbeddingService>(client =>
{
    client.BaseAddress = chatBaseUrl;
    client.Timeout = TimeSpan.FromMinutes(2);
}).AddHttpMessageHandler<RetryTransientHandler>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProviderErrorHandler>();

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

app.UseExceptionHandler();
app.UseCors("AllowReactDev");
app.MapControllers();

app.Run();
