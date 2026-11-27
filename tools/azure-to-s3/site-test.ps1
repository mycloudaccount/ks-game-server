# Checks the CloudFront site: editor files, SPA fallback, API routing, and that the function URL
# can't be called directly.
param(
    [string]$SiteUrl = "https://dn2b9xiqserj6.cloudfront.net",
    [string]$FunctionUrl = "https://nbty7r3qnefhpsxvwfxz2skeim0kwqei.lambda-url.us-east-1.on.aws"
)

$ProgressPreference = 'SilentlyContinue'

function Show([string]$label, [string]$url, [string]$expect) {
    try {
        $r = Invoke-WebRequest $url -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 60
        $type = $r.Headers['Content-Type']
        $cache = $r.Headers['Cache-Control']
        Write-Host ("{0,-38} {1}  {2,-26} cache={3}  (expect {4})" -f $label, $r.StatusCode, $type, $cache, $expect)
        return $r
    }
    catch {
        $resp = $_.Exception.Response
        $status = if ($resp) { $resp.StatusCode.value__ } else { "ERR $($_.Exception.Message)" }
        Write-Host ("{0,-38} {1}  (expect {2})" -f $label, $status, $expect)
        return $null
    }
}

$index = Show "/ (editor)" "$SiteUrl/" "200 text/html no-cache"
Show "/some/deep/link (SPA fallback)" "$SiteUrl/some/deep/link" "200 text/html" | Out-Null

if ($index) {
    $asset = [regex]::Match($index.Content, 'src="(/assets/[^"]+\.js)"').Groups[1].Value
    if ($asset) { Show "editor JS bundle" "$SiteUrl$asset" "200 javascript immutable" | Out-Null }
}

Show "/api/auth/me" "$SiteUrl/api/auth/me" "200" | Out-Null
Show "/api/games (anonymous)" "$SiteUrl/api/games" "401" | Out-Null
Show "/api/no-such-route (anonymous)" "$SiteUrl/api/no-such-route" "401, not index.html" | Out-Null

try {
    $null = Invoke-WebRequest "$SiteUrl/api/auth/login" -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 60
}
catch {
    $location = $_.Exception.Response.Headers["Location"]
    $redirect = [System.Web.HttpUtility]::ParseQueryString(([Uri]$location).Query)["redirect_uri"]
    Write-Host ("{0,-38} redirect_uri={1}" -f "/api/auth/login", $redirect)
}

Show "function URL direct /api/auth/me" "$FunctionUrl/api/auth/me" "403" | Out-Null
Show "function URL direct /" "$FunctionUrl/" "403" | Out-Null
