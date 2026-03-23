namespace KingdomStackServer.Api;

public sealed record BlobUploadResult(
    string BlobPath,
    string BlobUrl,
    string ETag,
    long ContentLength,
    DateTimeOffset LastModified);
