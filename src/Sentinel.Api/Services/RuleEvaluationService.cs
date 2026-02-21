using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Sentinel.Api.Persistence;
using Sentinel.Core.Interfaces;
using Sentinel.Core.Models;

namespace Sentinel.Api.Services;

public sealed class RuleEvaluationService : BackgroundService
{
    private readonly ChannelReader<LogEvent> _reader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRuleEngine _ruleEngine;
    private readonly IRemediationExecutor _remediationExecutor;
    private readonly ILogger<RuleEvaluationService> _logger;

    public RuleEvaluationService(
        ChannelReader<LogEvent> reader,
        IServiceScopeFactory scopeFactory,
        IRuleEngine ruleEngine,
        IRemediationExecutor remediationExecutor,
        ILogger<RuleEvaluationService> logger)
    {
        _reader = reader;
        _scopeFactory = scopeFactory;
        _ruleEngine = ruleEngine;
        _remediationExecutor = remediationExecutor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var logEvent in _reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await EvaluateLogAsync(logEvent, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Rule evaluation failed for log {LogId}.", logEvent.Id);
            }
        }
    }

    private async Task EvaluateLogAsync(LogEvent logEvent, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SentinelDbContext>();

        var rules = await db.Rules
            .Where(rule => rule.Enabled)
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
        {
            return;
        }

        var actions = await _ruleEngine.EvaluateAsync(new[] { logEvent }, rules, cancellationToken);
        if (actions.Count == 0)
        {
            return;
        }

        foreach (var action in actions)
        {
            var result = await ExecuteActionAsync(action, cancellationToken);
            db.ActionEvents.Add(new ActionEvent
            {
                Id = Guid.NewGuid(),
                Description = BuildDescription(action),
                Confidence = action.Confidence,
                Timestamp = result.Timestamp,
                Status = result.Status
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<RemediationActionResult> ExecuteActionAsync(
        RemediationAction action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _remediationExecutor.ExecuteAsync(action, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Remediation execution failed for action {ActionId}.", action.Id);
            return new RemediationActionResult
            {
                ActionId = action.Id,
                Status = ActionStatus.Failed,
                Timestamp = DateTimeOffset.UtcNow,
                Details = ex.Message
            };
        }
    }

    private static string BuildDescription(RemediationAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.Reason))
        {
            return action.Reason;
        }

        return $"{action.ActionType}: {action.TargetResource}";
    }
}
