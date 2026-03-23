# Kingdom Stack Server

## technical descisions

### Azure blob storage

The API uses Azure Blob Storage for static game assets, user game saves, and user preferences.

- `game-assets` is used for shared assets such as tiles and characters.
- `user-games` is used for user save data.
- `user-preferences` is used for user preference data.
- User save data is organized under `games/users/{scopeKey}/saves`.
- User preference data is organized under `preferences/users/{scopeKey}/preferences.json`.

## API

### Root

- `GET /` returns a basic API status payload.

### Tile assets

- `GET /api/assets/tiles/list` lists tile assets, optionally filtered by `prefix`.
- `GET /api/assets/tiles/catalog` returns the tile catalog with image URLs.
- `GET /api/assets/tiles/tiles.json` returns the raw tile manifest JSON.
- `GET /api/assets/tiles/bundle` downloads the tile bundle zip.
- `GET /api/assets/tiles/{**blobPath}` returns a single tile asset by blob path.

### Character assets

- `GET /api/assets/characters/list` lists character assets, optionally filtered by `prefix`.
- `GET /api/assets/characters/bundle` downloads the characters bundle zip.
- `GET /api/assets/characters/{**blobPath}` returns a single character asset by blob path.

### Games

- `POST /api/games` upserts a user game JSON document.
- `GET /api/games` lists saved games for the current user, optionally filtered by `prefix`.
- `GET /api/games/{gameId}` returns a saved game by `gameId`.
- `DELETE /api/games/{gameId}` deletes a saved game by `gameId`.

`POST /api/games` request body:

```json
{
  "gameId": "my-first-game",
  "name": "My First Game",
  "game": {
    "gameId": "my-first-game",
    "name": "My First Game"
  }
}
```

### Preferences

- `POST /api/preferences` upserts the current user's preferences JSON document.
- `GET /api/preferences` returns the current user's preferences JSON document.
- `DELETE /api/preferences` deletes the current user's preferences JSON document.

`POST /api/preferences` request body:

```json
{
  "preferences": {
    "theme": "dark",
    "musicVolume": 0.8
  }
}
```

### Legacy

- `POST /api/assets/games` uploads a game-related file using multipart form data. This endpoint is marked obsolete in code and `POST /api/games` is preferred.

### Development

- `GET /api/assets/azure-identity` returns Azure credential and token claim details in development only.

## secuty

### Azure roles

The `user-games` container requires the `Storage Blob Data Contributor` Azure role for the identity used by the API.

This role is needed because the API must be able to list, read, upload, update metadata, and delete blobs in `user-games`.

The `user-preferences` container should use the same `Storage Blob Data Contributor` role because the API reads, writes, and deletes the current user's preferences blob.
