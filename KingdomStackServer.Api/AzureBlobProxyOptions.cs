namespace KingdomStackServer.Api;

public class AzureBlobProxyOptions
{
    public const string SectionName = "AzureBlobProxy";

    public string StorageBaseUrl { get; set; } = string.Empty;
    
    public string UserGamesStorageBaseUrl { get; set; } = string.Empty;

    public string UserPreferencesStorageBaseUrl { get; set; } = string.Empty;

    public string ClientMessagesStorageBaseUrl { get; set; } = string.Empty;

    public string StackStampsStorageBaseUrl { get; set; } = string.Empty;

    public string TilesPrefix { get; set; } = "tiles";

    public string TilesBundleFileName { get; set; } = "tiles_bundle.zip";

    public string CharactersPrefix { get; set; } = "characters";

    public string CharactersBundleFileName { get; set; } = "eddie.zip";

    public string SoundsPrefix { get; set; } = "sounds";

    public string SoundsBundleFileName { get; set; } = "sounds_bundle.zip";

    public string UserGamesPrefix { get; set; } = "games";

    public string UserPreferencesPrefix { get; set; } = "preferences";

    public string ClientMessagesBlobName { get; set; } = "messages.json";

    public string StackStampsPrefix { get; set; } = "stacks";

    public string ParticleEffectsStorageBaseUrl { get; set; } = string.Empty;

    public string ParticleEffectsPrefix { get; set; } = "particle-effects";
}
