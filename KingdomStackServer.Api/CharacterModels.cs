namespace KingdomStackServer.Api;

public sealed record CharacterAttachmentOffsetDto(
    double X,
    double Y,
    double Z);

public sealed record CharacterRecipeDto(
    string? Gender,
    string? SkinToneId,
    string? TextureId,
    string? HairTextureId,
    string? BeardTextureId,
    string? AnimationId,
    IReadOnlyList<string> EnabledAttachmentCategories,
    Dictionary<string, string?> SelectedModelIds);

public sealed record EditorCharacterDefinitionDto(
    string? SelectedPackId,
    CharacterRecipeDto Recipe,
    Dictionary<string, CharacterAttachmentOffsetDto> AttachmentPositionOffsets,
    Dictionary<string, CharacterAttachmentOffsetDto> AttachmentRotationOffsets);

public sealed record CreateEditorCharacterRequest(
    string? Name,
    EditorCharacterDefinitionDto? Definition,
    string? ThumbnailImageBase64);

public sealed record UpdateEditorCharacterRequest(
    string? Name,
    string? ETag,
    EditorCharacterDefinitionDto? Definition,
    string? ThumbnailImageBase64,
    bool? ClearThumbnail);

public sealed record EditorCharacterListResponse(
    string Prefix,
    int Count,
    string? ContinuationToken,
    EditorCharacterListItemResponse[] Items);

public sealed record EditorCharacterListItemResponse(
    string Id,
    string Name,
    string BlobPath,
    string LoadUrl,
    string? ThumbnailUrl,
    int SchemaVersion,
    int Version,
    string? SelectedPackId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag);

public sealed record EditorCharacterResponse(
    string Id,
    string Name,
    string BlobPath,
    string LoadUrl,
    string? ThumbnailUrl,
    int SchemaVersion,
    int Version,
    string? SelectedPackId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag,
    EditorCharacterDefinitionDto Definition);

public sealed record StoredEditorCharacterDocument(
    string Id,
    string Name,
    int SchemaVersion,
    int Version,
    string? SelectedPackId,
    bool HasThumbnail,
    EditorCharacterDefinitionDto Definition);

public sealed record UserEditorCharacterListResult(
    IReadOnlyList<UserEditorCharacterListItem> Items,
    string? ContinuationToken);

public sealed record UserEditorCharacterListItem(
    string Id,
    string Name,
    string BlobPath,
    bool HasThumbnail,
    int SchemaVersion,
    int Version,
    string? SelectedPackId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag);

public sealed record UserEditorCharacterContent(
    string Id,
    string Name,
    string BlobPath,
    bool HasThumbnail,
    int SchemaVersion,
    int Version,
    string? SelectedPackId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag,
    EditorCharacterDefinitionDto Definition);

public sealed record UserEditorCharacterSaveResult(
    string Id,
    string Name,
    string BlobPath,
    bool HasThumbnail,
    int SchemaVersion,
    int Version,
    string? SelectedPackId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastModified,
    string? ETag,
    EditorCharacterDefinitionDto Definition);
