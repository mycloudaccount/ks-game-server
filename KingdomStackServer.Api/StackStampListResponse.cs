namespace KingdomStackServer.Api;

public sealed record StackStampListResponse(
    string Prefix,
    int Count,
    string? ContinuationToken,
    IReadOnlyList<StackStampListItemResponse> Items);
