using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Sentinel.Api.Auth;
using Sentinel.Api.Persistence;
using Sentinel.Core.Interfaces;
using Sentinel.Core.Models;
using Sentinel.Remediation;

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
builder.Services.Configure<RemediationOptions>(
    builder.Configuration.GetSection(RemediationOptions.SectionName));
builder.Services.AddSingleton<IRemediationExecutor, LocalRemediationExecutor>();

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
        Enabled = request.Enabled,
        Action = NormalizeRuleAction(request.Action, request.Name)
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

api.MapPost("/rules/{ruleId:guid}/execute", async (
    SentinelDbContext db,
    IRemediationExecutor remediationExecutor,
    Guid ruleId,
    bool dryRun,
    CancellationToken cancellationToken) =>
{
    var rule = await db.Rules.FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
    if (rule is null)
    {
        return Results.NotFound();
    }

    var actionDefinition = rule.Action ?? new RemediationActionDefinition
    {
        ActionType = ActionType.SendNotification,
        TargetResource = rule.Name,
        Confidence = 92,
        Reason = $"Rule executed: {rule.Name}",
        Channel = NotificationChannel.Email,
        Message = $"Rule '{rule.Name}' executed."
    };

    if (!TryBuildAction(actionDefinition, rule.Name, out var action, out var error))
    {
        return Results.BadRequest(new { error });
    }

    var result = dryRun
        ? new RemediationActionResult
        {
            ActionId = action.Id,
            Status = ActionStatus.Skipped,
            Timestamp = DateTimeOffset.UtcNow,
            Details = "Dry-run"
        }
        : await remediationExecutor.ExecuteAsync(action, cancellationToken);

    ActionEvent? actionEvent = null;
    if (!dryRun)
    {
        actionEvent = new ActionEvent
        {
            Id = Guid.NewGuid(),
            Description = $"{action.ActionType}: {action.TargetResource}",
            Confidence = action.Confidence,
            Timestamp = result.Timestamp,
            Status = result.Status
        };

        db.ActionEvents.Add(actionEvent);
        await db.SaveChangesAsync(cancellationToken);
    }

    return Results.Ok(new RuleExecutionResponse
    {
        Result = result,
        ActionEvent = actionEvent
    });
})
    .WithName("ExecuteRule");

api.MapPost("/remediation/execute", async (
    IRemediationExecutor remediationExecutor,
    SentinelDbContext db,
    RemediationActionDefinition actionDefinition,
    bool dryRun,
    CancellationToken cancellationToken) =>
{
    if (!TryBuildAction(actionDefinition, null, out var action, out var error))
    {
        return Results.BadRequest(new { error });
    }

    var result = dryRun
        ? new RemediationActionResult
        {
            ActionId = action.Id,
            Status = ActionStatus.Skipped,
            Timestamp = DateTimeOffset.UtcNow,
            Details = "Dry-run"
        }
        : await remediationExecutor.ExecuteAsync(action, cancellationToken);

    ActionEvent? actionEvent = null;
    if (!dryRun)
    {
        actionEvent = new ActionEvent
        {
            Id = Guid.NewGuid(),
            Description = $"{action.ActionType}: {action.TargetResource}",
            Confidence = action.Confidence,
            Timestamp = result.Timestamp,
            Status = result.Status
        };

        db.ActionEvents.Add(actionEvent);
        await db.SaveChangesAsync(cancellationToken);
    }

    return Results.Ok(new RemediationExecutionResponse
    {
        Result = result,
        ActionEvent = actionEvent
    });
})
    .WithName("ExecuteRemediation");

static RemediationActionDefinition? NormalizeRuleAction(RemediationActionDefinition? definition, string ruleName)
{
    if (definition is null)
    {
        return null;
    }

    var target = string.IsNullOrWhiteSpace(definition.TargetResource) ? ruleName : definition.TargetResource;
    return definition with { TargetResource = target };
}

static bool TryBuildAction(
    RemediationActionDefinition definition,
    string? fallbackTarget,
    out RemediationAction action,
    out string error)
{
    action = null!;
    error = string.Empty;

    var target = string.IsNullOrWhiteSpace(definition.TargetResource) ? fallbackTarget : definition.TargetResource;
    if (string.IsNullOrWhiteSpace(target))
    {
        error = "TargetResource is required.";
        return false;
    }

    if (definition.Confidence is < 0 or > 100)
    {
        error = "Confidence must be between 0 and 100.";
        return false;
    }

    switch (definition.ActionType)
    {
        case ActionType.RestartService:
            {
                var serviceName = string.IsNullOrWhiteSpace(definition.ServiceName) ? target : definition.ServiceName;
                if (string.IsNullOrWhiteSpace(serviceName))
                {
                    error = "ServiceName is required for RestartService actions.";
                    return false;
                }

                action = new RestartServiceAction
                {
                    Id = Guid.NewGuid(),
                    ActionType = definition.ActionType,
                    TargetResource = target,
                    Confidence = definition.Confidence,
                    Reason = definition.Reason,
                    ServiceName = serviceName
                };
                return true;
            }
        case ActionType.ScaleReplicas:
            {
                if (!definition.DesiredReplicas.HasValue)
                {
                    error = "DesiredReplicas is required for ScaleReplicas actions.";
                    return false;
                }

                action = new ScaleResourceAction
                {
                    Id = Guid.NewGuid(),
                    ActionType = definition.ActionType,
                    TargetResource = target,
                    Confidence = definition.Confidence,
                    Reason = definition.Reason,
                    DesiredReplicas = definition.DesiredReplicas.Value
                };
                return true;
            }
        case ActionType.SendNotification:
            {
                if (!definition.Channel.HasValue || string.IsNullOrWhiteSpace(definition.Message))
                {
                    error = "Channel and Message are required for SendNotification actions.";
                    return false;
                }

                action = new SendNotificationAction
                {
                    Id = Guid.NewGuid(),
                    ActionType = definition.ActionType,
                    TargetResource = target,
                    Confidence = definition.Confidence,
                    Reason = definition.Reason,
                    Channel = definition.Channel.Value,
                    Message = definition.Message
                };
                return true;
            }
        default:
            error = $"Action type {definition.ActionType} is not supported yet.";
            return false;
    }
}

app.Run();
