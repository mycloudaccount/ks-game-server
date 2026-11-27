# Creates the Kingdom Stack Cognito user pool (default Essentials tier, free up to 10,000 MAU), hosted sign-in
# domain, and a confidential app client for the API's cookie-based OIDC sign-in.
# Self sign-up is disabled: users are created by an admin and get a temporary password by email.
#
# The client secret is written to the API's dotnet user-secrets and is never printed.
param(
    [string]$Profile = "careflow",
    [string]$Region = "us-east-1",
    [string]$PoolName = "kingdom-stack",
    [string]$DomainPrefix = "kingdomstack",
    [string[]]$CallbackUrls = @("http://localhost:4000/api/auth/callback"),
    [string[]]$LogoutUrls = @("http://localhost:4000/"),
    [string]$AdminEmail,
    [string]$ApiProjectDir = (Join-Path $PSScriptRoot "..\..\KingdomStackServer.Api")
)

$ErrorActionPreference = "Stop"
$common = @("--profile", $Profile, "--region", $Region)

$existing = aws cognito-idp list-user-pools --max-results 60 @common --query "UserPools[?Name=='$PoolName'].Id" --output text
if ($existing) {
    throw "A user pool named '$PoolName' already exists ($existing). Not creating another."
}

$poolId = aws cognito-idp create-user-pool @common `
    --pool-name $PoolName `
    --username-attributes email `
    --auto-verified-attributes email `
    --admin-create-user-config AllowAdminCreateUserOnly=true `
    --account-recovery-setting "RecoveryMechanisms=[{Priority=1,Name=verified_email}]" `
    --user-pool-tags app=kingdom-stack `
    --query "UserPool.Id" --output text
Write-Host "User pool: $poolId"

aws cognito-idp create-user-pool-domain @common --user-pool-id $poolId --domain $DomainPrefix | Out-Null
Write-Host "Hosted sign-in: https://$DomainPrefix.auth.$Region.amazoncognito.com"

$client = aws cognito-idp create-user-pool-client @common `
    --user-pool-id $poolId `
    --client-name "kingdom-stack-api" `
    --generate-secret `
    --allowed-o-auth-flows code `
    --allowed-o-auth-flows-user-pool-client `
    --allowed-o-auth-scopes openid email profile `
    --supported-identity-providers COGNITO `
    --callback-urls $CallbackUrls `
    --logout-urls $LogoutUrls `
    --prevent-user-existence-errors ENABLED `
    --explicit-auth-flows ALLOW_REFRESH_TOKEN_AUTH `
    --output json | ConvertFrom-Json
$clientId = $client.UserPoolClient.ClientId
Write-Host "App client: $clientId"

Push-Location $ApiProjectDir
try {
    dotnet user-secrets set "Cognito:Authority" "https://cognito-idp.$Region.amazonaws.com/$poolId" | Out-Null
    dotnet user-secrets set "Cognito:ClientId" $clientId | Out-Null
    dotnet user-secrets set "Cognito:ClientSecret" $client.UserPoolClient.ClientSecret | Out-Null
    dotnet user-secrets set "Cognito:Domain" "https://$DomainPrefix.auth.$Region.amazoncognito.com" | Out-Null
}
finally {
    Pop-Location
}
$client = $null
Write-Host "Saved Cognito settings (including the client secret) to the API's user-secrets."

if ($AdminEmail) {
    aws cognito-idp admin-create-user @common `
        --user-pool-id $poolId `
        --username $AdminEmail `
        --user-attributes Name=email,Value=$AdminEmail Name=email_verified,Value=true `
        --desired-delivery-mediums EMAIL `
        --query "User.Attributes[?Name=='sub'].Value" --output text | ForEach-Object { Write-Host "User $AdminEmail created, sub=$_ (temporary password sent by email)" }
}
