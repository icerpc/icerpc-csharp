// Copyright (c) ZeroC, Inc.

using Google.Protobuf.Reflection;
using IceRpc.CaseConverter.Internal;
using ZeroC.CodeBuilder;

namespace IceRpc.ProtocGen;

internal class ServiceGenerator
{
    internal static CodeBlock GenerateInterface(ServiceDescriptor service)
    {
        var methods = new CodeBlock();
        foreach (MethodDescriptor method in service.Methods)
        {
            methods.AddBlock(GenerateServiceMethod(method, service.File.GetCsharpNamespace()));
        }
        methods = methods.Indent();

        return $@"
/// <summary>Represents a template that you use to implement Protobuf service <c>{service.FullName}</c>
/// with IceRPC.</summary>
/// <remarks>protoc-gen-icerpc-csharp generated this server-side interface.</remarks>
/// <seealso cref=""IceRpc.Protobuf.ProtobufServiceAttribute"" />
[IceRpc.DefaultServicePath(""/{service.FullName}"")]
public partial interface I{service.Name.ToPascalCase()}Service
{{
    {methods}
}}";
    }

    private static CodeBlock GenerateServiceMethod(MethodDescriptor method, string scope)
    {
        string returnType = method.OutputType.GetType(scope, method.IsServerStreaming);

        FunctionBuilder functionBuilder = new FunctionBuilder(
            access: "", // abstract interface methods are implicitly public
            $"global::System.Threading.Tasks.ValueTask<{returnType}>",
            $"{method.Name.ToPascalCase()}Async",
            FunctionType.Declaration)
            .AddComment("summary", $"Implements rpc method <c>{method.Name}</c>.");

        if (method.IsClientStreaming)
        {
            functionBuilder.AddParameter(
                method.InputType.GetType(scope, method.IsClientStreaming),
                "stream",
                docComment: "The stream of input messages.");
        }
        else
        {
            functionBuilder.AddParameter(
                method.InputType.GetType(scope, method.IsClientStreaming),
                "message",
                docComment: "The input message.");
        }

        functionBuilder
            .AddParameter(
                "IceRpc.Features.IFeatureCollection",
                "features",
                docComment: "The dispatch features.")
            .AddParameter(
                "global::System.Threading.CancellationToken",
                "cancellationToken",
                docComment: "A cancellation token that receives the cancellation requests.")
            .AddComment(
                "returns",
                method.IsServerStreaming ?
                    "A value task holding the stream of output messages." : "A value task holding the output message.")
            .AddAttribute($@"ProtobufServiceMethod(""{method.Name}"")");

        return functionBuilder.Build();
    }
}
