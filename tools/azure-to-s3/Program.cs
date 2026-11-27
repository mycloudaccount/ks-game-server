// Copies every blob in an Azure container to an S3 bucket/prefix, keeping content type,
// metadata, and the original creation time (stored as "createdon" metadata, which
// KingdomStackServer's S3BlobStore reads back as CreatedOn).
//
// Usage: ks-azure-to-s3 <azure-container-url> <s3://bucket/prefix> [--dry-run]
// Credentials: Azure from `az login` (DefaultAzureCredential); AWS from AWS_PROFILE / default chain.

using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: ks-azure-to-s3 <azure-container-url> <s3://bucket/prefix> [--dry-run]");
    return 2;
}

var source = new BlobContainerClient(
    new Uri(args[0].TrimEnd('/')),
    new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeInteractiveBrowserCredential = true }));
var target = new Uri(args[1].TrimEnd('/'));
if (!string.Equals(target.Scheme, "s3", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Target must be s3://bucket/prefix");
    return 2;
}

var bucket = target.Host;
var keyPrefix = target.AbsolutePath.Trim('/');
keyPrefix = keyPrefix.Length == 0 ? string.Empty : keyPrefix + "/";
var dryRun = args.Contains("--dry-run");
var region = RegionEndpoint.GetBySystemName(Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1");
using var s3 = new AmazonS3Client(region);

var blobs = new List<BlobItem>();
await foreach (var blob in source.GetBlobsAsync(traits: BlobTraits.Metadata))
{
    blobs.Add(blob);
}

Console.WriteLine($"{source.Uri} -> s3://{bucket}/{keyPrefix}  ({blobs.Count} blobs){(dryRun ? "  [dry run]" : "")}");

var copied = 0;
long bytes = 0;
var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

await Parallel.ForEachAsync(blobs, new ParallelOptions { MaxDegreeOfParallelism = 12 }, async (blob, ct) =>
{
    var key = keyPrefix + blob.Name;
    try
    {
        if (dryRun)
        {
            Console.WriteLine($"  would copy {blob.Name} ({blob.Properties.ContentLength} bytes, {blob.Properties.ContentType})");
            return;
        }

        var download = await source.GetBlobClient(blob.Name).DownloadContentAsync(ct);
        await using var content = download.Value.Content.ToStream();

        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = blob.Properties.ContentType ?? "application/octet-stream"
        };
        foreach (var (name, value) in blob.Metadata)
        {
            request.Metadata.Add(name, value);
        }
        if (blob.Properties.CreatedOn is { } createdOn && !blob.Metadata.ContainsKey("createdon"))
        {
            request.Metadata.Add("createdon", createdOn.ToString("O"));
        }
        if (blob.Properties.LastModified is { } lastModified && !blob.Metadata.ContainsKey("lastmodified"))
        {
            request.Metadata.Add("lastmodified", lastModified.ToString("O"));
        }

        await s3.PutObjectAsync(request, ct);
        Interlocked.Increment(ref copied);
        Interlocked.Add(ref bytes, blob.Properties.ContentLength ?? 0);
    }
    catch (Exception ex)
    {
        failures.Add($"{blob.Name}: {ex.Message}");
    }
});

Console.WriteLine($"Copied {copied}/{blobs.Count} blobs, {bytes / 1024.0 / 1024.0:N1} MB. Failures: {failures.Count}");
foreach (var failure in failures)
{
    Console.WriteLine($"  FAILED {failure}");
}

return failures.IsEmpty ? 0 : 1;
