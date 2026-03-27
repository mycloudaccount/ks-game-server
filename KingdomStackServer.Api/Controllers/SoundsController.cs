using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KingdomStackServer.Api.Controllers;

[ApiController]
[Route("api/assets/sounds")]
public sealed class SoundsController : ControllerBase
{
    private readonly AzureBlobProxyService _azureBlobProxyService;
    private readonly AzureBlobProxyOptions _options;

    public SoundsController(
        AzureBlobProxyService azureBlobProxyService,
        IOptions<AzureBlobProxyOptions> options)
    {
        _azureBlobProxyService = azureBlobProxyService;
        _options = options.Value;
    }

    [HttpGet("list")]
    public async Task<IActionResult> ListSoundAssets(
        [FromQuery] string? prefix,
        CancellationToken cancellationToken)
    {
        var items = await _azureBlobProxyService.ListSoundAssetsAsync(prefix, cancellationToken);
        var normalizedPrefix = prefix?.Trim('/');

        var response = new SoundAssetListResponse(
            normalizedPrefix ?? string.Empty,
            items.Count,
            items.Select(item => new SoundAssetListResponseItem(
                Path.GetFileNameWithoutExtension(item.BlobPath),
                Path.GetFileName(item.BlobPath),
                item.BlobPath,
                $"{Request.Scheme}://{Request.Host}/api/assets/sounds/{item.BlobPath}",
                item.ContentLength,
                item.LastModified))
                .ToArray());

        return Ok(response);
    }

    [HttpGet("bundle")]
    public async Task<IActionResult> DownloadSoundsBundle(CancellationToken cancellationToken)
    {
        var asset = await _azureBlobProxyService.GetSoundAssetAsync(
            _options.SoundsBundleFileName,
            cancellationToken);

        if (asset is null)
        {
            return NotFound();
        }

        ApplyAssetHeaders(asset);

        return File(
            asset.Content,
            "application/zip",
            fileDownloadName: _options.SoundsBundleFileName,
            enableRangeProcessing: true);
    }

    [HttpGet("{**blobPath}")]
    public async Task<IActionResult> GetSoundAsset(string blobPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobPath))
        {
            return BadRequest("A blob path is required.");
        }

        var asset = await _azureBlobProxyService.GetSoundAssetAsync(blobPath, cancellationToken);
        if (asset is null)
        {
            return NotFound();
        }

        ApplyAssetHeaders(asset);

        return File(
            asset.Content,
            asset.ContentType,
            fileDownloadName: Path.GetFileName(blobPath),
            enableRangeProcessing: true);
    }

    private void ApplyAssetHeaders(AzureBlobContent asset)
    {
        if (asset.ContentLength.HasValue)
        {
            Response.ContentLength = asset.ContentLength.Value;
        }

        if (asset.LastModified.HasValue)
        {
            Response.Headers.LastModified = asset.LastModified.Value.ToString("R");
        }

        if (!string.IsNullOrWhiteSpace(asset.ETag))
        {
            Response.Headers.ETag = asset.ETag;
        }
    }
}
