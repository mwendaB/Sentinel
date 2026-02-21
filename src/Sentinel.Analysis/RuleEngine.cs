using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sentinel.Core.Interfaces;
using Sentinel.Core.Models;
using Sentinel.Core.Services;

namespace Sentinel.Analysis;

/// <summary>
/// Evaluates rule definitions against log events and produces remediation actions.
/// </summary>
public sealed class RuleEngine : IRuleEngine
{
    private readonly ILogger<RuleEngine> _logger;

    private const RegexOptions DefaultRegexOptions =
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    public RuleEngine(ILogger<RuleEngine> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<RemediationAction>> EvaluateAsync(
        IReadOnlyList<LogEvent> events,
        IReadOnlyList<RuleDefinition> rules,
        CancellationToken cancellationToken)
    {
        if (events.Count == 0 || rules.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<RemediationAction>>(Array.Empty<RemediationAction>());
        }

        var actions = new List<RemediationAction>();

        foreach (var rule in rules)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (!rule.Enabled)
            {
                continue;
            }

            if (!TryCreateRegex(rule.Pattern, out var regex))
            {
                _logger.LogWarning("Rule {RuleId} has invalid regex pattern: {Pattern}", rule.Id, rule.Pattern);
                continue;
            }

            var match = FindBestMatch(events, rule, regex);
            if (match is null)
            {
                continue;
            }

            var definition = rule.Action ?? CreateDefaultAction(rule, match);
            if (!RemediationActionFactory.TryCreate(definition, rule.Name, out var action, out var error))
            {
                _logger.LogWarning("Rule {RuleId} action invalid: {Error}", rule.Id, error);
                continue;
            }

            actions.Add(action);
        }

        return Task.FromResult<IReadOnlyList<RemediationAction>>(actions);
    }

    private static bool TryCreateRegex(string pattern, out Regex regex)
    {
        regex = null!;

        try
        {
            regex = new Regex(pattern, DefaultRegexOptions);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static LogEvent? FindBestMatch(
        IReadOnlyList<LogEvent> events,
        RuleDefinition rule,
        Regex regex)
    {
        LogEvent? best = null;

        foreach (var evt in events)
        {
            if (evt.Level < rule.MinimumLevel)
            {
                continue;
            }

            if (rule.SourceTypes is { Count: > 0 } &&
                !rule.SourceTypes.Contains(evt.Source.Type))
            {
                continue;
            }

            if (!regex.IsMatch(evt.Message))
            {
                continue;
            }

            if (best is null || evt.Timestamp > best.Timestamp)
            {
                best = evt;
            }
        }

        return best;
    }

    private static RemediationActionDefinition CreateDefaultAction(RuleDefinition rule, LogEvent match)
    {
        return new RemediationActionDefinition
        {
            ActionType = ActionType.SendNotification,
            TargetResource = rule.Name,
            Confidence = 85,
            Reason = $"Rule '{rule.Name}' matched log event {match.Id}.",
            Channel = NotificationChannel.Email,
            Message = $"{match.Timestamp:O} [{match.Level}] {match.Source.Name}: {match.Message}"
        };
    }
}
