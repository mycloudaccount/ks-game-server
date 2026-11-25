# Builds the API for the Lambda .NET 10 runtime (Linux, ARM64) into artifacts/lambda-api.
$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$output = Join-Path $repoRoot "artifacts\lambda-api"

if (Test-Path $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

dotnet publish (Join-Path $repoRoot "KingdomStackServer.Api\KingdomStackServer.Api.csproj") `
    -c Release -r linux-arm64 --self-contained false -o $output -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# Local-only settings must not ship.
Remove-Item -LiteralPath (Join-Path $output "appsettings.Development.json") -ErrorAction SilentlyContinue

$size = (Get-ChildItem $output -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Published to {0} ({1:N1} MB)" -f $output, ($size / 1MB))
