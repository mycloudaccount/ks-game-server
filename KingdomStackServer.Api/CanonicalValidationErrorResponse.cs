namespace KingdomStackServer.Api;

public sealed record CanonicalValidationErrorResponse(
    string Error,
    string Message,
    IReadOnlyList<CanonicalValidationIssueResponse> Issues);

