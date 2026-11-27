# Creates the shared secret CloudFront sends to the API (X-Ks-Origin-Verify) so the public Lambda
# function URL can't be called directly. Stored as a standard String parameter (free) because
# CloudFront origin headers can't read SecureStrings; it never appears in git or the template.
# Rotating it: run with -Force, then redeploy the stack (CloudFront picks the value up at deploy time).
param(
    [string]$Profile = "careflow",
    [string]$Region = "us-east-1",
    [string]$Stage = "dev",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$name = "/kingdom-stack/$Stage/config/OriginVerify/Secret"

$existing = aws ssm get-parameters --profile $Profile --region $Region --names $name --query "Parameters[0].Name" --output text
if ($existing -eq $name -and -not $Force) {
    Write-Host "$name already exists; leaving it unchanged (use -Force to rotate)."
    return
}

$bytes = [byte[]]::new(32)
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$secret = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

aws ssm put-parameter --profile $Profile --region $Region --name $name --type String `
    --value $secret --overwrite --query "Version" --output text | ForEach-Object { Write-Host "Saved $name (version $_)" }
$secret = $null
