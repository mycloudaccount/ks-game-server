# Read-only smoke test: calls every endpoint the editor uses through the Vite proxy.
param([string]$BaseUrl = "http://localhost:4000")

$ProgressPreference = 'SilentlyContinue'
$endpoints = @(
    '/api/auth/dev-session',
    '/api/assets/tiles/list',
    '/api/assets/tiles/bundle',
    '/api/assets/characters/list',
    '/api/assets/sounds/list',
    '/api/assets/character-source-packs/list',
    '/api/games',
    '/api/preferences',
    '/api/characters',
    '/api/assets/stack-stamps',
    '/api/assets/particle-effects/foot-trails',
    '/api/assets/particle-effects/landing-impacts',
    '/api/client-messages'
)

foreach ($e in $endpoints) {
    try {
        $r = Invoke-WebRequest "$BaseUrl$e" -UseBasicParsing -TimeoutSec 90
        "{0,-48} {1} ({2:N0} bytes)" -f $e, $r.StatusCode, $r.Content.Length
    }
    catch {
        "{0,-48} {1} {2}" -f $e, $_.Exception.Response.StatusCode.value__, $_.Exception.Message
    }
}
