namespace KingdomStackServer.Api;

public sealed record StorageUserContext(
    string ScopeKey,
    string? ObjectId,
    string? Email,
    string? DisplayName);
