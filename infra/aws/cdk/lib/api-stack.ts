import * as cdk from "aws-cdk-lib";
import * as iam from "aws-cdk-lib/aws-iam";
import * as lambda from "aws-cdk-lib/aws-lambda";
import * as logs from "aws-cdk-lib/aws-logs";
import * as s3 from "aws-cdk-lib/aws-s3";
import { Construct } from "constructs";

export interface KingdomStackApiStackProps extends cdk.StackProps {
  /** "dev" or "prod"; used in SSM parameter paths. */
  stage: string;
  /** Existing bucket holding game-assets/, user-games/, user-preferences/, editor-messages/. */
  bucketName: string;
  /** Output of `dotnet publish -r linux-arm64` for KingdomStackServer.Api. */
  codePath: string;
  cognito: { userPoolId: string; clientId: string; domain: string };
  /** Origin the browser sees when the API sits behind CloudFront. Leave unset to use the function URL itself. */
  publicOrigin?: string;
}

/**
 * KingdomStackServer.Api as a single Lambda function with a function URL.
 * Everything here stays inside the AWS always-free tiers at Kingdom Stack's usage:
 * Lambda (1M requests, 400k GB-s), function URLs (no charge), SSM standard parameters,
 * and CloudWatch Logs (5 GB, kept for 2 weeks).
 */
export class KingdomStackApiStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: KingdomStackApiStackProps) {
    super(scope, id, props);

    const bucket = s3.Bucket.fromBucketName(this, "Data", props.bucketName);
    const configPath = `/kingdom-stack/${props.stage}/config`;
    const dataProtectionPath = `/kingdom-stack/${props.stage}/data-protection`;
    const s3Url = (prefix: string) => `s3://${props.bucketName}/${prefix}`;
    const parameterArn = (pathPattern: string) =>
      this.formatArn({ service: "ssm", resource: "parameter", resourceName: pathPattern.replace(/^\//, "") });

    const logGroup = new logs.LogGroup(this, "ApiLogs", {
      retention: logs.RetentionDays.TWO_WEEKS,
      removalPolicy: cdk.RemovalPolicy.DESTROY,
    });

    const environment: Record<string, string> = {
      ASPNETCORE_ENVIRONMENT: "Production",
      KS_SSM_CONFIG_PATH: configPath,
      DataProtection__SsmPath: dataProtectionPath,
      Identity__Mode: "Cognito",
      Cognito__Authority: `https://cognito-idp.${this.region}.amazonaws.com/${props.cognito.userPoolId}`,
      Cognito__ClientId: props.cognito.clientId,
      Cognito__Domain: props.cognito.domain,
      AzureBlobProxy__StorageBaseUrl: s3Url("game-assets"),
      AzureBlobProxy__UserGamesStorageBaseUrl: s3Url("user-games"),
      AzureBlobProxy__UserPreferencesStorageBaseUrl: s3Url("user-preferences"),
      AzureBlobProxy__ClientMessagesStorageBaseUrl: s3Url("editor-messages"),
      AzureBlobProxy__StackStampsStorageBaseUrl: s3Url("game-assets"),
      AzureBlobProxy__ParticleEffectsStorageBaseUrl: s3Url("game-assets"),
      // Lambda responses are capped at ~6 MB (binary is base64-encoded), so larger files redirect to S3.
      AzureBlobProxy__RedirectToStorageAboveBytes: "4000000",
    };
    if (props.publicOrigin) {
      environment.Cognito__PublicOrigin = props.publicOrigin;
    }

    const fn = new lambda.Function(this, "Api", {
      runtime: lambda.Runtime.DOTNET_10,
      architecture: lambda.Architecture.ARM_64,
      handler: "KingdomStackServer.Api",
      code: lambda.Code.fromAsset(props.codePath),
      memorySize: 1024,
      timeout: cdk.Duration.seconds(30),
      logGroup,
      environment,
    });

    bucket.grantReadWrite(fn);

    // Settings and secrets (Cognito client secret) loaded at startup.
    fn.addToRolePolicy(
      new iam.PolicyStatement({
        actions: ["ssm:GetParametersByPath"],
        resources: [parameterArn(configPath), parameterArn(`${configPath}/*`)],
      }),
    );

    // Shared keys for the sign-in cookie, created on first use.
    fn.addToRolePolicy(
      new iam.PolicyStatement({
        actions: ["ssm:GetParametersByPath", "ssm:PutParameter", "ssm:AddTagsToResource"],
        resources: [parameterArn(`${dataProtectionPath}*`)],
      }),
    );

    const functionUrl = fn.addFunctionUrl({ authType: lambda.FunctionUrlAuthType.NONE });

    new cdk.CfnOutput(this, "FunctionUrl", { value: functionUrl.url });
    new cdk.CfnOutput(this, "FunctionName", { value: fn.functionName });
  }
}
