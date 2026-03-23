namespace KingdomStackServer.Api;

public sealed class AzureBlobContent : IAsyncDisposable
{
    public required Stream Content { get; init; }

    public required string ContentType { get; init; }

    public long? ContentLength { get; init; }

    public string? ETag { get; init; }

    public DateTimeOffset? LastModified { get; init; }

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
    }
}
