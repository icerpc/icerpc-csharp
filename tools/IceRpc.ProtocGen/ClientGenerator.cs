// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;
using IceRpc.CaseConverter.Internal;

namespace IceRpc.ProtocGen;

internal class ClientGenerator
{
    internal static string GenerateInterface(ServiceDescriptor service)
    {
        string operations = "";
        string scope = service.File.GetCsharpNamespace();
        foreach (MethodDescriptor method in service.Methods)
        {
            string inputType = method.InputType.GetType(scope);
            string returnType = method.OutputType.GetType(scope);
            string methodName = $"{method.Name.ToPascalCase()}Async";

            operations += $@"
    global::System.Threading.Tasks.Task<{returnType}> {methodName}(
        {inputType} message,
        IceRpc.Features.IFeatureCollection? features = null,
        global::System.Threading.CancellationToken cancellationToken = default);";
        }
        return @$"
/// <remarks>protoc-gen-icerpc-csharp generated this client-side interface from Protobuf service <c>{service.Name}</c>.
/// It's implemented by <c>{service.Name.ToPascalCase()}Client</c></remarks>
public partial interface I{service.Name.ToPascalCase()}
{{
    {operations.Trim()}
}}";
    }

    internal static string GenerateImplementation(ServiceDescriptor service)
    {
        string operations = "";
        string scope = service.File.GetCsharpNamespace();
        foreach (MethodDescriptor method in service.Methods)
        {
            string inputType = method.InputType.GetType(scope);
            string returnType = method.OutputType.GetType(scope);
            string methodName = $"{method.Name.ToPascalCase()}Async";

            MethodOptions? methodOptions = method.GetOptions();
            bool idempotent =
                (methodOptions?.HasIdempotencyLevel ?? false) &&
                (methodOptions.IdempotencyLevel == MethodOptions.Types.IdempotencyLevel.NoSideEffects ||
                 methodOptions.IdempotencyLevel == MethodOptions.Types.IdempotencyLevel.Idempotent);

            operations += $@"
    public global::System.Threading.Tasks.Task<{returnType}> {methodName}(
        {inputType} message,
        IceRpc.Features.IFeatureCollection? features = null,
        global::System.Threading.CancellationToken cancellationToken = default) =>
        InvokerExtensions.InvokeAsync(
            Invoker,
            ServiceAddress,
            ""{method.Name}"",
            message.ToPipeReader(),
            payloadContinuation: null,
            async (response, request, cancellationToken) =>
            {{
                if (response.StatusCode == IceRpc.StatusCode.Ok)
                {{
                    var returnValue = new {returnType}();
                    await returnValue.MergeFromAsync(response.Payload).ConfigureAwait(false);
                    return returnValue;
                }}
                else
                {{
                    // IceRPC guarantees the error message is non-null when StatusCode > Ok.
                    throw new IceRpc.DispatchException(response.StatusCode, response.ErrorMessage!);
                }}
            }},
            features,
            idempotent: {idempotent.ToString().ToLowerInvariant()},
            cancellationToken: cancellationToken);";
        }

        string clientImplementationName = $"{service.Name.ToPascalCase()}Client";

        return @$"
/// <summary>Makes invocations on a remote IceRPC service. This remote service must implement Protobuf service
/// <c>{service.Name}</c>.</summary>
/// <remarks>protoc-gen-icerpc-csharp generated this record struct from Protobuf service <c>{service.Name}</c>.</remarks>
public readonly partial record struct {clientImplementationName} : I{service.Name.ToPascalCase()}
{{
    /// <summary>Gets the default service address for services that implement Protobuf service {service.FullName}:
    /// <c>icerpc:/{service.Name}</c>.</summary>
    public static IceRpc.ServiceAddress DefaultServiceAddress {{ get; }} =
        new(IceRpc.Protocol.IceRpc) {{ Path = ""/{service.Name}"" }};

    /// <summary>Gets or initializes the invoker of this client.</summary>
    public IceRpc.IInvoker Invoker {{ get; init; }}

    /// <summary>Gets or initializes the address of the remote service.</summary>
    IceRpc.ServiceAddress ServiceAddress {{ get; init; }}

    /// <summary>Constructs a client from an invoker and a service address.</summary>
    /// <param name=""invoker"">The invoker of this client.</param>
    /// <param name=""serviceAddress"">The service address. <see langword=""null"" /> is equivalent to
    /// <see cref=""DefaultServiceAddress"" />.</param>
    public {clientImplementationName}(
        IceRpc.IInvoker invoker,
        IceRpc.ServiceAddress? serviceAddress = null)
    {{
        Invoker = invoker;
        ServiceAddress = serviceAddress ?? DefaultServiceAddress;
    }}

    /// <summary>Constructs a client from an invoker and a service address URI.</summary>
    /// <param name=""invoker"">The invocation pipeline of the proxy.</param>
    /// <param name=""serviceAddressUri"">A URI that represents a service address.</param>
    public {clientImplementationName}(IceRpc.IInvoker invoker, System.Uri serviceAddressUri)
        : this(invoker, new IceRpc.ServiceAddress(serviceAddressUri))
    {{
    }}

    {operations.Trim()}
}}";
    }
}
