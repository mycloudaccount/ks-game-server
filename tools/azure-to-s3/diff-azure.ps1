# Compares two Azure containers by blob name and size.
param(
    [string]$Left = "kstackdevstorage",
    [string]$Right = "ksstorage",
    [string]$Container = "game-assets"
)

function Get-Blobs([string]$account) {
    $map = @{}
    foreach ($b in (az storage blob list --account-name $account -c $Container --auth-mode login --num-results 5000 -o json | ConvertFrom-Json)) {
        $map[$b.name] = [long]$b.properties.contentLength
    }
    return $map
}

$l = Get-Blobs $Left
$r = Get-Blobs $Right

"Only in ${Right}:"
$r.Keys | Where-Object { -not $l.ContainsKey($_) } | Sort-Object | ForEach-Object { "  {0,12:N0}  {1}" -f $r[$_], $_ }
"Only in ${Left}:"
$l.Keys | Where-Object { -not $r.ContainsKey($_) } | Sort-Object | ForEach-Object { "  {0,12:N0}  {1}" -f $l[$_], $_ }
"Different size:"
$l.Keys | Where-Object { $r.ContainsKey($_) -and $r[$_] -ne $l[$_] } | Sort-Object | ForEach-Object { "  $Left={0:N0} $Right={1:N0}  {2}" -f $l[$_], $r[$_], $_ }
