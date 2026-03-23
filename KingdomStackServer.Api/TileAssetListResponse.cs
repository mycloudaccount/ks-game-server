namespace KingdomStackServer.Api;

public sealed record TileAssetListResponse(
    string Prefix,
    int Count,
    IReadOnlyList<TileAssetListResponseItem> Files);

public sealed record TileAssetListResponseItem(
    string Id,
    string FileName,
    string BlobPath,
    string Url,
    long? ContentLength,
    DateTimeOffset? LastModified);
