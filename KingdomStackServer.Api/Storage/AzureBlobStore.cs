using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace KingdomStackServer.Api.Storage;

public sealed class AzureBlobStore(BlobContainerClient containerClient) : IBlobStore
{
    public string Location => containerClient.Uri.ToString();

    public string GetUri(string blobName) => containerClient.GetBlobClient(blobName).Uri.ToString();

    public async Task<StoredBlob?> DownloadAsync(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await containerClient.GetBlobClient(blobName)
                .DownloadContentAsync(cancellationToken);
            var details = response.Value.Details;

            return new StoredBlob(
                response.Value.Content,
                new StoredBlobProperties(
                    details.ETag.ToString(),
                    details.ContentLength,
                    details.ContentType,
                    details.CreatedOn,
                    details.LastModified,
                    ToMetadata(details.Metadata)));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<StoredBlobStream?> OpenReadAsync(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await containerClient.GetBlobClient(blobName)
                .DownloadStreamingAsync(cancellationToken: cancellationToken);
            var details = response.Value.Details;

            return new StoredBlobStream(
                response.Value.Content,
                new StoredBlobProperties(
                    details.ETag.ToString(),
                    details.ContentLength,
                    details.ContentType,
                    details.CreatedOn,
                    details.LastModified,
                    ToMetadata(details.Metadata)));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    // Azure keeps streaming every file through the API.
    public Task<string?> GetReadUrlAsync(string blobName, TimeSpan lifetime, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);

    public async Task<StoredBlobProperties?> GetPropertiesAsync(string blobName, CancellationToken cancellationToken)
    {
        try
        {
            var response = await containerClient.GetBlobClient(blobName)
                .GetPropertiesAsync(cancellationToken: cancellationToken);
            return ToProperties(response.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<StoredBlobProperties> UploadAsync(
        string blobName,
        Stream content,
        BlobWriteOptions options,
        CancellationToken cancellationToken)
    {
        var blobClient = containerClient.GetBlobClient(blobName);
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = options.ContentType },
            Metadata = options.Metadata?.ToDictionary(pair => pair.Key, pair => pair.Value),
            Conditions = options.Condition switch
            {
                BlobWriteCondition.IfNotExists => new BlobRequestConditions { IfNoneMatch = ETag.All },
                BlobWriteCondition.IfMatch => new BlobRequestConditions { IfMatch = new ETag(options.IfMatchETag!) },
                _ => null
            }
        };

        try
        {
            await blobClient.UploadAsync(content, uploadOptions, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            throw new BlobConditionFailedException(ex.Status, ex.Message, ex);
        }

        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
        return ToProperties(properties.Value);
    }

    public async Task<bool> DeleteIfExistsAsync(string blobName, CancellationToken cancellationToken)
    {
        var response = await containerClient.GetBlobClient(blobName)
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);
        return response.Value;
    }

    public async Task<StoredBlobPage> ListPageAsync(
        string prefix,
        bool includeMetadata,
        int? pageSize,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        var pageable = containerClient.GetBlobsAsync(
            traits: includeMetadata ? BlobTraits.Metadata : BlobTraits.None,
            states: BlobStates.None,
            prefix: prefix,
            cancellationToken: cancellationToken);

        await foreach (var page in pageable.AsPages(continuationToken, pageSizeHint: pageSize))
        {
            var items = page.Values
                .Select(blobItem => new StoredBlobItem(
                    blobItem.Name,
                    blobItem.Properties.ContentLength,
                    blobItem.Properties.CreatedOn,
                    blobItem.Properties.LastModified,
                    blobItem.Properties.ETag?.ToString(),
                    ToMetadata(blobItem.Metadata)))
                .ToArray();

            return new StoredBlobPage(
                items,
                string.IsNullOrWhiteSpace(page.ContinuationToken) ? null : page.ContinuationToken);
        }

        return new StoredBlobPage([], null);
    }

    private static StoredBlobProperties ToProperties(BlobProperties properties)
        => new(
            properties.ETag.ToString(),
            properties.ContentLength,
            properties.ContentType,
            properties.CreatedOn,
            properties.LastModified,
            ToMetadata(properties.Metadata));

    private static IReadOnlyDictionary<string, string> ToMetadata(IDictionary<string, string>? metadata)
        => metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
}
