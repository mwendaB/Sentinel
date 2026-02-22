using System.Net.Http;

namespace Sentinel.Web.Services;

public sealed class ApiClientStatusHandler : DelegatingHandler
{
    private readonly ApiClientStatus _status;

    public ApiClientStatusHandler(ApiClientStatus status)
    {
        _status = status;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _status.MarkSuccess();
            }
            else
            {
                _status.MarkFailure($"{(int)response.StatusCode} {response.ReasonPhrase}");
            }

            return response;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _status.MarkFailure(ex.Message);
            throw;
        }
    }
}
