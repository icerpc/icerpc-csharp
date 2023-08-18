// Copyright (c) ZeroC, Inc.

using System.Runtime.InteropServices;

namespace IceRpc.Slice.Generators.Internal;

internal class Emitter
{
    private static readonly string _newLine = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\r\n" : "\n";

    internal string Emit(IReadOnlyList<ServiceDefinition> serviceClasses, CancellationToken cancellationToken)
    {
        string generated = "";
        foreach (ServiceDefinition serviceClass in serviceClasses)
        {
            // stop if we're asked to.
            cancellationToken.ThrowIfCancellationRequested();

            string methodModifier =
                serviceClass.HasBaseServiceDefinition ? "public override" :
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

                if (serviceClass.HasBaseServiceDefinition)
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
                dispatcherBlock = serviceClass.HasBaseServiceDefinition ?
                    "base.DispatchAsync(request, cancellationToken);" :
                    "new(new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented));";
            }

            string typeIds = "";
            foreach (string typeId in serviceClass.TypeIds)
            {
                typeIds += @$"""{typeId}"",{_newLine}";
            }
            typeIds = typeIds.TrimEnd(',', '\n', '\r', ' ');

            string dispatcherClass = $@"
partial {serviceClass.Keyword} {serviceClass.Name} : IceRpc.IDispatcher, IceRpc.Slice.Ice.IIceObjectService
{{
    private static readonly global::System.Collections.Generic.IReadOnlySet<string> _typeIds =
        new global::System.Collections.Generic.SortedSet<string>()
        {{
            {typeIds.WithIndent("            ")}
        }};

    {methodModifier} global::System.Threading.Tasks.ValueTask<IceRpc.OutgoingResponse> DispatchAsync(
        IceRpc.IncomingRequest request,
        global::System.Threading.CancellationToken cancellationToken)
    {{
        {dispatcherBlock.WithIndent("        ")}
    }}

    /// <inheritdoc/>
    {methodModifier} global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<string>> IceIdsAsync(
        IceRpc.Features.IFeatureCollection features,
        global::System.Threading.CancellationToken cancellationToken) =>
        new(_typeIds);

    /// <inheritdoc/>
    {methodModifier} global::System.Threading.Tasks.ValueTask<bool> IceIsAAsync(
        string id,
        IceRpc.Features.IFeatureCollection features,
        global::System.Threading.CancellationToken cancellationToken) =>
        new(_typeIds.Contains(id));

    /// <inheritdoc/>
    {methodModifier} global::System.Threading.Tasks.ValueTask IcePingAsync(
        IceRpc.Features.IFeatureCollection features,
        global::System.Threading.CancellationToken cancellationToken) => default;
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
