#!/usr/bin/env node
import * as path from "node:path";
import { fileURLToPath } from "node:url";
import * as cdk from "aws-cdk-lib";
import { KingdomStackApiStack } from "../lib/api-stack.js";

const repoRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  "../../../..",
);
const app = new cdk.App();

const shared = {
  env: { account: "897154406373", region: "us-east-1" },
  codePath: path.join(repoRoot, "artifacts", "lambda-api"),
  // One user pool and app client for both stages; each stage's address is registered as a callback.
  cognito: {
    userPoolId: "us-east-1_1mHZH5Kn6",
    clientId: "4v0ndv46h41s4gojcbfou39njl",
    domain: "https://kingdomstack.auth.us-east-1.amazoncognito.com",
  },
};

new KingdomStackApiStack(app, "KingdomStackApiDev", {
  ...shared,
  stage: "dev",
  bucketName: "kingdom-stack-dev-897154406373",
  // The SiteUrl output from the first deploy.
  publicOrigin: "https://dn2b9xiqserj6.cloudfront.net",
  tags: { app: "kingdom-stack", env: "dev" },
});

new KingdomStackApiStack(app, "KingdomStackApiProd", {
  ...shared,
  stage: "prod",
  bucketName: "kingdom-stack-prod-897154406373",
  // Sign-in only works on this address; the d12ch69blsir6f.cloudfront.net address still serves the site.
  publicOrigin: "https://kingdomstack.org",
  customDomain: {
    name: "kingdomstack.org",
    certificateArn:
      "arn:aws:acm:us-east-1:897154406373:certificate/c7100bf8-5031-455b-9725-5f167415f49a",
  },
  tags: { app: "kingdom-stack", env: "prod" },
});
