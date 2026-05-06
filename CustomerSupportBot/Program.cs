using CustomerSupportBot.Endpoints;
using CustomerSupportBot.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddLogging();
builder.Services.AddAiServices(builder.Configuration);
builder.Services.AddPersistenceServices(builder.Configuration);
builder.Services.AddApplicationServices();
builder.Services.AddAuthenticationServices(builder.Configuration);

var app = builder.Build();

await app.MigrateIfDevelopmentAsync();

app.UseCors();
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapChatEndpoints();
app.MapSessionEndpoints();

var adminScope = app.MapGroup("").RequireAuthorization("Admin");
adminScope.MapAdminEndpoints();
adminScope.MapTraceEndpoints();
adminScope.MapEvaluationEndpoints();
adminScope.MapMemoryEndpoints();
adminScope.MapImprovementsEndpoints();

app.MapAnalyticsEndpoints();

app.Run();

public partial class Program { }
