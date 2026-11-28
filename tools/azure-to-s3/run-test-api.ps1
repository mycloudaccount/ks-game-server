# Runs the API locally on port 5071 against the dev bucket with the Development-only fixed user, so
# asset endpoints can be tested without signing in. Mirrors the Lambda storage settings.
# Use -Port 5070 to sit behind the editor's Vite proxy (with VITE_KS_AUTH_MODE=dev-identity).
param([int]$Port = 5071)

$api = Resolve-Path (Join-Path $PSScriptRoot "..\..\KingdomStackServer.Api")
$bucket = "s3://kingdom-stack-dev-897154406373"

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:AWS__Profile = "careflow"
$env:AWS__Region = "us-east-1"
$env:Identity__Mode = "Configured"
$env:AzureBlobProxy__StorageBaseUrl = "$bucket/game-assets"
$env:AzureBlobProxy__UserGamesStorageBaseUrl = "$bucket/user-games"
$env:AzureBlobProxy__UserPreferencesStorageBaseUrl = "$bucket/user-preferences"
$env:AzureBlobProxy__ClientMessagesStorageBaseUrl = "$bucket/editor-messages"
$env:AzureBlobProxy__StackStampsStorageBaseUrl = "$bucket/game-assets"
$env:AzureBlobProxy__ParticleEffectsStorageBaseUrl = "$bucket/game-assets"
$env:AzureBlobProxy__RedirectToStorageAboveBytes = "4000000"

Set-Location $api
dotnet run --no-build --no-launch-profile
