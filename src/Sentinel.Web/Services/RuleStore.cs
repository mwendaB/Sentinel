using System.Text.RegularExpressions;
using Sentinel.Core.Models;
using Models = Sentinel.Core.Models;

namespace Sentinel.Web.Services;

public sealed class RuleStore
{
    private readonly List<RuleDefinition> _rules = new();
    private readonly LogStreamState _logStreamState;

    public RuleStore(LogStreamState logStreamState)
    {
        _logStreamState = logStreamState;
        Seed();
    }

    public IReadOnlyList<RuleDefinition> Rules => _rules;

    public RuleDefinition AddRule(string name, string pattern, Models.LogLevel minimumLevel, bool enabled)
    {
        var rule = new RuleDefinition
        {
            Id = Guid.NewGuid(),
            Name = name,
            Pattern = pattern,
            MinimumLevel = minimumLevel,
            Enabled = enabled
        };

        _rules.Add(rule);
        return rule;
    }

    public void ToggleRule(Guid id)
    {
        var index = _rules.FindIndex(rule => rule.Id == id);
        if (index < 0)
        {
            return;
        }

        var current = _rules[index];
        _rules[index] = current with { Enabled = !current.Enabled };
    }

    public IReadOnlyList<LogEvent> TestRule(RuleDefinition rule)
    {
        var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var candidates = _logStreamState.GetRecentEvents(100)
            .Where(evt => evt.Level >= rule.MinimumLevel)
            .ToList();

        return candidates
            .Where(evt => regex.IsMatch(evt.Message))
            .ToList();
    }

    private void Seed()
    {
        if (_rules.Count > 0)
        {
            return;
        }

        AddRule("High latency on postgres", "slow query detected", Models.LogLevel.Warning, true);
        AddRule("Nginx timeouts", "connection timeout", Models.LogLevel.Error, true);
        AddRule("Cache miss spike", "cache miss", Models.LogLevel.Warning, false);
    }
}
