using Sentinel.Api.Services;
using Sentinel.Core.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSingleton<LogDataStore>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

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
    .WithName("AddLog");

api.MapGet("/actions", (LogDataStore store, int count) => store.GetActions(count))
    .WithName("GetActions");

api.MapPost("/actions", (LogDataStore store, ActionEvent actionEvent) =>
{
    store.AddAction(actionEvent);
    return Results.Accepted();
})
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
