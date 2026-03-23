using System.Text.Json;

namespace KingdomStackServer.Api;

public sealed class SaveUserPreferencesRequest
{
    public JsonElement? Preferences { get; init; }
}
