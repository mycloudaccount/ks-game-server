namespace KingdomStackServer.Api;

public sealed record UserPreferencesSaveResult(
    string BlobPath,
    string Url,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    long? ContentLength);
