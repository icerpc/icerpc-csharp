// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;
using IceRpc.CaseConverter.Internal;

namespace IceRpc.ProtocGen;

internal class ClientGenerator
{
    internal static string GenerateInterface(ServiceDescriptor service)
    {
        string methods = "";
        string scope = service.File.GetCsharpNamespace();
        foreach (MethodDescriptor method in service.Methods)
        {
            string inputType = method.InputType.GetType(scope, method.IsClientStreaming);
            string inputParam = method.IsClientStreaming ? "stream" : "message";
            string returnType = method.OutputType.GetType(scope, method.IsServerStreaming);
            string methodName = $"{method.Name.ToPascalCase()}Async";

            if (method.GetOptions()?.Deprecated ?? false)
            {
                methods += @$"
    [global::System.Obsolete]";
            }
            methods += @$"
    global::System.Threading.Tasks.Task<{returnType}> {methodName}(
        {inputType} {inputParam},
        IceRpc.Features.IFeatureCollection? features = null,
        global::System.Threading.CancellationToken cancellationToken = default);";
            methods += "\n";
        }

        string clientInterface = @$"
/// <remarks>protoc-gen-icerpc-csharp generated this client-side interface from Protobuf service <c>{service.FullName}</c>.
/// It's implemented by <c>{service.Name.ToPascalCase()}Client</c></remarks>";
        if (service.GetOptions()?.Deprecated ?? false)
        {
            clientInterface += @"
[global::System.Obsolete]";
        }
        clientInterface += @$"
public partial interface I{service.Name.ToPascalCase()}
{{
    {methods.Trim()}
}}";
        return clientInterface;
    }

    internal static string GenerateImplementation(ServiceDescriptor service)
    {
        string methods = "";
        string scope = service.File.GetCsharpNamespace();
        foreach (MethodDescriptor method in service.Methods)
        {
            string inputType = method.InputType.GetType(scope, method.IsClientStreaming);
            string inputParam = method.IsClientStreaming ? "stream" : "message";
            string returnType = method.OutputType.GetType(scope, method.IsServerStreaming);
            string returnTypeParser = method.OutputType.GetParserType(scope);
            string methodName = $"{method.Name.ToPascalCase()}Async";

            MethodOptions? methodOptions = method.GetOptions();
            bool idempotent =
                (methodOptions?.HasIdempotencyLevel ?? false) &&
                (methodOptions.IdempotencyLevel == MethodOptions.Types.IdempotencyLevel.NoSideEffects ||
                 methodOptions.IdempotencyLevel == MethodOptions.Types.IdempotencyLevel.Idempotent);

            string invokeAsyncMethod = (method.IsClientStreaming, method.IsServerStreaming) switch
            {
                (false, false) => "InvokeUnaryAsync",
                (true, false) => "InvokeClientStreamingAsync",
                (false, true) => "InvokeServerStreamingAsync",
                (true, true) => "InvokeBidiStreamingAsync",
            };

            if (method.GetOptions()?.Deprecated ?? false)
            {
                methods += @"
    [global::System.Obsolete]";
            }
            methods += @$"
    public global::System.Threading.Tasks.Task<{returnType}> {methodName}(
        {inputType} {inputParam},
        IceRpc.Features.IFeatureCollection? features = null,
        global::System.Threading.CancellationToken cancellationToken = default) =>
        Invoker.{invokeAsyncMethod}(
            ServiceAddress,
            ""{method.Name}"",
            {inputParam},
            {returnTypeParser},
            EncodeOptions,
            features,
            idempotent: {idempotent.ToString().ToLowerInvariant()},
            cancellationToken: cancellationToken);";
            methods += "\n";
        }

        string clientImplementationName = $"{service.Name.ToPascalCase()}Client";

        string clientImplementation = @$"
/// <summary>Makes invocations on a remote IceRPC service. This remote service must implement Protobuf service
/// <c>{service.FullName}</c>.</summary>
/// <remarks>protoc-gen-icerpc-csharp generated this record struct from Protobuf service <c>{service.FullName}</c>.</remarks>";
        if (service.GetOptions()?.Deprecated ?? false)
        {
            clientImplementation += @"
[global::System.Obsolete]";
        }
        clientImplementation += @$"
public readonly partial record struct {clientImplementationName} : I{service.Name.ToPascalCase()}, IProtobufClient
{{
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
    public {clientImplementationName}(
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
    public {clientImplementationName}(
        IceRpc.IInvoker invoker,
        System.Uri serviceAddressUri,
        ProtobufEncodeOptions? encodeOptions = null)
        : this(invoker, new IceRpc.ServiceAddress(serviceAddressUri), encodeOptions)
    {{
    }}

    /// <summary>Constructs a client with an icerpc service address with path <see cref=""DefaultServicePath"" />.</summary>
    public {clientImplementationName}()
    {{
    }}

    {methods.Trim()}
}}";
        return clientImplementation;
    }
}
