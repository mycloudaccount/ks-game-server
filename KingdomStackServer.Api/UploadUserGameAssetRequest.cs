using Microsoft.AspNetCore.Mvc;

namespace KingdomStackServer.Api;

public sealed class UploadUserGameAssetRequest
{
    [FromForm(Name = "file")]
    public IFormFile? File { get; init; }

    [FromForm(Name = "blobPath")]
    public string? BlobPath { get; init; }
}
