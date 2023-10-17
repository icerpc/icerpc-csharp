// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;
using IceRpc.CaseConverter.Internal;

namespace IceRpc.ProtocGen;

internal class ClientGenerator
{
    internal static string GenerateInterface(ServiceDescriptor service)
    {
        string operations = "";
        foreach (MethodDescriptor method in service.Methods)
        {
            string inputType = method.InputType.GetFullyQualifiedType();
            string returnType = method.OutputType.GetFullyQualifiedType();
            string methodName = $"{method.Name.ToPascalCase()}Async";

            operations += $@"
    global::System.Threading.Tasks.Task<{returnType}> {methodName}(
        {inputType} message,
        IceRpc.Features.IFeatureCollection? features = null,
        global::System.Threading.CancellationToken cancellationToken = default);";
        }
        return @$"
public partial interface I{service.Name.ToPascalCase()}
{{
    {operations.Trim()}
}}";
    }

    internal static string GenerateImplementation(ServiceDescriptor service)
    {
        string operations = "";
        foreach (MethodDescriptor method in service.Methods)
        {
            string inputType = method.InputType.GetFullyQualifiedType();
            string returnType = method.OutputType.GetFullyQualifiedType();
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
        IceRpc.Protobuf.InvokerExtensions.InvokeAsync(
            Invoker,
            ServiceAddress,
            ""{method.Name}"",
            IceRpc.Protobuf.MessageExtensions.ToPipeReader(message),
            payloadContinuation: null,
            async (response, request, cancellationToken) =>
            {{
                if (response.StatusCode == IceRpc.StatusCode.Ok)
                {{
                    var returnValue = new {returnType}();
                    await IceRpc.Protobuf.MessageExtensions.MergeFromAsync(returnValue, response.Payload).ConfigureAwait(false);
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
/// <remarks>The IceRpc.ProtocGen protoc plugin generated this client struct from the Protobuf service <c>{service.Name}</c>.</remarks>
public partial struct {clientImplementationName} : I{service.Name.ToPascalCase()}
{{
    /// <summary>Gets the default service address for services that implement Protobuf service {service.FullName}.
    /// Its protocol is <see cref=""IceRpc.Protocol.IceRpc"" /> and its path is computed from the name of the Protobuf service.
    /// </summary>
    public static IceRpc.ServiceAddress DefaultServiceAddress {{ get; }} =
        new(IceRpc.Protocol.IceRpc) {{ Path = ""/{service.Name}"" }};

    /// <summary>Gets or initializes the invocation pipeline of this client.</summary>
    public IceRpc.IInvoker Invoker {{ get; init; }}

    /// <summary>Gets or initializes the address of the remote service.</summary>
    IceRpc.ServiceAddress ServiceAddress {{ get; init; }}

    /// <summary>Constructs a client from an invoker and a service address.</summary>
    /// <param name=""invoker"">The invocation pipeline of the proxy.</param>
    /// <param name=""serviceAddress"">The service address. <see langword=""null"" /> is equivalent to <see cref=""DefaultServiceAddress"" />.</param>
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
