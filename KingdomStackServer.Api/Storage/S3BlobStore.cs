using System.Net;
using Amazon.S3;
using Amazon.S3.Model;

namespace KingdomStackServer.Api.Storage;

/// <summary>
/// Stores blobs as S3 objects under <c>s3://{bucket}/{keyPrefix}</c>.
/// S3 has no creation timestamp, so the first write records one in the "createdon" metadata entry.
/// </summary>
public sealed class S3BlobStore : IBlobStore
{
    private const string MetadataHeaderPrefix = "x-amz-meta-";
    private const string CreatedOnMetadataKey = "createdon";

    // Set only by the Azure-to-S3 migration copy, because S3 resets LastModified on every write.
    // Normal uploads replace all metadata, so after the next real save S3's own timestamp applies again.
    private const string MigratedLastModifiedMetadataKey = "lastmodified";

    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;
    private readonly string _keyPrefix;

    public S3BlobStore(IAmazonS3 s3, string bucketName, string? keyPrefix)
    {
        _s3 = s3;
        _bucketName = bucketName;
        var trimmedPrefix = keyPrefix?.Trim('/');
        _keyPrefix = string.IsNullOrWhiteSpace(trimmedPrefix) ? string.Empty : $"{trimmedPrefix}/";
    }

    public string Location => $"s3://{_bucketName}/{_keyPrefix}".TrimEnd('/');

    public string GetUri(string blobName) => $"s3://{_bucketName}/{ToKey(blobName)}";

    public async Task<StoredBlob?> DownloadAsync(string blobName, CancellationToken cancellationToken)
    {
        var opened = await OpenReadAsync(blobName, cancellationToken);
        if (opened is null)
        {
            return null;
        }

        await using (opened.Content)
        {
            var content = await BinaryData.FromStreamAsync(opened.Content, cancellationToken);
            return new StoredBlob(content, opened.Properties);
        }
    }

    public async Task<StoredBlobStream?> OpenReadAsync(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _s3.GetObjectAsync(
                new GetObjectRequest { BucketName = _bucketName, Key = ToKey(blobName) },
                cancellationToken);

            var metadata = ToMetadata(response.Metadata);
            return new StoredBlobStream(
                response.ResponseStream,
                ToProperties(
                    response.ETag,
                    response.ContentLength,
                    response.Headers.ContentType,
                    response.LastModified,
                    metadata));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<StoredBlobProperties?> GetPropertiesAsync(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _s3.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = _bucketName, Key = ToKey(blobName) },
                cancellationToken);

            return ToProperties(
                response.ETag,
                response.ContentLength,
                response.Headers.ContentType,
                response.LastModified,
                ToMetadata(response.Metadata));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string?> GetReadUrlAsync(string blobName, TimeSpan lifetime, CancellationToken cancellationToken)
        => await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = ToKey(blobName),
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(lifetime)
        });

    public async Task<StoredBlobProperties> UploadAsync(
        string blobName,
        Stream content,
        BlobWriteOptions options,
        CancellationToken cancellationToken)
    {
        var key = ToKey(blobName);
        var existing = options.Condition == BlobWriteCondition.IfNotExists
            ? null
            : await GetPropertiesAsync(blobName, cancellationToken);
        var createdOn = existing?.CreatedOn ?? DateTimeOffset.UtcNow;

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            ContentType = options.ContentType
        };

        if (options.Metadata is not null)
        {
            foreach (var (metadataKey, value) in options.Metadata)
            {
                request.Metadata.Add(metadataKey, value);
            }
        }

        request.Metadata.Add(CreatedOnMetadataKey, createdOn.ToString("O"));

        switch (options.Condition)
        {
            case BlobWriteCondition.IfNotExists:
                request.IfNoneMatch = "*";
                break;
            case BlobWriteCondition.IfMatch:
                request.IfMatch = options.IfMatchETag;
                break;
        }

        try
        {
            await _s3.PutObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            // S3 returns 412 for both "already exists" (If-None-Match) and "etag mismatch" (If-Match).
            // Report "already exists" as 409 so callers can tell the two cases apart, like Azure does.
            var status = options.Condition == BlobWriteCondition.IfNotExists ? 409 : 412;
            throw new BlobConditionFailedException(status, ex.Message, ex);
        }

        return await GetPropertiesAsync(blobName, cancellationToken)
            ?? throw new InvalidOperationException($"Object '{key}' was not found right after it was written.");
    }

    public async Task<bool> DeleteIfExistsAsync(string blobName, CancellationToken cancellationToken)
    {
        if (await GetPropertiesAsync(blobName, cancellationToken) is null)
        {
            return false;
        }

        await _s3.DeleteObjectAsync(
            new DeleteObjectRequest { BucketName = _bucketName, Key = ToKey(blobName) },
            cancellationToken);
        return true;
    }

    public async Task<StoredBlobPage> ListPageAsync(
        string prefix,
        bool includeMetadata,
        int? pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var response = await _s3.ListObjectsV2Async(
            new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = ToKey(prefix),
                MaxKeys = pageSize ?? 1000,
                ContinuationToken = string.IsNullOrWhiteSpace(continuationToken) ? null : continuationToken
            },
            cancellationToken);

        var items = new List<StoredBlobItem>();
        foreach (var s3Object in response.S3Objects ?? [])
        {
            var name = s3Object.Key[_keyPrefix.Length..];
            if (string.IsNullOrEmpty(name) || name.EndsWith('/'))
            {
                continue;
            }

            if (!includeMetadata)
            {
                DateTimeOffset? lastModified = s3Object.LastModified is { } modified
                    ? new DateTimeOffset(modified.ToUniversalTime())
                    : null;
                items.Add(new StoredBlobItem(
                    name,
                    s3Object.Size,
                    lastModified,
                    lastModified,
                    s3Object.ETag,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
                continue;
            }

            // ListObjectsV2 does not return user metadata, so read it per object.
            var properties = await GetPropertiesAsync(name, cancellationToken);
            if (properties is null)
            {
                continue;
            }

            items.Add(new StoredBlobItem(
                name,
                properties.ContentLength,
                properties.CreatedOn,
                properties.LastModified,
                properties.ETag,
                properties.Metadata));
        }

        var nextToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        return new StoredBlobPage(items, nextToken);
    }

    private string ToKey(string blobName) => _keyPrefix + blobName.TrimStart('/');

    private static StoredBlobProperties ToProperties(
        string? eTag,
        long contentLength,
        string? contentType,
        DateTime? lastModified,
        IReadOnlyDictionary<string, string> metadata)
    {
        var modified = metadata.TryGetValue(MigratedLastModifiedMetadataKey, out var migratedRaw)
                       && DateTimeOffset.TryParse(migratedRaw, out var migrated)
            ? migrated
            : lastModified is { } value
                ? new DateTimeOffset(value.ToUniversalTime())
                : DateTimeOffset.UtcNow;
        var createdOn = metadata.TryGetValue(CreatedOnMetadataKey, out var createdRaw)
                        && DateTimeOffset.TryParse(createdRaw, out var parsed)
            ? parsed
            : modified;

        return new StoredBlobProperties(eTag ?? string.Empty, contentLength, contentType, createdOn, modified, metadata);
    }

    private static IReadOnlyDictionary<string, string> ToMetadata(MetadataCollection metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in metadata.Keys)
        {
            var name = key.StartsWith(MetadataHeaderPrefix, StringComparison.OrdinalIgnoreCase)
                ? key[MetadataHeaderPrefix.Length..]
                : key;
            result[name] = metadata[key];
        }

        return result;
    }
}
