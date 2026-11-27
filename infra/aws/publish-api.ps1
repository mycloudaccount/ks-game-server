# Builds the API for the Lambda .NET 10 runtime (Linux, ARM64) into artifacts/lambda-api.
# Works on Windows and on Linux (GitHub Actions) with PowerShell 7.
$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, "..", ".."))
$project = [IO.Path]::Combine($repoRoot, "KingdomStackServer.Api", "KingdomStackServer.Api.csproj")
$output = [IO.Path]::Combine($repoRoot, "artifacts", "lambda-api")

if (Test-Path $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

dotnet publish $project -c Release -r linux-arm64 --self-contained false -o $output -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# Local-only settings must not ship.
Remove-Item -LiteralPath ([IO.Path]::Combine($output, "appsettings.Development.json")) -ErrorAction SilentlyContinue

$size = (Get-ChildItem $output -Recurse -File | Measure-Object Length -Sum).Sum
Write-Host ("Published to {0} ({1:N1} MB)" -f $output, ($size / 1MB))
