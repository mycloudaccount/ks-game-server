using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

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
/// Turns any <c>File(...)</c> result whose stream is a <see cref="BlobRedirectStream"/> into a 302
/// to the signed storage URL, so controllers don't need to know about redirects.
/// </summary>
public sealed class BlobRedirectResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is FileStreamResult { FileStream: BlobRedirectStream redirect })
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            context.Result = new RedirectResult(redirect.Url, permanent: false);
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
