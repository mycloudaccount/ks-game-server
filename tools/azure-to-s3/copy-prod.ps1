# Copies all four production containers from Azure (ksstorage, read only) into the production bucket,
# then copies the legacy user's files to the Cognito user without overwriting anything already there.
# Run once as a rehearsal and again right before switching DNS.
param(
    [string]$Bucket = "kingdom-stack-prod-897154406373",
    [string]$LegacyScopeKey = "6095aa29-3d60-4b76-8a79-c5791768b6ae",
    [string]$CognitoScopeKey = "64d84418-90f1-7016-996e-227993cc0318"
)

$ErrorActionPreference = "Stop"
$env:AWS_PROFILE = "careflow"
Set-Location $PSScriptRoot

dotnet build -nologo -v q | Out-Null
foreach ($container in "game-assets", "user-games", "user-preferences", "editor-messages") {
    dotnet run --no-build -- "https://ksstorage.blob.core.windows.net/$container" "s3://$Bucket/$container" | Select-Object -Last 3
}
& .\copy-user-scope.ps1 -Bucket $Bucket -FromScopeKey $LegacyScopeKey -ToScopeKey $CognitoScopeKey
"ALL DONE"
