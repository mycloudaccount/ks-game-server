using System.Text.Json;

namespace KingdomStackServer.Api;

public sealed class SaveGameRequest
{
    public string? GameId { get; init; }

    public string? Name { get; init; }

    public JsonElement? Game { get; init; }
}
