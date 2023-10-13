// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generators.Internal;

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

            string dispatcherBlock;
            if (serviceClass.ServiceMethods.Count > 0)
            {
                var dispatchBlocks = new List<string>();
                foreach (ServiceMethod serviceMethod in serviceClass.ServiceMethods)
                {
                    dispatchBlocks.Add(@$"
case ""{serviceMethod.OperationName}"":
    return global::{serviceMethod.DispatchMethodName}(this, request, cancellationToken);".Trim());
                }

                if (serviceClass.HasBaseServiceClass)
                {
                    dispatchBlocks.Add(@$"
default:
    return base.DispatchAsync(request, cancellationToken);".Trim());
                }
                else
                {
                    dispatchBlocks.Add(@$"
default:
    return new(new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented));".Trim());
                }

                dispatcherBlock = @$"
switch (request.Operation)
{{
    {string.Join("\n\n", dispatchBlocks).Trim().WithIndent("    ")}
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
                container = GenerateContainer($"partial {parent.Keyword} {parent.Name}", container);
                containerDefinition = parent;
            }

            generatedClasses.Add(GenerateContainer($"namespace {serviceClass.ContainingNamespace}", container));
        }
        string generated = string.Join("\n\n", generatedClasses).Trim();
        generated += "\n";
        return generated;
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
