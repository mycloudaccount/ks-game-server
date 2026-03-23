namespace KingdomStackServer.Api;

public sealed record GameListResponse(
    string Prefix,
    int Count,
    IReadOnlyList<GameListItemResponse> Games);
