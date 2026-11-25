import * as cdk from "aws-cdk-lib";
import * as cloudfront from "aws-cdk-lib/aws-cloudfront";
import * as origins from "aws-cdk-lib/aws-cloudfront-origins";
import * as iam from "aws-cdk-lib/aws-iam";
import * as ssm from "aws-cdk-lib/aws-ssm";
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
  /**
   * Origin the browser sees (the CloudFront address or custom domain). It can't be read from the
   * distribution inside this stack without a dependency cycle, so set it after the first deploy.
   */
  publicOrigin?: string;
}

/**
 * KingdomStackServer.Api as a single Lambda function with a function URL.
 * KingdomStackServer.Api on Lambda plus the editor on S3, both behind one CloudFront distribution.
 * Everything here stays inside the AWS always-free tiers at Kingdom Stack's usage:
 * Lambda (1M requests, 400k GB-s), function URLs (no charge), SSM standard parameters,
 * CloudWatch Logs (5 GB, kept for 2 weeks), CloudFront (1 TB out, 10M requests,
 * 2M CloudFront Function calls), and S3 for a few MB of editor files.
 */
export class KingdomStackApiStack extends cdk.Stack {
  constructor(scope: Construct, id: string, props: KingdomStackApiStackProps) {
    super(scope, id, props);

    const bucket = s3.Bucket.fromBucketName(this, "Data", props.bucketName);
    const configPath = `/kingdom-stack/${props.stage}/config`;
    const dataProtectionPath = `/kingdom-stack/${props.stage}/data-protection`;
    const s3Url = (prefix: string) => `s3://${props.bucketName}/${prefix}`;
    const parameterArn = (pathPattern: string) =>
      this.formatArn({
        service: "ssm",
        resource: "parameter",
        resourceName: pathPattern.replace(/^\//, ""),
      });

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
        actions: [
          "ssm:GetParametersByPath",
          "ssm:PutParameter",
          "ssm:AddTagsToResource",
        ],
        resources: [parameterArn(`${dataProtectionPath}*`)],
      }),
    );

    // Public URL, but the API refuses requests without CloudFront's X-Ks-Origin-Verify header.
    const functionUrl = fn.addFunctionUrl({
      authType: lambda.FunctionUrlAuthType.NONE,
    });

    // --- Editor hosting: one CloudFront address serves the editor (S3) and /api/* (Lambda),
    // so the sign-in cookie is first-party and the API needs no CORS.
    const webBucket = new s3.Bucket(this, "Web", {
      blockPublicAccess: s3.BlockPublicAccess.BLOCK_ALL,
      encryption: s3.BucketEncryption.S3_MANAGED,
      enforceSSL: true,
    });

    // Same behavior as the old staticwebapp.config.json navigationFallback: paths without a file
    // extension get index.html. Runs only on the editor behavior, never on /api/*.
    const spaFallback = new cloudfront.Function(this, "SpaFallback", {
      runtime: cloudfront.FunctionRuntime.JS_2_0,
      code: cloudfront.FunctionCode.fromInline(`
function handler(event) {
  var request = event.request;
  var lastSegment = request.uri.substring(request.uri.lastIndexOf('/') + 1);
  if (lastSegment.indexOf('.') === -1) {
    request.uri = '/index.html';
  }
  return request;
}`),
    });

    // Resolved at deploy time; the API loads the same parameter through KS_SSM_CONFIG_PATH.
    const originSecret = ssm.StringParameter.valueForStringParameter(
      this,
      `${configPath}/OriginVerify/Secret`,
    );

    const distribution = new cloudfront.Distribution(this, "Site", {
      comment: `Kingdom Stack ${props.stage}`,
      priceClass: cloudfront.PriceClass.PRICE_CLASS_100,
      defaultRootObject: "index.html",
      defaultBehavior: {
        origin: origins.S3BucketOrigin.withOriginAccessControl(webBucket),
        viewerProtocolPolicy: cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
        cachePolicy: cloudfront.CachePolicy.CACHING_OPTIMIZED,
        functionAssociations: [
          {
            function: spaFallback,
            eventType: cloudfront.FunctionEventType.VIEWER_REQUEST,
          },
        ],
      },
      additionalBehaviors: {
        "/api/*": {
          origin: new origins.FunctionUrlOrigin(functionUrl, {
            customHeaders: { "X-Ks-Origin-Verify": originSecret },
          }),
          viewerProtocolPolicy:
            cloudfront.ViewerProtocolPolicy.REDIRECT_TO_HTTPS,
          allowedMethods: cloudfront.AllowedMethods.ALLOW_ALL,
          cachePolicy: cloudfront.CachePolicy.CACHING_DISABLED,
          originRequestPolicy:
            cloudfront.OriginRequestPolicy.ALL_VIEWER_EXCEPT_HOST_HEADER,
        },
      },
    });

    new cdk.CfnOutput(this, "FunctionUrl", { value: functionUrl.url });
    new cdk.CfnOutput(this, "FunctionName", { value: fn.functionName });
    new cdk.CfnOutput(this, "SiteUrl", {
      value: `https://${distribution.distributionDomainName}`,
    });
    new cdk.CfnOutput(this, "DistributionId", {
      value: distribution.distributionId,
    });
    new cdk.CfnOutput(this, "WebBucketName", { value: webBucket.bucketName });
  }
}
