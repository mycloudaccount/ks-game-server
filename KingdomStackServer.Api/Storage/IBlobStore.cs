namespace KingdomStackServer.Api.Storage;

/// <summary>
/// A provider-neutral view of one storage container (an Azure blob container or an S3 bucket/prefix).
/// Blob names are relative to the container root.
/// </summary>
public interface IBlobStore
{
    /// <summary>Human-readable location used in logs and responses, e.g. s3://bucket/prefix.</summary>
    string Location { get; }

    string GetUri(string blobName);

    /// <summary>Downloads the full blob into memory. Returns null when the blob does not exist.</summary>
    Task<StoredBlob?> DownloadAsync(string blobName, CancellationToken cancellationToken);

    /// <summary>Opens a streaming read. Returns null when the blob does not exist. The caller disposes the stream.</summary>
    Task<StoredBlobStream?> OpenReadAsync(string blobName, CancellationToken cancellationToken);

    Task<StoredBlobProperties?> GetPropertiesAsync(string blobName, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the blob, replacing any existing content unless a condition says otherwise.
    /// Throws <see cref="BlobConditionFailedException"/> when the write condition is not met.
    /// </summary>
    Task<StoredBlobProperties> UploadAsync(
        string blobName,
        Stream content,
        BlobWriteOptions options,
        CancellationToken cancellationToken);

    Task<bool> DeleteIfExistsAsync(string blobName, CancellationToken cancellationToken);

    Task<StoredBlobPage> ListPageAsync(
        string prefix,
        bool includeMetadata,
        int? pageSize,
        string? continuationToken,
        CancellationToken cancellationToken);
}

public enum BlobWriteCondition
{
    None,
    /// <summary>Only write when the blob does not exist yet.</summary>
    IfNotExists,
    /// <summary>Only write when the blob's current ETag equals <see cref="BlobWriteOptions.IfMatchETag"/>.</summary>
    IfMatch
}

public sealed record BlobWriteOptions(
    string ContentType,
    IReadOnlyDictionary<string, string>? Metadata = null,
    BlobWriteCondition Condition = BlobWriteCondition.None,
    string? IfMatchETag = null);

public sealed record StoredBlobProperties(
    string ETag,
    long ContentLength,
    string? ContentType,
    DateTimeOffset CreatedOn,
    DateTimeOffset LastModified,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record StoredBlob(BinaryData Content, StoredBlobProperties Properties);

public sealed record StoredBlobStream(Stream Content, StoredBlobProperties Properties);

public sealed record StoredBlobItem(
    string Name,
    long? ContentLength,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? LastModified,
    string? ETag,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record StoredBlobPage(IReadOnlyList<StoredBlobItem> Items, string? ContinuationToken);

/// <summary>Raised when a conditional write fails. Status is 409 (already exists) or 412 (etag mismatch).</summary>
public sealed class BlobConditionFailedException(int status, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int Status { get; } = status;
}

public static class BlobStoreExtensions
{
    public static async IAsyncEnumerable<StoredBlobItem> ListAllAsync(
        this IBlobStore store,
        string prefix,
        bool includeMetadata,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? continuationToken = null;
        do
        {
            var page = await store.ListPageAsync(prefix, includeMetadata, null, continuationToken, cancellationToken);
            foreach (var item in page.Items)
            {
                yield return item;
            }

            continuationToken = page.ContinuationToken;
        }
        while (!string.IsNullOrWhiteSpace(continuationToken));
    }

    public static Task<StoredBlobProperties> UploadAsync(
        this IBlobStore store,
        string blobName,
        string text,
        BlobWriteOptions options,
        CancellationToken cancellationToken)
        => store.UploadAsync(blobName, System.Text.Encoding.UTF8.GetBytes(text), options, cancellationToken);

    public static async Task<StoredBlobProperties> UploadAsync(
        this IBlobStore store,
        string blobName,
        byte[] bytes,
        BlobWriteOptions options,
        CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(bytes, writable: false);
        return await store.UploadAsync(blobName, stream, options, cancellationToken);
    }
}
