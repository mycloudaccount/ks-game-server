namespace KingdomStackServer.Api;

public sealed record UserGameListResponse(
    string Prefix,
    int Count,
    IReadOnlyList<UserGameMetadataResponse> Items);
