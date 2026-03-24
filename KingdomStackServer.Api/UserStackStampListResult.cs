namespace KingdomStackServer.Api;

public sealed record UserStackStampListResult(
    IReadOnlyList<UserStackStampListItem> Items,
    string? ContinuationToken);
