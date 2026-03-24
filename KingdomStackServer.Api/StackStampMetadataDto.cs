namespace KingdomStackServer.Api;

public sealed record StackStampMetadataDto(
    string? CreatedInKsEditorVersion,
    Dictionary<string, string>? Custom);
