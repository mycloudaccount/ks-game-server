namespace KingdomStackServer.Api;

public sealed record CanonicalValidationIssueResponse(
    string Code,
    string Message,
    string? Path = null,
    string Severity = "error",
    IReadOnlyList<string>? RelatedIds = null);

