# Lists blobs in an Azure container under a prefix (filtered in PowerShell, because az.cmd mangles
# JMESPath characters like ! and [ ]).
param(
    [Parameter(Mandatory)] [string]$Account,
    [string]$Container = "game-assets",
    [string]$Prefix = "",
    [switch]$ExcludeUsers,
    [switch]$CountOnly
)

$args = @("storage", "blob", "list", "--account-name", $Account, "-c", $Container, "--auth-mode", "login", "--num-results", "5000", "-o", "json")
if ($Prefix) { $args += @("--prefix", $Prefix) }
$blobs = az @args | ConvertFrom-Json
if ($ExcludeUsers) { $blobs = $blobs | Where-Object { $_.name -notmatch '/users/' } }

if ($CountOnly) {
    "{0}/{1} prefix '{2}': {3} blobs, {4:N1} MB" -f $Account, $Container, $Prefix, @($blobs).Count, ((@($blobs) | ForEach-Object { $_.properties.contentLength } | Measure-Object -Sum).Sum / 1MB)
    return
}
foreach ($b in $blobs) { "{0,12:N0}  {1}  {2}" -f $b.properties.contentLength, $b.properties.lastModified, $b.name }
