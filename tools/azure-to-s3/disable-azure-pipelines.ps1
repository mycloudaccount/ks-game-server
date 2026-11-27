# Disables (does not delete) the Azure DevOps pipelines now that GitHub Actions deploys to AWS.
# A disabled pipeline ignores triggers and can't be queued; re-enable with -Status enabled.
param(
    [string]$Org = "https://dev.azure.com/codesaints",
    [string]$Project = "kingdomstack",
    [int[]]$Ids = @(1, 2),
    [ValidateSet("disabled", "enabled")] [string]$Status = "disabled"
)

$ErrorActionPreference = "Stop"
foreach ($id in $Ids) {
    $definition = az devops invoke --org $Org --area build --resource definitions `
        --route-parameters project=$Project definitionId=$id --http-method GET --api-version 7.1 -o json | ConvertFrom-Json
    $definition.queueStatus = $Status

    $file = Join-Path $env:TEMP "ks-ado-definition-$id.json"
    try {
        # az devops invoke rejects a UTF-8 byte-order mark, which Windows PowerShell's -Encoding utf8 adds.
        [IO.File]::WriteAllText($file, ($definition | ConvertTo-Json -Depth 50), [Text.UTF8Encoding]::new($false))
        $updated = az devops invoke --org $Org --area build --resource definitions `
            --route-parameters project=$Project definitionId=$id --http-method PUT --api-version 7.1 `
            --in-file $file -o json | ConvertFrom-Json
        "{0} (id {1}): {2}" -f $updated.name, $id, $updated.queueStatus
    }
    finally {
        Remove-Item -LiteralPath $file -ErrorAction SilentlyContinue
    }
}
