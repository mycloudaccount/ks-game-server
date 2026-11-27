# Waits for the latest GitHub Actions run of each repo for a branch/event and prints the result.
param(
    [string[]]$Repos = @("mycloudaccount/ks-game-server", "mycloudaccount/ks-game-editor"),
    [Parameter(Mandatory)] [string]$Branch,
    [Parameter(Mandatory)] [ValidateSet("push", "pull_request")] [string]$Event,
    [int]$TimeoutMinutes = 15
)

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
foreach ($repo in $Repos) {
    do {
        $run = gh run list --repo $repo --branch $Branch --event $Event --limit 1 --json status,conclusion,url | ConvertFrom-Json | Select-Object -First 1
        if ($run -and $run.status -eq "completed") { break }
        Start-Sleep 15
    } while ((Get-Date) -lt $deadline)

    if ($run) { "{0}: {1} {2}  {3}" -f $repo, $run.status, $run.conclusion, $run.url }
    else { "${repo}: no run found" }
}
