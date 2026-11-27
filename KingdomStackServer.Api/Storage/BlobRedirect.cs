using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace KingdomStackServer.Api.Storage;

/// <summary>
/// Placeholder content for a file that should be downloaded straight from storage instead of
/// through the API (AWS Lambda responses are capped at about 6 MB).
/// </summary>
public sealed class BlobRedirectStream(string url) : MemoryStream
{
    public string Url { get; } = url;
}

/// <summary>
/// Post-processes every <c>File(...)</c> result so controllers don't need to:
/// <list type="bullet">
/// <item>A <see cref="BlobRedirectStream"/> becomes a 302 to the signed storage URL.</item>
/// <item>Other files get <c>Cache-Control: private, no-cache</c> and their ETag / Last-Modified are
/// handed to ASP.NET, which then answers If-None-Match / If-Modified-Since with 304. Browsers
/// recheck on every load, so a changed asset shows up immediately, but unchanged files aren't
/// downloaded again. Without this, browsers guessed a lifetime of weeks from Last-Modified.</item>
/// </list>
/// </summary>
public sealed class AssetFileResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is not FileStreamResult file)
        {
            return;
        }

        var response = context.HttpContext.Response;

        if (file.FileStream is BlobRedirectStream redirect)
        {
            response.Headers.CacheControl = "no-store";
            context.Result = new RedirectResult(redirect.Url, permanent: false);
            return;
        }

        // Controllers set these headers by hand (ApplyAssetHeaders). Move them onto the result so
        // the file executor can evaluate conditional requests and set them itself.
        if (file.EntityTag is null && TryParseEntityTag(response.Headers.ETag.ToString(), out var entityTag))
        {
            file.EntityTag = entityTag;
        }

        if (file.LastModified is null
            && DateTimeOffset.TryParse(response.Headers.LastModified.ToString(), out var lastModified))
        {
            file.LastModified = lastModified;
        }

        response.Headers.Remove(HeaderNames.ETag);
        response.Headers.Remove(HeaderNames.LastModified);
        // A preset length would conflict with an empty 304 body; the executor sets it for 200s.
        response.ContentLength = null;
        response.Headers.CacheControl = "private, no-cache";
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    private static bool TryParseEntityTag(string value, out EntityTagHeaderValue entityTag)
    {
        entityTag = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var quoted = value.StartsWith('"') || value.StartsWith("W/", StringComparison.Ordinal)
            ? value
            : $"\"{value}\"";
        if (!EntityTagHeaderValue.TryParse(quoted, out var parsed) || parsed is null)
        {
            return false;
        }

        entityTag = parsed;
        return true;
    }
}
