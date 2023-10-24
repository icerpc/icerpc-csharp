// Copyright (c) ZeroC, Inc.

namespace IceRpc.Protobuf.Generators.Internal;

internal class Emitter
{
    internal string Emit(IReadOnlyList<ServiceClass> serviceClasses, CancellationToken cancellationToken)
    {
        var generatedClasses = new List<string>();
        foreach (ServiceClass serviceClass in serviceClasses)
        {
            // stop if we're asked to.
            cancellationToken.ThrowIfCancellationRequested();

            string methodModifier =
                serviceClass.HasBaseServiceClass ? "public override" :
                serviceClass.IsSealed ? "public" : "public virtual";

            string dispatchImplementation;
            if (serviceClass.ServiceMethods.Count > 0)
            {
                methodModifier += " async";

                dispatchImplementation = "";
                foreach (ServiceMethod serviceMethod in serviceClass.ServiceMethods)
                {
                    string inputParamDecodeCode = serviceMethod.IsClientStreaming ?
                        @$"
var payload = request.DetachPayload();
var inputParam = payload.ToAsyncEnumerable(
    {serviceMethod.InputTypeName}.Parser,
    protobufFeature.MaxMessageLength,
    cancellationToken)".Trim() :
                        @$"
var inputParam = await request.Payload.DecodeProtobufMessageAsync(
    {serviceMethod.InputTypeName}.Parser,
    protobufFeature.MaxMessageLength,
    cancellationToken).ConfigureAwait(false)".Trim();

                    string outputParamEncode = serviceMethod.IsServerStreaming ?
                        $@"
PayloadContinuation = outputParam.ToPipeReader(
    protobufFeature.EncodeOptions)".Trim() :
                        $@"
Payload = outputParam.EncodeAsLengthPrefixedMessage(
    protobufFeature.EncodeOptions?.PipeOptions ?? ProtobufEncodeOptions.Default.PipeOptions)";

                    dispatchImplementation += @$"
case ""{serviceMethod.OperationName}"":
{{
    var protobufFeature = request.Features.Get<IProtobufFeature>() ?? ProtobufFeature.Default;
    {inputParamDecodeCode};
    var outputParam = await (({serviceMethod.InterfaceName})this).{serviceMethod.MethodName}(
        inputParam,
        request.Features,
        cancellationToken).ConfigureAwait(false);
    return new IceRpc.OutgoingResponse(request)
    {{
        {outputParamEncode.WithIndent("        ").Trim()}
    }};
}}".Trim();
                    dispatchImplementation += "\n\n";
                }
                if (serviceClass.HasBaseServiceClass)
                {
                    dispatchImplementation += @$"
default:
    return await base.DispatchAsync(request, cancellationToken).ConfigureAwait(false);".Trim();
                }
                else
                {
                    dispatchImplementation += @$"
default:
    return new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented);".Trim();
                }
                dispatchImplementation = @$"
switch (request.Operation)
{{
    {dispatchImplementation.WithIndent("    ")}
}};".Trim();
            }
            else
            {
                dispatchImplementation = serviceClass.HasBaseServiceClass ?
                    "return base.DispatchAsync(request, cancellationToken);" :
                    "return new(new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented));";
            }

            string dispatcherClass = $@"
partial {serviceClass.Keyword} {serviceClass.Name} : IceRpc.IDispatcher
{{
    {methodModifier} global::System.Threading.Tasks.ValueTask<IceRpc.OutgoingResponse> DispatchAsync(
        IceRpc.IncomingRequest request,
        global::System.Threading.CancellationToken cancellationToken)
    {{
        {dispatchImplementation.WithIndent("        ")}
    }}
}}";

            string container = dispatcherClass;
            ContainerDefinition? containerDefinition = serviceClass;
            while (containerDefinition.Enclosing is ContainerDefinition parent)
            {
                container = GenerateContainer($"partial {parent.Keyword} {parent.Name}", container);
                containerDefinition = parent;
            }

            if (serviceClass.ContainingNamespace is not null)
            {
                container = GenerateContainer($"namespace {serviceClass.ContainingNamespace}", container);
            }
            generatedClasses.Add(container);
        }

        string generated = "using IceRpc.Protobuf;\n\n";
        generated += string.Join("\n\n", generatedClasses).Trim();
        generated += "\n";
        return generated.ReplaceLineEndings();
    }

    private static string GenerateContainer(string header, string body)
    {
        return $@"
{header}
{{
    {body.WithIndent("    ")}
}}".Trim();
    }
}
