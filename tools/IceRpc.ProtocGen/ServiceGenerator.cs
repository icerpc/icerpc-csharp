// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;
using IceRpc.CaseConverter.Internal;

namespace IceRpc.ProtocGen;

internal class ServiceGenerator
{
    public static string GenerateInterface(ServiceDescriptor service)
    {
        string methods = "";
        string scope = service.File.GetCsharpNamespace();
        foreach (MethodDescriptor method in service.Methods)
        {
            string inputType = method.InputType.GetType(scope);
            string returnType = method.OutputType.GetType(scope);
            string methodName = $"{method.Name.ToPascalCase()}Async";

            // Add an abstract method to the interface for each service method.
            methods += $@"
    global::System.Threading.Tasks.ValueTask<{returnType}> {methodName}(
        {inputType} message,
        IceRpc.Features.IFeatureCollection features,
        global::System.Threading.CancellationToken cancellationToken);";

            // Add a static method for each service method, the implementation calls the abstract method and creates
            // the outgoing response.
            methods += $@"
    [ProtobufOperation(""{method.Name}"")]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    protected static async global::System.Threading.Tasks.ValueTask<IceRpc.OutgoingResponse> ProtobufD{methodName}(
        I{service.Name.ToPascalCase()}Service target,
        IceRpc.IncomingRequest request,
        global::System.Threading.CancellationToken cancellationToken)
    {{
        var inputParam = new {inputType}();
        await inputParam.MergeFromAsync(request.Payload).ConfigureAwait(false);
        var returnParam = await target.{methodName}(inputParam, request.Features, cancellationToken).ConfigureAwait(false);
        return new IceRpc.OutgoingResponse(request)
        {{
            Payload = returnParam.ToPipeReader()
        }};
    }}";
        }

        return @$"
/// <remarks>protoc-gen-icerpc-csharp generated this server-side interface from Protobuf service <c>{service.FullName}</c>.
/// </remarks>
[IceRpc.DefaultServicePath(""/{service.FullName}"")]
public partial interface I{service.Name.ToPascalCase()}Service
{{
    {methods.Trim()}
}}";
    }
}
