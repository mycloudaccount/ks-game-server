# Write test for conditional saves: create, duplicate create, stale update, good update, delete.
# Uses a throwaway foot-trail particle effect so no real user data is touched.
param([string]$BaseUrl = "http://localhost:4000")

$ProgressPreference = 'SilentlyContinue'
$id = "zz-s3-write-test-" + [guid]::NewGuid().ToString("N").Substring(0, 8)
$url = "$BaseUrl/api/assets/particle-effects/foot-trails"

function New-Effect([string]$color) {
    @{
        id     = $id
        type   = "footTrail"
        config = @{
            shape = "circle"; primaryColor = $color; secondaryColor = "#000000"
            sizeMin = 1; sizeMax = 2; endSizeMin = 0; endSizeMax = 1
            lifeMinMs = 100; lifeMaxMs = 200; spawnRateMs = 50; spawnCount = 1; stepMode = "alternate"
            leftOffset = @{ x = -2; y = 0 }; rightOffset = @{ x = 2; y = 0 }
        }
    }
}

function Invoke-Step([string]$label, [string]$method, [string]$uri, $body) {
    try {
        $json = if ($body) { $body | ConvertTo-Json -Depth 6 } else { $null }
        $r = Invoke-WebRequest $uri -Method $method -Body $json -ContentType "application/json" -UseBasicParsing -TimeoutSec 60
        Write-Host ("{0,-34} {1}" -f $label, $r.StatusCode)
        return $r
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        $reader = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
        Write-Host ("{0,-34} {1} {2}" -f $label, $status, $reader.ReadToEnd())
        return $null
    }
}

$created = Invoke-Step "create (expect 201)" POST $url @{ name = "S3 write test"; effect = (New-Effect "#ff0000") }
if (-not $created) { return }
$etag = ($created.Content | ConvertFrom-Json).eTag
"  etag after create: $etag"

Invoke-Step "duplicate create (expect 409)" POST $url @{ name = "dup"; effect = (New-Effect "#ff0000") } | Out-Null
Invoke-Step "stale etag update (expect 412)" PUT "$url/$id" @{ name = "stale"; eTag = '"not-the-real-etag"'; effect = (New-Effect "#00ff00") } | Out-Null
$updated = Invoke-Step "good etag update (expect 200)" PUT "$url/$id" @{ name = "updated"; eTag = $etag; effect = (New-Effect "#0000ff") }
if ($updated) {
    $u = $updated.Content | ConvertFrom-Json
    "  new etag: $($u.eTag)  name: $($u.name)  color: $($u.effect.config.primaryColor)"
}
Invoke-Step "old etag again (expect 412)" PUT "$url/$id" @{ name = "stale2"; eTag = $etag; effect = (New-Effect "#00ff00") } | Out-Null
Invoke-Step "delete (expect 204)" DELETE "$url/$id" $null | Out-Null
Invoke-Step "get after delete (expect 404)" GET "$url/$id" $null | Out-Null
Invoke-Step "delete again (expect 404)" DELETE "$url/$id" $null | Out-Null
