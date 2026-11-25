# Copies the Cognito app client secret into SSM Parameter Store as a SecureString
# (encrypted with the free AWS-managed aws/ssm key). The secret is never printed.
param(
    [string]$Profile = "careflow",
    [string]$Region = "us-east-1",
    [string]$Stage = "dev",
    [string]$UserPoolId = "us-east-1_1mHZH5Kn6",
    [string]$ClientId = "4v0ndv46h41s4gojcbfou39njl"
)

$ErrorActionPreference = "Stop"
$name = "/kingdom-stack/$Stage/config/Cognito/ClientSecret"

$secret = aws cognito-idp describe-user-pool-client --profile $Profile --region $Region `
    --user-pool-id $UserPoolId --client-id $ClientId --query "UserPoolClient.ClientSecret" --output text
if (-not $secret -or $secret -eq "None") {
    throw "Could not read the client secret."
}

aws ssm put-parameter --profile $Profile --region $Region --name $name --type SecureString `
    --value $secret --overwrite --query "Version" --output text | ForEach-Object { Write-Host "Saved $name (version $_)" }
$secret = $null
