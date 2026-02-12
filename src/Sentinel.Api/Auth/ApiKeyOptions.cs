namespace Sentinel.Api.Auth;

public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKeyAuth";

    public required string Key { get; init; }
}
