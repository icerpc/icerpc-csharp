// Copyright (c) ZeroC, Inc.

namespace IceRpc.Slice.Generators.Internal;

internal class Emitter
{
    private const string IndentSpaces = "    ";

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
                dispatchImplementation = "";
                foreach (ServiceMethod serviceMethod in serviceClass.ServiceMethods)
                {
                    string operationName = serviceMethod.OperationName;
                    string dispatchMethodName = serviceMethod.DispatchMethodName;
                    string indent = IndentSpaces;
                    dispatchImplementation += $"case \"{operationName}\"" + ":\n";
                    dispatchImplementation += $"{indent}// EncodedReturn = {serviceMethod.EncodedReturn}\n";
                    if (serviceMethod.ExceptionSpecification.Length > 0)
                    {
                        string exceptions = string.Join(
                            ", ",
                            serviceMethod.ExceptionSpecification.Select(e => $"{e}"));
                        dispatchImplementation += $"{indent}// Exception specification = {exceptions}\n";
                    }

                    if (!serviceMethod.Idempotent)
                    {
                        dispatchImplementation += $"{indent}IceRpc.Slice.IncomingRequestExtensions.CheckNonIdempotent(request);\n";
                    }
                    if (serviceMethod.CompressReturnValue)
                    {
                        dispatchImplementation +=
                            @$"{indent}request.Features = IceRpc.Features.FeatureCollectionExtensions.With(
    {indent}request.Features,
    {indent}IceRpc.Features.CompressFeature.Compress);
";
                    }

                    /*
                    This does not work because we need to await the call to handle the exceptions.
                    if (serviceMethod.ExceptionSpecification.Length > 0)
                    {
                        dispatchImplementation += $"{indent}try\n";
                        dispatchImplementation += $"{indent}{{\n";
                        indent += IndentSpaces;
                    }
                    */

                    dispatchImplementation +=
                        $"{indent}return global::{dispatchMethodName}(this, request, cancellationToken);\n";

                    /*
                    if (serviceMethod.ExceptionSpecification.Length > 0)
                    {
                        indent = IndentSpaces; // TODO: this is not good
                        dispatchImplementation += $"{indent}}}\n";
                        if (serviceMethod.ExceptionSpecification.Length == 1)
                        {
                            dispatchImplementation +=
                                $"{indent}catch (global::{serviceMethod.ExceptionSpecification[0]} sliceException)";
                        }
                        else
                        {
                            string exceptionList = string.Join(
                                " or ",
                                serviceMethod.ExceptionSpecification.Select(e => $"global::{e}"));

                            dispatchImplementation +=
                                $"{indent}catch (SliceException sliceException) when (sliceException is {exceptionList})";
                        }
                        dispatchImplementation += $@"{indent}
    {{
    {indent}return IceRpc.Slice.IncomingRequestExtensions.CreateSliceExceptionResponse(request,sliceException, SliceEncoding.Slice1);
    }}
";
                    }
                    */
                }

                dispatchImplementation += "default:\n";
                if (serviceClass.HasBaseServiceClass)
                {
                    dispatchImplementation += $"{IndentSpaces}return base.DispatchAsync(request, cancellationToken);\n";
                }
                else
                {
                    dispatchImplementation +=
                        $"{IndentSpaces}return new(new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented));\n";
                }

                dispatchImplementation = @$"
switch (request.Operation)
{{
    {dispatchImplementation.WithIndent(IndentSpaces)}
}};".Trim();
            }
            else
            {
                dispatchImplementation = serviceClass.HasBaseServiceClass ?
                    "return base.DispatchAsync(request, cancellationToken);" :
                    "return new(new IceRpc.OutgoingResponse(request, IceRpc.StatusCode.NotImplemented));";
            }

            string dispatcherClass = $@"
/// <summary>Implements <see cref=""IceRpc.IDispatcher"" /> for the Slice interface(s) implemented by this class.
/// </summary>
partial {serviceClass.Keyword} {serviceClass.Name} : IceRpc.IDispatcher
{{
    /// <summary>Dispatches an incoming request to a method of {serviceClass.Name} based on the operation name carried
    /// by the request.</summary>
    /// <param name=""request"">The incoming request.</param>
    /// <param name=""cancellationToken"">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The outgoing response.</returns>
    /// <exception cref=""IceRpc.DispatchException"">Thrown if the operation name carried by the request does not
    /// correspond to any method implemented by this class. The exception status code is
    /// <see cref=""IceRpc.StatusCode.NotImplemented"" /> in this case.</exception>
    {methodModifier} global::System.Threading.Tasks.ValueTask<IceRpc.OutgoingResponse> DispatchAsync(
        IceRpc.IncomingRequest request,
        global::System.Threading.CancellationToken cancellationToken)
    {{
        {dispatchImplementation.WithIndent("        ")}
    }}
}}";

            string container = dispatcherClass;
            ContainerDefinition? containerDefinition = serviceClass;
            while (containerDefinition.Enclosing is ContainerDefinition enclosing)
            {
                container = GenerateContainer($"partial {enclosing.Keyword} {enclosing.Name}", container);
                containerDefinition = enclosing;
            }

            if (serviceClass.ContainingNamespace is not null)
            {
                container = GenerateContainer($"namespace {serviceClass.ContainingNamespace}", container);
            }
            generatedClasses.Add(container);
        }

        string generated = @$"
// <auto-generated/>
// IceRpc.Slice.Generators version: {typeof(ServiceGenerator).Assembly.GetName().Version!.ToString(3)}
#nullable enable

#pragma warning disable CS0612 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
#pragma warning disable CS0619 // Type or member is obsolete

using ZeroC.Slice;

";
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
