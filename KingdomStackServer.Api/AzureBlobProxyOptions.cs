namespace KingdomStackServer.Api;

public class AzureBlobProxyOptions
{
    public const string SectionName = "AzureBlobProxy";

    public string StorageBaseUrl { get; set; } = string.Empty;
    
    public string UserGamesStorageBaseUrl { get; set; } = string.Empty;

    public string UserPreferencesStorageBaseUrl { get; set; } = string.Empty;

    public string StackStampsStorageBaseUrl { get; set; } = string.Empty;

    public string TilesPrefix { get; set; } = "tiles";

    public string TilesBundleFileName { get; set; } = "tiles_bundle.zip";

    public string CharactersPrefix { get; set; } = "characters";

    public string CharactersBundleFileName { get; set; } = "eddie.zip";

    public string UserGamesPrefix { get; set; } = "games";

    public string UserPreferencesPrefix { get; set; } = "preferences";

    public string StackStampsPrefix { get; set; } = "stacks";
}
