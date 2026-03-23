namespace KingdomStackServer.Api;

public sealed record TileAssetListItem(string BlobPath, long? ContentLength, DateTimeOffset? LastModified);
