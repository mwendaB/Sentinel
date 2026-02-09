using Microsoft.AspNetCore.Authentication;
using Sentinel.Api.Auth;
using Sentinel.Api.Services;
using Sentinel.Core.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.Configure<ApiKeyOptions>(
    builder.Configuration.GetSection(ApiKeyOptions.SectionName));
builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Ingestion", policy => policy.RequireRole("Ingestion"));
});
builder.Services.AddSingleton<LogDataStore>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");

api.MapGet("/metrics", (LogDataStore store) => store.GetMetrics())
    .WithName("GetMetrics");

api.MapGet("/logs", (LogDataStore store, int count) => store.GetLogs(count))
    .WithName("GetLogs");

api.MapPost("/logs", (LogDataStore store, LogEvent logEvent) =>
{
    store.AddLog(logEvent);
    return Results.Accepted();
})
    .RequireAuthorization("Ingestion")
    .WithName("AddLog");

api.MapGet("/actions", (LogDataStore store, int count) => store.GetActions(count))
    .WithName("GetActions");

api.MapPost("/actions", (LogDataStore store, ActionEvent actionEvent) =>
{
    store.AddAction(actionEvent);
    return Results.Accepted();
})
    .RequireAuthorization("Ingestion")
    .WithName("AddAction");

api.MapGet("/rules", (LogDataStore store) => store.Rules)
    .WithName("GetRules");

api.MapPost("/rules", (LogDataStore store, NewRuleRequest request) => Results.Created("/api/rules", store.AddRule(request)))
    .WithName("AddRule");

api.MapPost("/rules/{ruleId:guid}/toggle", (LogDataStore store, Guid ruleId) =>
{
    var rule = store.ToggleRule(ruleId);
    return rule is null ? Results.NotFound() : Results.Ok(rule);
})
    .WithName("ToggleRule");

api.MapPost("/rules/{ruleId:guid}/test", (LogDataStore store, Guid ruleId) => store.TestRule(ruleId))
    .WithName("TestRule");

api.MapPost("/rules/{ruleId:guid}/execute", (LogDataStore store, Guid ruleId, bool dryRun) => store.ExecuteRule(ruleId, dryRun))
    .WithName("ExecuteRule");

app.Run();
