# Allows a new site origin to use Cognito sign-in by adding {origin}/api/auth/callback and {origin}/
# to the app client. update-user-pool-client resets any setting it is not given, so this reads the
# current client and re-sends every setting. The client secret is not part of the request.
param(
    [Parameter(Mandatory)] [string]$Origin,
    [string]$Profile = "careflow",
    [string]$Region = "us-east-1",
    [string]$UserPoolId = "us-east-1_1mHZH5Kn6",
    [string]$ClientId = "4v0ndv46h41s4gojcbfou39njl"
)

$ErrorActionPreference = "Stop"
$origin = $Origin.TrimEnd('/')
$current = (aws cognito-idp describe-user-pool-client --profile $Profile --region $Region `
    --user-pool-id $UserPoolId --client-id $ClientId --output json | ConvertFrom-Json).UserPoolClient

$callbacks = @($current.CallbackURLs) + "$origin/api/auth/callback" | Where-Object { $_ } | Select-Object -Unique
$logouts = @($current.LogoutURLs) + "$origin/" | Where-Object { $_ } | Select-Object -Unique

$request = [ordered]@{
    UserPoolId                      = $UserPoolId
    ClientId                        = $ClientId
    ClientName                      = $current.ClientName
    RefreshTokenValidity            = $current.RefreshTokenValidity
    AccessTokenValidity             = $current.AccessTokenValidity
    IdTokenValidity                 = $current.IdTokenValidity
    TokenValidityUnits              = $current.TokenValidityUnits
    ReadAttributes                  = $current.ReadAttributes
    WriteAttributes                 = $current.WriteAttributes
    ExplicitAuthFlows               = $current.ExplicitAuthFlows
    SupportedIdentityProviders      = $current.SupportedIdentityProviders
    CallbackURLs                    = @($callbacks)
    LogoutURLs                      = @($logouts)
    AllowedOAuthFlows               = $current.AllowedOAuthFlows
    AllowedOAuthScopes              = $current.AllowedOAuthScopes
    AllowedOAuthFlowsUserPoolClient = $current.AllowedOAuthFlowsUserPoolClient
    PreventUserExistenceErrors      = $current.PreventUserExistenceErrors
    EnableTokenRevocation           = $current.EnableTokenRevocation
}
foreach ($key in @($request.Keys)) {
    if ($null -eq $request[$key]) { $request.Remove($key) }
}

$file = Join-Path $env:TEMP "ks-cognito-client-$([guid]::NewGuid().ToString('N')).json"
try {
    $request | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $file -Encoding ascii
    aws cognito-idp update-user-pool-client --profile $Profile --region $Region --cli-input-json "file://$file" `
        --query "UserPoolClient.{callbacks:CallbackURLs,logouts:LogoutURLs}" --output json
}
finally {
    Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue
}
