using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AwsApigatewayv2Integrations;
using DependencyModules.Runtime.Attributes;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using HttpMethod = Amazon.CDK.AWS.Apigatewayv2.HttpMethod;

namespace Hardened.Amz.Cdk.Lambda;

public class LambdaRequest {
    
    public string Name { get; set; } = string.Empty;

    public string DistLocation { get; set; } = "../dist/function.zip";
    

    public string? AliasName { get; set; } = "live";

    public bool UseCodeDeploy { get; set; } = true;
    
    public Action<FunctionProps> Props { get; set; } = props => { };
    
    public Type ApplicationType { get; set; } = default!;

    /// <summary>
    /// Where the function's code comes from. Null, the default, packages the application project
    /// with <c>dotnet-lambda</c> into <see cref="DistLocation"/>.
    /// </summary>
    /// <remarks>
    /// A factory rather than a value because <c>Code.FromCustomCommand</c> runs its command the
    /// moment it is called, so the default cannot be built until it is known to be wanted. A stack
    /// test supplies an asset here; overriding <c>Code</c> through <see cref="Props"/> is too late.
    /// </remarks>
    public Func<Code>? Code { get; set; }

    /// <summary>
    /// How the function answers: one buffered payload, or a stream that opens at the first body
    /// byte. Written to the function's environment as
    /// <c>HARDENED_LAMBDA_RESPONSE_MODE</c>, which is where the application reads it, and matched
    /// to the invoke mode of the front door this helper puts in front of it.
    /// </summary>
    /// <remarks>
    /// Buffered is the default and the only mode an HTTP API can serve. Stream needs a function URL
    /// in <c>RESPONSE_STREAM</c> invoke mode; see <see cref="FunctionUrlLambdaRequest"/>.
    /// </remarks>
    public LambdaResponseMode ResponseMode { get; set; } = LambdaResponseMode.Buffered;
}

public class HttpApiLambdaRequest : LambdaRequest {

    /// <summary>
    /// CORS preflight for the API. Null — no preflight configured — is the default, and is correct
    /// for an API that browsers do not call directly.
    ///
    /// <para>
    /// This used to default to <c>AllowOrigins ["*"]</c> with <c>AllowMethods ANY</c>, so every API
    /// created through <see cref="LambdaCdkUtil.HttpApiFunctionCreate"/> was open to every origin
    /// unless the caller noticed and closed it. Name the origins:
    /// </para>
    /// <code>
    /// CorsPreflight = new CorsPreflightOptions {
    ///     AllowOrigins = ["https://app.example.com"],
    ///     AllowMethods = [CorsHttpMethod.GET, CorsHttpMethod.POST]
    /// }
    /// </code>
    /// </summary>
    public CorsPreflightOptions? CorsPreflight { get; set; }

    public Action<HttpApiProps> HttpApiProps { get; set; } = props => {};

    public Action<HttpApi, Alias> ConfigureApi { get; set; } = (api, alias) => {
        var lambdaIntegration  = new HttpLambdaIntegration("LambdaIntegration", alias);
        
        api.AddRoutes(new AddRoutesOptions {
            Path = "/{proxy+}",
            Methods = [
                HttpMethod.ANY,
            ],
            Integration = lambdaIntegration
        });
    };
    
}

/// <summary>
/// A function behind a function URL: the deployment shape that streams, and the one a CloudFront
/// distribution fronts.
/// </summary>
/// <remarks>
/// <para>
/// A function attached to a VPC does not stream through a URL at all, so this shape is for
/// functions that are not. The distribution itself stays the application's to add; the URL is
/// registered under <see cref="KnownCdkResources.LambdaFunctionUrl"/> and handed to
/// <see cref="ConfigureUrl"/> so one can be pointed at it.
/// </para>
/// </remarks>
public class FunctionUrlLambdaRequest : LambdaRequest {
    /// <summary>
    /// <c>AWS_IAM</c> by default: the default-deny posture, and what a CloudFront origin access
    /// control signs for. An application that fronts browsers without a distribution sets
    /// <c>NONE</c> and does its own authentication.
    /// </summary>
    public FunctionUrlAuthType AuthType { get; set; } = FunctionUrlAuthType.AWS_IAM;

    public IFunctionUrlCorsOptions? Cors { get; set; }

    public Action<FunctionUrl, Alias> ConfigureUrl { get; set; } = (url, alias) => { };
}

[TransientService]
public class LambdaCdkUtil {
    private readonly StackContextAccessor _stackContextAccessor;
    private readonly IDefaultFunctionProps _defaultFunctionProps;

    public LambdaCdkUtil(
        StackContextAccessor stackContextAccessor, 
        IDefaultFunctionProps defaultFunctionProps) {
        _stackContextAccessor = stackContextAccessor;
        _defaultFunctionProps = defaultFunctionProps;
    }

    public (Function function, HttpApi api) HttpApiFunctionCreate(HttpApiLambdaRequest request) {
        // An HTTP API buffers every response and cannot stream. Refused rather than warned about: a
        // stream-mode application behind it writes a prelude the gateway does not understand, which
        // is not a degraded deployment but a broken one.
        if (request.ResponseMode == LambdaResponseMode.Stream) {
            throw new InvalidOperationException(
                $"'{request.Name}' sets ResponseMode to Stream behind an HTTP API. HTTP API buffers " +
                "every response and cannot stream. Deploy it behind a function URL with " +
                $"{nameof(FunctionUrlFunctionCreate)}, or set ResponseMode to Buffered.");
        }

        var context = _stackContextAccessor.Context;
        var (lambdaFunction, alias) = LambdaFunctionCreate(request);

        // An HTTP API integrates against the alias, not the function, so AliasName = null leaves
        // nothing to route to. Said here rather than left to a NullReferenceException inside the
        // integration construct, which names neither the request nor the setting that caused it.
        if (alias == null) {
            throw new InvalidOperationException(
                $"'{request.Name}' asks for an HTTP API but sets AliasName to null. " +
                "The API integrates against the alias, so one has to exist.");
        }

        // No CORS preflight unless the caller asks for one. This defaulted to AllowOrigins ["*"]
        // with AllowMethods ANY, so every API built through this helper was open to any origin
        // and any method unless someone thought to close it. Overridable is not the same as
        // safe-by-default, and the default is what ships when nobody is thinking about it.
        //
        // A browser-facing API sets its own through HttpApiProps, or through CorsPreflight on
        // HttpApiLambdaRequest, which names the origins rather than accepting all of them.
        var apiProps = new HttpApiProps {
            ApiName = request.Name,
            CorsPreflight = request.CorsPreflight,
        };

        request.HttpApiProps(apiProps);
        
        var apiGateway = new HttpApi(context.Stack, request.Name + "-gateway",apiProps);
        
        request.ConfigureApi(apiGateway, alias);
        
        return (lambdaFunction, apiGateway);
    }

    /// <summary>
    /// The function and a URL on its alias, with the invoke mode and the application's response
    /// mode set from the same request so the two cannot disagree.
    /// </summary>
    public (Function function, FunctionUrl url) FunctionUrlFunctionCreate(FunctionUrlLambdaRequest request) {
        var context = _stackContextAccessor.Context;
        var (lambdaFunction, alias) = LambdaFunctionCreate(request);

        if (alias == null) {
            throw new InvalidOperationException(
                $"'{request.Name}' asks for a function URL but sets AliasName to null. " +
                "The URL attaches to the alias, so one has to exist.");
        }

        var url = alias.AddFunctionUrl(new FunctionUrlOptions {
            AuthType = request.AuthType,
            Cors = request.Cors,
            InvokeMode = request.ResponseMode == LambdaResponseMode.Stream
                ? InvokeMode.RESPONSE_STREAM
                : InvokeMode.BUFFERED
        });

        context.Set(KnownCdkResources.LambdaFunctionUrl, url, request.Name);

        request.ConfigureUrl(url, alias);

        return (lambdaFunction, url);
    }

    public (Function function, Alias? alias) LambdaFunctionCreate(LambdaRequest request) {
        var context = _stackContextAccessor.Context;

        var assemblyName = request.ApplicationType.Assembly.GetName().Name
            ?? throw new InvalidOperationException(
                $"'{request.ApplicationType}' is in an assembly with no name, so it cannot be a handler.");

        var lambdaProps = new FunctionProps {
            Runtime = Runtime.DOTNET_8,
            MemorySize = 768,
            // TODO: migrate to LogGroup. CDK deprecated LogRetention in favour of passing
            // an explicit LogGroup, but the two produce different CloudFormation - the
            // former provisions a log-retention custom resource, the latter an actual
            // LogGroup resource. Switching changes the deployed stack, so it needs a
            // deliberate infrastructure decision rather than a warning cleanup.
#pragma warning disable CS0618
            LogRetention = RetentionDays.ONE_MONTH,
#pragma warning restore CS0618
            FunctionName = request.Name,
            // The executable assembly form. Every Hardened Lambda application carries a generated
            // Main that runs the AWS bootstrap, and the managed runtime starts it when the handler
            // names the assembly alone rather than a type and method.
            Handler = assemblyName,
            Code = request.Code?.Invoke() ?? Amazon.CDK.AWS.Lambda.Code.FromCustomCommand(request.DistLocation,
                [
                    $"dotnet-lambda package -pl ../{assemblyName} -o {request.DistLocation}"
                ],
                new CustomCommandOptions {
                    CommandOptions = new Dictionary<string, object> {
                        {
                            "shell", true
                        }
                    }
                })
        };
        
        _defaultFunctionProps.ApplyDefaults(lambdaProps);

        request.Props(lambdaProps);

        var lambdaFunction = new Function(context.Stack, request.Name + "-function", lambdaProps);

        // After the caller's Props, so an Environment they assigned wholesale does not drop it. The
        // application fails at startup on a value it does not recognise, and falls back to nothing.
        lambdaFunction.AddEnvironment(
            LambdaResponseModeConfiguration.EnvironmentVariable,
            LambdaResponseModeConfiguration.ValueOf(request.ResponseMode));

        context.Set(KnownCdkResources.LambdaFunction, lambdaFunction, request.Name);
        
        if (request.AliasName != null) {
            var alias = lambdaFunction.AddAlias(request.AliasName);

            context.Set(KnownCdkResources.LambdaFunctionAlias, alias, request.Name);
            
            return (lambdaFunction, alias);
        }
        
        return (lambdaFunction, null);
    }
}
