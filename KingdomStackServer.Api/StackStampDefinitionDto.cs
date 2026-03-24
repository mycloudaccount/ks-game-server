namespace KingdomStackServer.Api;

public sealed record StackStampDefinitionDto(
    int SchemaVersion,
    StackStampOriginDto Origin,
    StackStampEntryDto[] Entries,
    StackStampMetadataDto? Metadata);
