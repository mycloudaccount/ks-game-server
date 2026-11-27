# Checks that the API refuses anonymous access and that sign-in redirects to Cognito correctly.
param([string]$BaseUrl = "http://localhost:4000")

$ProgressPreference = 'SilentlyContinue'

function Get-Status([string]$path) {
    try {
        $r = Invoke-WebRequest "$BaseUrl$path" -UseBasicParsing -MaximumRedirection 0 -TimeoutSec 60
        return @{ Status = $r.StatusCode; Body = $r.Content; Location = $r.Headers.Location }
    }
    catch {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { return @{ Status = "ERR"; Body = $_.Exception.Message } }
        return @{ Status = $resp.StatusCode.value__; Location = $resp.Headers["Location"] }
    }
}

$me = Get-Status "/api/auth/me"
Write-Host ("{0,-40} {1} {2}" -f "/api/auth/me (anonymous)", $me.Status, $me.Body)

foreach ($p in '/api/games', '/api/preferences', '/api/assets/tiles/list', '/api/assets/tiles/bundle', '/api/auth/dev-session') {
    $r = Get-Status $p
    Write-Host ("{0,-40} {1}  (expect 401)" -f "$p (anonymous)", $r.Status)
}

$login = Get-Status "/api/auth/login?post_login_redirect_uri=http%3A%2F%2Flocalhost%3A4000%2F"
Write-Host ("{0,-40} {1}" -f "/api/auth/login", $login.Status)
if ($login.Location) {
    $uri = [Uri]$login.Location
    Write-Host "  redirects to host: $($uri.Host)"
    $query = [System.Web.HttpUtility]::ParseQueryString($uri.Query)
    foreach ($k in 'response_type', 'redirect_uri', 'scope', 'code_challenge_method', 'response_mode') {
        Write-Host ("  {0,-22} {1}" -f $k, $query[$k])
    }
}

$evil = Get-Status "/api/auth/login?post_login_redirect_uri=https%3A%2F%2Fevil.example.com%2F"
Write-Host ("{0,-40} {1} (open-redirect attempt; target is checked after sign-in)" -f "/api/auth/login evil return", $evil.Status)
