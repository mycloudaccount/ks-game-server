# Checks file caching headers and conditional requests (304) against an API that doesn't need sign-in
# (Identity:Mode=Configured, Development only).
param(
    [string]$BaseUrl = "http://127.0.0.1:5071",
    [string]$GlbPath = "/api/assets/character-source-packs/meshtint-characters/runtime/bodies/character-02.glb",
    [string]$ReferenceFile = (Join-Path $env:TEMP "ks-glb-check\s3-02.glb")
)

$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.Net.Http
$client = [System.Net.Http.HttpClient]::new([System.Net.Http.HttpClientHandler]@{ AllowAutoRedirect = $false })

function Send([string]$path, [hashtable]$headers = @{}) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, "$BaseUrl$path")
    foreach ($k in $headers.Keys) { $null = $request.Headers.TryAddWithoutValidation($k, $headers[$k]) }
    return $client.SendAsync($request).GetAwaiter().GetResult()
}

function Describe([string]$label, $r) {
    $etag = $r.Headers.ETag
    $cache = $r.Headers.CacheControl
    $modified = $r.Content.Headers.LastModified
    $length = $r.Content.Headers.ContentLength
    $location = $r.Headers.Location
    Write-Host ("{0,-34} {1}  etag={2}  last-modified={3}  cache={4}  length={5}{6}" -f $label, [int]$r.StatusCode, $etag, $modified, $cache, $length, $(if ($location) { "  location=$($location.Host)" } else { "" }))
}

$first = Send $GlbPath
Describe "GLB first request" $first
$bytes = $first.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
if (Test-Path $ReferenceFile) {
    $reference = [IO.File]::ReadAllBytes($ReferenceFile)
    $identical = $bytes.Length -eq $reference.Length -and [Linq.Enumerable]::SequenceEqual($bytes, $reference)
    Write-Host ("  body: {0:N0} bytes, identical to S3 copy: {1}" -f $bytes.Length, $identical)
}

$etag = $first.Headers.ETag
if ($etag) { Describe "GLB If-None-Match (expect 304)" (Send $GlbPath @{ "If-None-Match" = $etag.ToString() }) }
Describe "GLB wrong If-None-Match (expect 200)" (Send $GlbPath @{ "If-None-Match" = '"not-it"' })
$modified = $first.Content.Headers.LastModified
if ($modified) { Describe "GLB If-Modified-Since (expect 304)" (Send $GlbPath @{ "If-Modified-Since" = $modified.ToString("R") }) }

Describe "Large zip (expect 302 no-store)" (Send "/api/assets/character-source-packs/meshtint-characters/meshtint-characters-catalog.zip")
Describe "Tile bundle (expect 200)" (Send "/api/assets/tiles/bundle")
$json = Send "/api/games"
Write-Host ("{0,-34} {1}  cache={2}" -f "/api/games JSON (no change)", [int]$json.StatusCode, $json.Headers.CacheControl)
