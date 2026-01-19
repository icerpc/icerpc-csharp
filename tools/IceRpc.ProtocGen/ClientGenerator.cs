// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;
using IceRpc.CaseConverter.Internal;
using ZeroC.CodeBuilder;

namespace IceRpc.ProtocGen;

internal class ClientGenerator
{
    internal static CodeBlock GenerateClient(ServiceDescriptor service)
    {
        string clientName = $"{service.Name.ToPascalCase()}Client";

        ContainerBuilder clientBuilder =
            new ContainerBuilder(
                "public readonly partial record struct",
                $"{clientName} : I{service.Name.ToPascalCase()}, IProtobufClient")
            .AddComment(
                "summary",
                @$"Makes invocations on a remote IceRPC service. This remote service must implement Protobuf service
<c>{service.FullName}</c>.")
            .AddComment(
                "remarks",
                $"protoc-gen-icerpc-csharp generated this record struct from Protobuf service <c>{service.FullName}</c>.")
            .AddObsoleteAttribute(condition: service.GetOptions()?.Deprecated ?? false);

        var body = new CodeBlock($@"
/// <summary>Represents the default path for IceRPC services that implement Protobuf service
/// <c>{service.FullName}</c>.</summary>
public const string DefaultServicePath = ""/{service.FullName}"";

/// <summary>Gets or initializes the encode options, used to customize the encoding of payloads created from this
/// client.</summary>
public ProtobufEncodeOptions? EncodeOptions {{ get; init; }}

/// <summary>Gets or initializes the invoker of this client.</summary>
public required IceRpc.IInvoker Invoker {{ get; init; }}

/// <summary>Gets or initializes the address of the remote service.</summary>
public IceRpc.ServiceAddress ServiceAddress {{ get; init; }} = _defaultServiceAddress;

private static IceRpc.ServiceAddress _defaultServiceAddress =
    new(IceRpc.Protocol.IceRpc) {{ Path = DefaultServicePath }};

/// <summary>Constructs a client from an invoker and a service address.</summary>
/// <param name=""invoker"">The invoker of this client.</param>
/// <param name=""serviceAddress"">The service address. <see langword=""null"" /> is equivalent to an icerpc service
/// address with path <see cref=""DefaultServicePath"" />.</param>
/// <param name=""encodeOptions"">The encode options, used to customize the encoding of request payloads.</param>
[System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
public {clientName}(
    IceRpc.IInvoker invoker,
    IceRpc.ServiceAddress? serviceAddress = null,
    ProtobufEncodeOptions? encodeOptions = null)
{{
    Invoker = invoker;
    ServiceAddress = serviceAddress ?? _defaultServiceAddress;
    EncodeOptions = encodeOptions;
}}

/// <summary>Constructs a client from an invoker and a service address URI.</summary>
/// <param name=""invoker"">The invocation pipeline of the proxy.</param>
/// <param name=""serviceAddressUri"">A URI that represents a service address.</param>
/// <param name=""encodeOptions"">The encode options, used to customize the encoding of request payloads.</param>
[System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
public {clientName}(
    IceRpc.IInvoker invoker,
    System.Uri serviceAddressUri,
    ProtobufEncodeOptions? encodeOptions = null)
    : this(invoker, new IceRpc.ServiceAddress(serviceAddressUri), encodeOptions)
{{
}}

/// <summary>Constructs a client with an icerpc service address with path <see cref=""DefaultServicePath"" />.</summary>
public {clientName}()
{{
}}");

        clientBuilder.AddBlock(body);

        foreach (MethodDescriptor method in service.Methods)
        {
            clientBuilder.AddBlock(GenerateClientMethod(method, service.File.GetCsharpNamespace()));
        }

        return clientBuilder.Build();
    }

    internal static CodeBlock GenerateInterface(ServiceDescriptor service)
    {
        ContainerBuilder interfaceBuilder =
            new ContainerBuilder(
                "public partial interface",
                $"I{service.Name.ToPascalCase()}")
            .AddComment(
                "remarks",
                @$"protoc-gen-icerpc-csharp generated this client-side interface from Protobuf service <c>{service.FullName}</c>.
It's implemented by <c>{service.Name.ToPascalCase()}Client</c>.")
        .AddObsoleteAttribute(condition: service.GetOptions()?.Deprecated ?? false);

        foreach (MethodDescriptor method in service.Methods)
        {
            interfaceBuilder.AddBlock(GenerateInterfaceMethod(method, service.File.GetCsharpNamespace()));
        }

        return interfaceBuilder.Build();
    }

    private static CodeBlock GenerateClientMethod(MethodDescriptor method, string scope)
    {
        string returnType = method.OutputType.GetType(scope, method.IsServerStreaming);

        FunctionBuilder functionBuilder =
            new FunctionBuilder(
                access: "public",
                $"global::System.Threading.Tasks.Task<{returnType}>",
                $"{method.Name.ToPascalCase()}Async",
                FunctionType.ExpressionBody)
            .AddParameter(
                method.InputType.GetType(scope, method.IsClientStreaming),
                method.IsClientStreaming ? "stream" : "message")
            .AddParameter("IceRpc.Features.IFeatureCollection?", "features", "null")
            .AddParameter("global::System.Threading.CancellationToken", "cancellationToken", "default")
            .AddObsoleteAttribute(condition: method.GetOptions()?.Deprecated ?? false);

        string invokeAsyncMethod = (method.IsClientStreaming, method.IsServerStreaming) switch
        {
            (false, false) => "InvokeUnaryAsync",
            (true, false) => "InvokeClientStreamingAsync",
            (false, true) => "InvokeServerStreamingAsync",
            (true, true) => "InvokeBidiStreamingAsync",
        };

        MethodOptions? methodOptions = method.GetOptions();
        bool idempotent =
            (methodOptions?.HasIdempotencyLevel ?? false) &&
            (methodOptions.IdempotencyLevel == MethodOptions.Types.IdempotencyLevel.NoSideEffects ||
             methodOptions.IdempotencyLevel == MethodOptions.Types.IdempotencyLevel.Idempotent);

        FunctionCallBuilder functionCallBuilder =
            new FunctionCallBuilder($"Invoker.{invokeAsyncMethod}")
                .ArgumentsOnNewline()
                .AddArgument("ServiceAddress")
                .AddArgument($@"""{method.Name}""")
                .AddArgument(method.IsClientStreaming ? "stream" : "message")
                .AddArgument(method.OutputType.GetParserType(scope))
                .AddArgument("EncodeOptions")
                .AddArgument("features")
                .AddArgument($"idempotent: {idempotent.ToString().ToLowerInvariant()}")
                .AddArgument("cancellationToken: cancellationToken")
                .UseSemicolon(false);

        return functionBuilder.SetBody(functionCallBuilder.Build().Indent()).Build();
    }

    private static CodeBlock GenerateInterfaceMethod(MethodDescriptor method, string scope)
    {
        string returnType = method.OutputType.GetType(scope, method.IsServerStreaming);

        return
            new FunctionBuilder(
                access: "",
                $"global::System.Threading.Tasks.Task<{returnType}>",
                $"{method.Name.ToPascalCase()}Async",
                FunctionType.Declaration)
            .AddParameter(
                method.InputType.GetType(scope, method.IsClientStreaming),
                method.IsClientStreaming ? "stream" : "message")
            .AddParameter("IceRpc.Features.IFeatureCollection?", "features", "null")
            .AddParameter("global::System.Threading.CancellationToken", "cancellationToken", "default")
            .AddObsoleteAttribute(condition: method.GetOptions()?.Deprecated ?? false)
            .Build();
    }
}
