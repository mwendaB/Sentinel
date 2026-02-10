using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Sentinel.Api.Auth;
using Sentinel.Api.Persistence;
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
builder.Services.AddDbContext<SentinelDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("SentinelDb")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SentinelDbContext>();
    db.Database.Migrate();
}

var api = app.MapGroup("/api");

api.MapGet("/metrics", async (SentinelDbContext db) =>
{
    var recentLogs = await db.LogEvents
        .OrderByDescending(log => log.Timestamp)
        .Take(500)
        .ToListAsync();

    var recentActions = await db.ActionEvents
        .OrderByDescending(action => action.Timestamp)
        .Take(500)
        .ToListAsync();

    var eventsLastMinute = recentLogs.Count(log => log.Timestamp >= DateTimeOffset.UtcNow.AddMinutes(-1));
    var successActions = recentActions.Count(action => action.Status == ActionStatus.Success);
    var totalActions = recentActions.Count;

    return new MetricsSnapshot
    {
        EventsPerSecond = eventsLastMinute / 60,
        ActiveRules = await db.Rules.CountAsync(rule => rule.Enabled),
        ActionsToday = await db.ActionEvents.CountAsync(),
        ActiveSources = recentLogs.Select(log => log.Source.Name).Distinct().Count(),
        RulesEvaluated = 0,
        ActionSuccessRate = totalActions == 0 ? 100 : (int)Math.Round(successActions * 100.0 / totalActions)
    };
})
    .WithName("GetMetrics");

api.MapGet("/logs", async (SentinelDbContext db, int count) =>
    await db.LogEvents
        .OrderByDescending(log => log.Timestamp)
        .Take(count)
        .ToListAsync())
    .WithName("GetLogs");

api.MapPost("/logs", async (SentinelDbContext db, LogEvent logEvent) =>
{
    db.LogEvents.Add(logEvent);
    await db.SaveChangesAsync();
    return Results.Accepted();
})
    .RequireAuthorization("Ingestion")
    .WithName("AddLog");

api.MapGet("/actions", async (SentinelDbContext db, int count) =>
    await db.ActionEvents
        .OrderByDescending(action => action.Timestamp)
        .Take(count)
        .ToListAsync())
    .WithName("GetActions");

api.MapPost("/actions", async (SentinelDbContext db, ActionEvent actionEvent) =>
{
    db.ActionEvents.Add(actionEvent);
    await db.SaveChangesAsync();
    return Results.Accepted();
})
    .RequireAuthorization("Ingestion")
    .WithName("AddAction");

api.MapGet("/rules", async (SentinelDbContext db) => await db.Rules.ToListAsync())
    .WithName("GetRules");

api.MapPost("/rules", async (SentinelDbContext db, NewRuleRequest request) =>
{
    var rule = new RuleDefinition
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        Pattern = request.Pattern,
        MinimumLevel = request.MinimumLevel,
        Enabled = request.Enabled
    };

    db.Rules.Add(rule);
    await db.SaveChangesAsync();
    return Results.Created("/api/rules", rule);
})
    .WithName("AddRule");

api.MapPost("/rules/{ruleId:guid}/toggle", async (SentinelDbContext db, Guid ruleId) =>
{
    var rule = await db.Rules.FirstOrDefaultAsync(r => r.Id == ruleId);
    return rule is null ? Results.NotFound() : Results.Ok(rule);
})
    .WithName("ToggleRule");

api.MapPost("/rules/{ruleId:guid}/test", async (SentinelDbContext db, Guid ruleId) =>
{
    var rule = await db.Rules.FirstOrDefaultAsync(r => r.Id == ruleId);
    if (rule is null)
    {
        return Results.NotFound();
    }

    var recentLogs = await db.LogEvents
        .OrderByDescending(log => log.Timestamp)
        .Take(500)
        .ToListAsync();

    var regex = new System.Text.RegularExpressions.Regex(
        rule.Pattern,
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    var matches = recentLogs
        .Where(log => log.Level >= rule.MinimumLevel && regex.IsMatch(log.Message))
        .Take(50)
        .ToList();

    return Results.Ok(new RuleTestResult
    {
        RuleId = ruleId,
        MatchCount = matches.Count,
        Matches = matches
    });
})
    .WithName("TestRule");

api.MapPost("/rules/{ruleId:guid}/execute", async (SentinelDbContext db, Guid ruleId, bool dryRun) =>
{
    var rule = await db.Rules.FirstOrDefaultAsync(r => r.Id == ruleId);
    if (rule is null)
    {
        return Results.NotFound();
    }

    var result = new RemediationActionResult
    {
        ActionId = Guid.NewGuid(),
        Status = dryRun ? ActionStatus.Skipped : ActionStatus.Success,
        Timestamp = DateTimeOffset.UtcNow,
        Details = dryRun ? "Dry-run" : "Executed"
    };

    ActionEvent? actionEvent = null;
    if (!dryRun)
    {
        actionEvent = new ActionEvent
        {
            Id = Guid.NewGuid(),
            Description = $"Executed rule: {rule.Name}",
            Confidence = 92,
            Timestamp = result.Timestamp,
            Status = ActionStatus.Success
        };

        db.ActionEvents.Add(actionEvent);
        await db.SaveChangesAsync();
    }

    return Results.Ok(new RuleExecutionResponse
    {
        Result = result,
        ActionEvent = actionEvent
    });
})
    .WithName("ExecuteRule");

app.Run();
