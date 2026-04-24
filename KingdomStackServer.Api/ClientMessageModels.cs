namespace KingdomStackServer.Api;

public sealed record ClientMessagesResponse(
    int Version,
    string UpdatedAt,
    IReadOnlyList<ClientMessageResponse> Messages,
    string? ETag,
    DateTimeOffset? LastModified);

public sealed record ClientMessageResponse(
    string Id,
    string Audience,
    string Title,
    string Body,
    string Kind,
    string Severity,
    string StartsAt,
    string? ExpiresAt,
    bool Dismissible,
    ClientMessageActionResponse? Action,
    string CreatedAt,
    bool Read,
    bool Dismissed,
    DateTimeOffset? ReadAt,
    DateTimeOffset? DismissedAt);

public sealed record ClientMessageActionResponse(
    string Label,
    string? Url,
    string? Route);

public sealed record ClientMessageUserState(
    DateTimeOffset? ReadAt,
    DateTimeOffset? DismissedAt);
