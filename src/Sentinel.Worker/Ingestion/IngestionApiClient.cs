using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Sentinel.Core.Models;

namespace Sentinel.Worker.Ingestion;

public sealed class IngestionApiClient
{
    private readonly HttpClient _httpClient;

    public IngestionApiClient(HttpClient httpClient, IOptions<ApiOptions> apiOptions)
    {
        _httpClient = httpClient;
        var options = apiOptions.Value;
        _httpClient.BaseAddress = new Uri(options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Remove("X-Api-Key");
        _httpClient.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
    }

    public async Task SendLogAsync(LogEvent logEvent, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/logs", logEvent, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SendActionAsync(ActionEvent actionEvent, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/actions", actionEvent, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
