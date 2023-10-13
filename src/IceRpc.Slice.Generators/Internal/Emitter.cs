// Copyright (c) ZeroC, Inc.

using System.Runtime.InteropServices;

namespace IceRpc.Slice.Generators.Internal;

internal class Emitter
{
    private static readonly string _newLine = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\r\n" : "\n";

    internal string Emit(IReadOnlyList<ServiceClass> serviceClasses, CancellationToken cancellationToken)
    {
        string generated = "";
        foreach (ServiceClass serviceClass in serviceClasses)
        {
            // stop if we're asked to.
            cancellationToken.ThrowIfCancellationRequested();

            string methodModifier =
                serviceClass.HasBaseServiceClass ? "public override" :
                serviceClass.IsSealed ? "public" : "public virtual";

            string dispatcherBlock;
            if (serviceClass.ServiceMethods.Count > 0)
            {
                string dispatchBlocks = "";
                foreach (ServiceMethod serviceMethod in serviceClass.ServiceMethods)
                {
                    dispatchBlocks += @$"

case ""{serviceMethod.OperationName}"":
    return {serviceMethod.DispatchMethodName}(this, request, cancellationToken);";
                }

                if (serviceClass.HasBaseServiceClass)
                {
                    dispatchBlocks += @$"

default:
    return base.DispatchAsync(request, cancellationToken);";
                }
                else
                {
                    dispatchBlocks += @$"

default:
    return new(new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented));";
                }
                dispatchBlocks = dispatchBlocks.Trim();

                dispatcherBlock = @$"
switch (request.Operation)
{{
    {dispatchBlocks.WithIndent("    ")}
}}".Trim();
            }
            else
            {
                dispatcherBlock = serviceClass.HasBaseServiceClass ?
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
        {dispatcherBlock.WithIndent("        ")}
    }}
}}";

            string container = dispatcherClass;
            ContainerDefinition? containerDefinition = serviceClass;
            while (containerDefinition.Parent is ContainerDefinition parent)
            {
                container = WriteContainer($"partial {parent.Keyword} {parent.Name}", container);
                containerDefinition = parent;
            }

            generated += WriteContainer($"namespace {serviceClass.ContainingNamespace}", container);
            generated += $"{_newLine}{_newLine}";
        }
        return generated.TrimTrailingWhiteSpaces();
    }

    private static string WriteContainer(string header, string body)
    {
        return $@"
{header}
{{
    {body.WithIndent("    ")}
}}".Trim();
    }
}
