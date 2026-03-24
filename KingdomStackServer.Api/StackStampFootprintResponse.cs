namespace KingdomStackServer.Api;

public sealed record StackStampFootprintResponse(
    int MinDx,
    int MinDy,
    int MaxDx,
    int MaxDy,
    int Width,
    int Height);
