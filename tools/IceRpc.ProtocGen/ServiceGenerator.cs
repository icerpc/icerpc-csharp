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
    /// <summary>Implements rpc method <c>{method.Name}</c>.</summary>
    /// <param name=""message"">The input message.</param>
    /// <param name=""features"">The dispatch features.</param>
    /// <param name=""cancellationToken"">A cancellation token that receives the cancellation requests.</param>
    /// <returns>A value task holding the output message.</returns>
    [ProtobufOperation(""{method.Name}"")]
    global::System.Threading.Tasks.ValueTask<{returnType}> {methodName}(
        {inputType} message,
        IceRpc.Features.IFeatureCollection features,
        global::System.Threading.CancellationToken cancellationToken);";
        }

        return @$"
/// <summary>Represents a template that you use to implement Protobuf service <c>{service.FullName}</c>
/// with IceRPC.</summary>
/// <remarks>protoc-gen-icerpc-csharp generated this server-side interface.</remarks>
/// <seealso cref=""IceRpc.Protobuf.ProtobufServiceAttribute"" />
[IceRpc.DefaultServicePath(""/{service.FullName}"")]
public partial interface I{service.Name.ToPascalCase()}Service
{{
    {methods.Trim()}
}}";
    }
}
