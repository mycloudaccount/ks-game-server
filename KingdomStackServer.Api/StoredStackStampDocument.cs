namespace KingdomStackServer.Api;

public sealed record StoredStackStampDocument(
    string Id,
    string Name,
    string? Description,
    int SchemaVersion,
    int Version,
    string[] Tags,
    int EntryCount,
    string[] TileReferences,
    string[] CharacterReferences,
    StackStampFootprintResponse Footprint,
    bool HasPreview,
    StackStampDefinitionDto Definition);
