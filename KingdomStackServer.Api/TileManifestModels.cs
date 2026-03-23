using System.Text.Json;
using System.Text.Json.Serialization;

namespace KingdomStackServer.Api;

public sealed class TileManifest
{
    public int Version { get; init; }

    public string GeneratedBy { get; init; } = string.Empty;

    public IReadOnlyList<TileDefinition> Tiles { get; init; } = [];
}

public sealed class TileDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Kind { get; init; } = "tile";

    public Dictionary<string, string> Images { get; init; } = [];

    public IReadOnlyList<string>? Variants { get; init; }

    public string? UiColor { get; init; }

    public int? PhaserColor { get; init; }

    public Dictionary<string, JsonElement>? Properties { get; init; }

    public Dictionary<string, JsonElement>? Metadata { get; init; }
}

public sealed class TileCatalogResponse
{
    public int Version { get; init; }

    public string GeneratedBy { get; init; } = string.Empty;

    public IReadOnlyList<TileCatalogItem> Tiles { get; init; } = [];
}

public sealed class TileCatalogItem
{
    public string Id { get; init; } = string.Empty;

    public string Kind { get; init; } = "tile";

    public Dictionary<string, string> Images { get; init; } = [];

    public Dictionary<string, string> ImageUrls { get; init; } = [];

    public IReadOnlyList<string>? Variants { get; init; }

    public string? UiColor { get; init; }

    public int? PhaserColor { get; init; }

    public Dictionary<string, JsonElement>? Properties { get; init; }

    public Dictionary<string, JsonElement>? Metadata { get; init; }
}
