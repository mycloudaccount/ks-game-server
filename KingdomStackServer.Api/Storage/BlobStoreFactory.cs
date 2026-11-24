using Amazon.S3;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;

namespace KingdomStackServer.Api.Storage;

/// <summary>
/// Creates a blob store from a configured base URL:
/// <c>https://{account}.blob.core.windows.net/{container}</c> uses Azure Blob Storage,
/// <c>s3://{bucket}/{optional/prefix}</c> uses Amazon S3.
/// </summary>
public sealed class BlobStoreFactory(
    IServiceProvider serviceProvider,
    ILogger<BlobStoreFactory> logger)
{
    private readonly Lazy<TokenCredential> _azureCredential = new(() =>
        new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeInteractiveBrowserCredential = true
        }));

    public TokenCredential AzureCredential => _azureCredential.Value;

    public IBlobStore Create(string storageBaseUrl, string optionName)
    {
        if (string.IsNullOrWhiteSpace(storageBaseUrl))
        {
            throw new InvalidOperationException(
                $"{AzureBlobProxyOptions.SectionName}:{optionName} must be configured.");
        }

        var uri = new Uri(storageBaseUrl.Trim().TrimEnd('/'), UriKind.Absolute);

        if (string.Equals(uri.Scheme, "s3", StringComparison.OrdinalIgnoreCase))
        {
            var bucketName = uri.Host;
            var keyPrefix = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
            logger.LogInformation(
                "Using Amazon S3 for {OptionName}: bucket {Bucket}, prefix {Prefix}",
                optionName,
                bucketName,
                keyPrefix);
            return new S3BlobStore(serviceProvider.GetRequiredService<IAmazonS3>(), bucketName, keyPrefix);
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Using Azure Blob Storage with DefaultAzureCredential for {OptionName}: {ContainerUri}",
                optionName,
                uri);
            return new AzureBlobStore(new BlobContainerClient(uri, AzureCredential));
        }

        throw new InvalidOperationException(
            $"{AzureBlobProxyOptions.SectionName}:{optionName} must start with https:// (Azure) or s3:// (AWS).");
    }
}
