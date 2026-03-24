namespace KingdomStackServer.Api;

public sealed record CreateStackStampRequest(
    string? Name,
    string? Description,
    IReadOnlyList<string>? Tags,
    StackStampDefinitionDto? Definition,
    string? PreviewImageBase64);
