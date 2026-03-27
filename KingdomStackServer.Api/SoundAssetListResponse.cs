namespace KingdomStackServer.Api;

public sealed record SoundAssetListResponse(
    string Prefix,
    int Count,
    IReadOnlyList<SoundAssetListResponseItem> Files);

public sealed record SoundAssetListResponseItem(
    string Id,
    string FileName,
    string BlobPath,
    string Url,
    long? ContentLength,
    DateTimeOffset? LastModified);
