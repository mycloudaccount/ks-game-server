using System.Text.Json;

namespace KingdomStackServer.Api;

public sealed record StackStampEntryDto(
    string EntryId,
    string EntityType,
    string? EntitySubtype,
    int Dx,
    int Dy,
    JsonElement Payload);
