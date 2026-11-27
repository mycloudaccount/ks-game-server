# Azure to S3 migration tools

Used to move Kingdom Stack data from Azure Blob Storage to S3 and to check the AWS deployment.
Azure is only ever read. Credentials come from `az login` and the `careflow` AWS profile.

## Copying data

| Script | What it does |
|---|---|
| `dotnet run -- <azure-container-url> <s3://bucket/prefix> [--dry-run]` | Copies one container, keeping content type, metadata and the original created/modified dates. |
| `copy-prod.ps1` | Copies all four production containers into the production bucket, then copies the legacy user's files to the Cognito user. |
| `copy-user-scope.ps1` | Copies one user's files to another user ID in the same bucket. Skips files that already exist unless `-Overwrite` is given. |

## Checking

| Script | What it checks |
|---|---|
| `compare.ps1` | An Azure container against an S3 prefix, by name and size. |
| `list-azure.ps1`, `diff-azure.ps1` | Lists one Azure container, or diffs two. |
| `site-test.ps1` | A deployed site: editor files, SPA fallback, `/api` routing, and that the function URL rejects direct calls. |
| `auth-test.ps1` | Signed-out access is refused and sign-in redirects to Cognito correctly. |
| `run-test-api.ps1` + `smoke-test.ps1`, `write-test.ps1`, `cache-test.ps1` | Runs the API locally against the dev bucket without sign-in (Development only), then checks the editor's endpoints, conditional writes, and caching / 304 responses. |
