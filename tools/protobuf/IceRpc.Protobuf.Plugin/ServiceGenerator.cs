// Copyright (c) ZeroC, Inc.

using CaseConverter;
using Google.Protobuf.Reflection;

namespace IceRpc.Protoc;

internal class ServiceGenerator
{
    public static string GenerateInterface(ServiceDescriptor service)
    {
        string methods = "";
        foreach (MethodDescriptor method in service.Methods)
        {
            string inputType = method.InputType.GetFullyQualifiedType();
            string returnType = method.OutputType.GetFullyQualifiedType();
            string methodName = $"{method.Name.ToPascalCase()}Async";

            // Add an abstract method to the interface for each service method.
            methods += $@"
    global::System.Threading.Tasks.ValueTask<{returnType}> {methodName}(
        {inputType} message,
        IceRpc.Features.IFeatureCollection? features,
        global::System.Threading.CancellationToken cancellationToken);
";

            // Add a static method for each service method, the implementation calls the
            // abstract method and creates the outgoing response.
            methods += $@"
    [ProtobufOperation(""{method.Name}"")]
    [global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
    protected static async global::System.Threading.Tasks.ValueTask<IceRpc.OutgoingResponse> {methodName}(
        I{service.Name.ToPascalCase()}Service target,
        IceRpc.IncomingRequest request,
        global::System.Threading.CancellationToken cancellationToken)
    {{
        var inputParam = new {inputType}();
        await IceRpc.Protobuf.MessageExtensions.MergeFromAsync(inputParam, request.Payload).ConfigureAwait(false);
        var returnParam = await target.{methodName}(inputParam, request.Features, cancellationToken).ConfigureAwait(false);
        return new IceRpc.OutgoingResponse(request)
        {{
            Payload = IceRpc.Protobuf.MessageExtensions.ToPipeReader(returnParam),
            PayloadContinuation = null
        }};
    }}
";
        }
        return @$"
public partial interface I{service.Name.ToPascalCase()}Service
{{
    {methods.Trim()}
}}";
    }
}
