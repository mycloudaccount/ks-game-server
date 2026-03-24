namespace KingdomStackServer.Api;

public sealed record UpdateStackStampRequest(
    string? Name,
    string? Description,
    IReadOnlyList<string>? Tags,
    string? ETag,
    StackStampDefinitionDto? Definition,
    string? PreviewImageBase64,
    bool? ClearPreview);
