# Prepares both GitHub repos for the Actions workflows (uses the gh CLI; safe to re-run):
#   - "dev" and "prod" environments; dev only accepts the dev branch, prod only accepts main
#   - repo variable AWS_DEPLOY_ROLE_ARN pointing at that repo's deploy role
param(
    [string]$Owner = "mycloudaccount",
    [string]$Account = "897154406373"
)

$ErrorActionPreference = "Stop"
$repos = @{
    "ks-game-server" = "arn:aws:iam::${Account}:role/ks-github-api-deploy"
    "ks-game-editor" = "arn:aws:iam::${Account}:role/ks-github-editor-deploy"
}
$environments = @{ dev = "dev"; prod = "main" }

foreach ($repo in $repos.Keys) {
    $full = "$Owner/$repo"
    foreach ($envName in $environments.Keys) {
        $branch = $environments[$envName]
        '{"deployment_branch_policy":{"protected_branches":false,"custom_branch_policies":true}}' |
            gh api --method PUT "repos/$full/environments/$envName" --input - | Out-Null

        $existing = gh api "repos/$full/environments/$envName/deployment-branch-policies" --jq ".branch_policies[].name"
        if (@($existing) -notcontains $branch) {
            gh api --method POST "repos/$full/environments/$envName/deployment-branch-policies" -f name=$branch -f type=branch | Out-Null
        }
        Write-Host "$full environment '$envName' accepts branch '$branch'"
    }

    gh variable set AWS_DEPLOY_ROLE_ARN --repo $full --body $repos[$repo]
    Write-Host "$full AWS_DEPLOY_ROLE_ARN = $($repos[$repo])"
}
