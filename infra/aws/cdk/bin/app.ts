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

new KingdomStackApiStack(app, "KingdomStackApiDev", {
  env: { account: "897154406373", region: "us-east-1" },
  stage: "dev",
  bucketName: "kingdom-stack-dev-897154406373",
  codePath: path.join(repoRoot, "artifacts", "lambda-api"),
  // The SiteUrl output from the first deploy (switch to https://kingdomstack.org at cutover).
  publicOrigin: "https://dn2b9xiqserj6.cloudfront.net",
  cognito: {
    userPoolId: "us-east-1_1mHZH5Kn6",
    clientId: "4v0ndv46h41s4gojcbfou39njl",
    domain: "https://kingdomstack.auth.us-east-1.amazoncognito.com",
  },
  tags: { app: "kingdom-stack", env: "dev" },
});
