namespace Sentinel.Worker;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public required string BaseUrl { get; init; }
    public required string ApiKey { get; init; }
}
