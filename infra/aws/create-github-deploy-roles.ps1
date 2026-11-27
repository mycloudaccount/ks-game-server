# Lets GitHub Actions deploy Kingdom Stack without stored AWS keys (GitHub OIDC).
# Creates, or updates if they already exist:
#   - the GitHub OIDC identity provider (free)
#   - ks-github-api-deploy:    usable only by mycloudaccount/ks-game-server from its dev/prod environments;
#                              may only assume the CDK bootstrap roles (CDK does the actual deploy)
#   - ks-github-editor-deploy: usable only by mycloudaccount/ks-game-editor from its dev/prod environments;
#                              may only upload to the two site buckets and clear the two distributions
# Re-run after redeploying a stack whose bucket or distribution changed.
param(
    [string]$Profile = "careflow",
    [string]$Region = "us-east-1",
    [string]$Owner = "mycloudaccount",
    [string[]]$Stacks = @("KingdomStackApiDev", "KingdomStackApiProd")
)

$ErrorActionPreference = "Stop"
$common = @("--profile", $Profile)
$account = aws sts get-caller-identity @common --query Account --output text
$providerUrl = "token.actions.githubusercontent.com"
$providerArn = "arn:aws:iam::${account}:oidc-provider/$providerUrl"

$providers = @(aws iam list-open-id-connect-providers @common --query "OpenIDConnectProviderList[].Arn" --output json | ConvertFrom-Json)
if ($providers -notcontains $providerArn) {
    # AWS verifies GitHub's certificate itself; the thumbprint is required by older CLIs but not used.
    aws iam create-open-id-connect-provider @common --url "https://$providerUrl" `
        --client-id-list sts.amazonaws.com --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1 | Out-Null
    Write-Host "Created OIDC provider $providerArn"
}

$sites = foreach ($stack in $Stacks) {
    $outputs = aws cloudformation describe-stacks @common --region $Region --stack-name $stack --query "Stacks[0].Outputs" --output json | ConvertFrom-Json
    [pscustomobject]@{
        Stack          = $stack
        StackArn       = aws cloudformation describe-stacks @common --region $Region --stack-name $stack --query "Stacks[0].StackId" --output text
        Bucket         = ($outputs | Where-Object OutputKey -eq "WebBucketName").OutputValue
        DistributionId = ($outputs | Where-Object OutputKey -eq "DistributionId").OutputValue
    }
}

function New-TrustPolicy([string]$repo) {
    @{
        Version   = "2012-10-17"
        Statement = @(@{
            Effect    = "Allow"
            Principal = @{ Federated = $providerArn }
            Action    = "sts:AssumeRoleWithWebIdentity"
            Condition = @{
                StringEquals = @{ "${providerUrl}:aud" = "sts.amazonaws.com" }
                StringLike   = @{ "${providerUrl}:sub" = @(
                    "repo:${Owner}/${repo}:environment:dev",
                    "repo:${Owner}/${repo}:environment:prod"
                ) }
            }
        })
    }
}

$bootstrapRoles = "deploy", "file-publishing", "lookup" | ForEach-Object {
    "arn:aws:iam::${account}:role/cdk-hnb659fds-$_-role-$account-$Region"
}
$apiPolicy = @{
    Version   = "2012-10-17"
    Statement = @(@{ Sid = "UseCdkBootstrapRoles"; Effect = "Allow"; Action = "sts:AssumeRole"; Resource = @($bootstrapRoles) })
}

$editorPolicy = @{
    Version   = "2012-10-17"
    Statement = @(
        @{ Sid = "ReadStackOutputs"; Effect = "Allow"; Action = "cloudformation:DescribeStacks"; Resource = @($sites.StackArn) },
        @{ Sid = "ListSiteBuckets"; Effect = "Allow"; Action = "s3:ListBucket"; Resource = @($sites | ForEach-Object { "arn:aws:s3:::$($_.Bucket)" }) },
        @{ Sid = "WriteSiteFiles"; Effect = "Allow"; Action = @("s3:PutObject", "s3:DeleteObject", "s3:GetObject"); Resource = @($sites | ForEach-Object { "arn:aws:s3:::$($_.Bucket)/*" }) },
        @{ Sid = "ClearSiteCache"; Effect = "Allow"; Action = "cloudfront:CreateInvalidation"; Resource = @($sites | ForEach-Object { "arn:aws:cloudfront::${account}:distribution/$($_.DistributionId)" }) }
    )
}

$tmp = Join-Path $env:TEMP "ks-gh-roles-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force $tmp | Out-Null
try {
    foreach ($role in @(
            @{ Name = "ks-github-api-deploy"; Repo = "ks-game-server"; Policy = $apiPolicy; Description = "GitHub Actions deploy for ks-game-server (CDK)" },
            @{ Name = "ks-github-editor-deploy"; Repo = "ks-game-editor"; Policy = $editorPolicy; Description = "GitHub Actions deploy for ks-game-editor (S3 + CloudFront)" }
        )) {
        $trustFile = Join-Path $tmp "$($role.Name)-trust.json"
        $policyFile = Join-Path $tmp "$($role.Name)-policy.json"
        New-TrustPolicy $role.Repo | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $trustFile -Encoding ascii
        $role.Policy | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $policyFile -Encoding ascii

        $exists = aws iam list-roles @common --query "Roles[?RoleName=='$($role.Name)'].RoleName" --output text
        if ($exists -eq $role.Name) {
            aws iam update-assume-role-policy @common --role-name $role.Name --policy-document "file://$trustFile"
        }
        else {
            aws iam create-role @common --role-name $role.Name --description $role.Description `
                --assume-role-policy-document "file://$trustFile" --max-session-duration 3600 `
                --tags Key=app,Value=kingdom-stack | Out-Null
        }
        aws iam put-role-policy @common --role-name $role.Name --policy-name deploy --policy-document "file://$policyFile"
        Write-Host ("{0,-24} arn:aws:iam::{1}:role/{0}" -f $role.Name, $account)
    }
}
finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
