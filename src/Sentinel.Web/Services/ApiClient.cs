using System.Net.Http.Json;
using Sentinel.Core.Models;

namespace Sentinel.Web.Services;

public sealed class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<MetricsSnapshot>("/api/metrics", cancellationToken)
            ?? new MetricsSnapshot
            {
                EventsPerSecond = 0,
                ActiveRules = 0,
                ActionsToday = 0,
                ActiveSources = 0,
                RulesEvaluated = 0,
                ActionSuccessRate = 0
            };
    }

    public async Task<IReadOnlyList<LogEvent>> GetLogsAsync(int count, CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<IReadOnlyList<LogEvent>>($"/api/logs?count={count}", cancellationToken)
            ?? Array.Empty<LogEvent>();
    }

    public async Task<IReadOnlyList<ActionEvent>> GetActionsAsync(int count, CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<IReadOnlyList<ActionEvent>>($"/api/actions?count={count}", cancellationToken)
            ?? Array.Empty<ActionEvent>();
    }

    public async Task<IReadOnlyList<RuleDefinition>> GetRulesAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<IReadOnlyList<RuleDefinition>>("/api/rules", cancellationToken)
            ?? Array.Empty<RuleDefinition>();
    }

    public async Task<RuleDefinition?> AddRuleAsync(NewRuleRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/rules", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<RuleDefinition>(cancellationToken: cancellationToken);
    }

    public async Task<RuleDefinition?> ToggleRuleAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/rules/{ruleId}/toggle", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<RuleDefinition>(cancellationToken: cancellationToken);
    }

    public async Task<RuleTestResult?> TestRuleAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/rules/{ruleId}/test", new { }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<RuleTestResult>(cancellationToken: cancellationToken);
    }

    public async Task<RuleExecutionResponse?> ExecuteRuleAsync(Guid ruleId, bool dryRun, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync($"/api/rules/{ruleId}/execute?dryRun={dryRun.ToString().ToLowerInvariant()}", null, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<RuleExecutionResponse>(cancellationToken: cancellationToken);
    }
}
