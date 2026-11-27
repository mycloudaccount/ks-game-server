# Shows how the Azure DevOps main/dev branches relate to GitHub's master, for a repo with both remotes.
param([Parameter(Mandatory)] [string]$Repo)

git -C $Repo fetch --all --quiet 2>&1 | Out-Null
foreach ($b in 'azure/main', 'azure/dev', 'origin/master', 'origin/main', 'origin/dev') {
    $sha = git -C $Repo rev-parse --verify --quiet --short $b
    if ($sha) {
        "{0,-14} {1}  {2}" -f $b, $sha, (git -C $Repo log -1 --format='%cs %s' $b)
    }
}

git -C $Repo merge-base --is-ancestor origin/master azure/main
"azure/main contains origin/master: $($LASTEXITCODE -eq 0)"
git -C $Repo merge-base --is-ancestor azure/main azure/dev
"azure/dev contains azure/main: $($LASTEXITCODE -eq 0)"
"commits on origin/master not on azure/main: " + (git -C $Repo rev-list --count azure/main..origin/master)
