namespace KingdomStackServer.Api;

public sealed record StackStampGroupDto(
    string StackId,
    string Name,
    string AnchorEntryId,
    string[] EntryIds,
    string[] ChildStackIds,
    string? ParentStackId);
