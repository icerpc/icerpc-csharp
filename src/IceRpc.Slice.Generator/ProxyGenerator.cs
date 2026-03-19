// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Symbols;

using static ZeroC.Slice.Generator.OperationHelpers;

namespace ZeroC.Slice.Generator;

/// <summary>Generates C# client-side code from a Slice interface: client interface, proxy struct, and
/// encoder/decoder extensions.</summary>
internal static class ProxyGenerator
{
    /// <summary>Generates all proxy-side code blocks for the given interface.</summary>
    internal static CodeBlock Generate(Interface interfaceDef)
    {
        string name = interfaceDef.Name;
        string scopedId = interfaceDef.ScopedIdentifier;
        string accessModifier = interfaceDef.AccessModifier;
        string currentNamespace = interfaceDef.Namespace;
        string defaultServicePath = $"/{scopedId.Replace("::", ".", StringComparison.Ordinal)}";

        return CodeBlock.FromBlocks(
        [
            GenerateClientInterface(interfaceDef, name, scopedId, accessModifier, currentNamespace),
            GenerateProxyStruct(interfaceDef, name, scopedId, accessModifier, currentNamespace, defaultServicePath),
            GenerateProxyEncoderExtensions(name, accessModifier),
            GenerateProxyDecoderExtensions(name, accessModifier),
        ]);
    }

    // --- Client Interface ---

    private static CodeBlock GenerateClientInterface(
        Interface interfaceDef,
        string name,
        string scopedId,
        string accessModifier,
        string currentNamespace)
    {
        var builder = new ContainerBuilder($"{accessModifier} partial interface", $"I{name}");
        builder.AddComment(
            "remarks",
            $"""
            The Slice compiler generated this client-side interface from Slice interface <c>{scopedId}</c>.
            It's implemented by <see cref="{name}Proxy" />.
            """);

        // Inherit from base client interfaces
        foreach (Interface baseInterface in interfaceDef.Bases)
        {
            builder.AddBase($"I{baseInterface.Name}");
        }

        foreach (Operation op in interfaceDef.Operations)
        {
            builder.AddBlock(BuildClientOperationDeclaration(op, currentNamespace));
        }

        return builder.Build();
    }

    private static CodeBlock BuildClientOperationDeclaration(Operation op, string currentNamespace)
    {
        string opName = op.Name;
        string returnType = GetClientReturnType(op, currentNamespace);
        ImmutableList<Field> nonStreamedParams = op.NonStreamedParameters;

        var fn = new FunctionBuilder("", returnType, $"{opName}Async", FunctionType.Declaration);

        foreach (Field param in nonStreamedParams)
        {
            fn.AddParameter(
                param.DataType.OutgoingParameterTypeString(param.DataTypeIsOptional, currentNamespace),
                param.ParameterName);
        }

        // Streamed parameter (if any) goes after non-streamed params
        if (op.StreamedParameter is Field streamParam)
        {
            fn.AddParameter(
                OperationExtensions.GetStreamTypeString(streamParam, currentNamespace),
                streamParam.ParameterName);
        }

        string featuresParam = op.FeaturesParamName;
        fn.AddParameter("IceRpc.Features.IFeatureCollection?", featuresParam, "null", "The invocation features.");
        fn.AddParameter(
            "global::System.Threading.CancellationToken",
            "cancellationToken",
            "default",
            "A cancellation token that receives the cancellation requests.");

        if (op.NonStreamedReturns.Count == 0 && !op.HasStreamedReturn)
        {
            fn.AddComment("returns", "A task that completes when the response is received.");
        }

        return fn.Build();
    }

    // --- Proxy Struct ---

    private static CodeBlock GenerateProxyStruct(
        Interface interfaceDef,
        string name,
        string scopedId,
        string accessModifier,
        string currentNamespace,
        string defaultServicePath)
    {
        string proxyName = $"{name}Proxy";

        var builder = new ContainerBuilder($"{accessModifier} readonly partial record struct", proxyName);
        builder.AddComment(
            "summary",
            $"""
            Implements <see cref="I{name}" /> by making invocations on a remote IceRPC service.
            This remote service must implement Slice interface {scopedId}.
            """);
        builder.AddComment(
            "remarks",
            $"The Slice compiler generated this record struct from the Slice interface <c>{scopedId}</c>.");
        builder.AddBase($"I{name}");
        builder.AddBase("ISliceProxy");

        builder.AddBlock(BuildProxyRequestClass(interfaceDef, scopedId, currentNamespace));
        builder.AddBlock(BuildProxyResponseClass(interfaceDef, scopedId, currentNamespace));
        builder.AddBlock(BuildProxyProperties(scopedId, defaultServicePath));

        // Implicit conversion operators for base interfaces
        foreach (Interface baseInterface in interfaceDef.AllBases)
        {
            string baseProxyName = $"{baseInterface.Name}Proxy";
            var implicitOp = new FunctionBuilder(
                "public static implicit",
                "",
                $"operator {baseProxyName}",
                FunctionType.ExpressionBody);
            implicitOp.AddComment(
                "summary",
                $"""Provides an implicit conversion to <see cref="{baseProxyName}" />.""");
            implicitOp.AddParameter(proxyName, "proxy");
            implicitOp.SetBody(
                "new() { EncodeOptions = proxy.EncodeOptions, Invoker = proxy.Invoker, ServiceAddress = proxy.ServiceAddress }");
            builder.AddBlock(implicitOp.Build());
        }

        builder.AddBlock(BuildProxyConstructors(proxyName));

        foreach (Operation op in interfaceDef.Operations)
        {
            builder.AddBlock(BuildProxyOperationImpl(op, currentNamespace));
        }

        return builder.Build();
    }

    private static CodeBlock BuildProxyRequestClass(Interface interfaceDef, string scopedId, string currentNamespace)
    {
        var request = new ContainerBuilder("public static class", "Request");
        request.AddComment("summary", "Provides static methods that encode operation arguments into request payloads.");
        request.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice interface <c>{scopedId}</c>.");

        foreach (Operation op in interfaceDef.Operations)
        {

            ImmutableList<Field> nonStreamedParams = op.NonStreamedParameters;
            string opName = op.Name;

            CodeBlock? encodeBody = GenerateEncodeBody(nonStreamedParams, currentNamespace);

            if (encodeBody is null)
            {
                // Stream operations with no non-streamed params use CreateEmptySliceStructPayload
                // (encodes an empty Slice2 struct). Truly void operations use EmptyPipeReader.Instance.
                string emptyPayload = op.HasStreamedParameter
                    ? "System.IO.Pipelines.PipeReader.CreateEmptySliceStructPayload()"
                    : "IceRpc.EmptyPipeReader.Instance";

                var fn = new FunctionBuilder(
                    "public static",
                    "global::System.IO.Pipelines.PipeReader",
                    $"Encode{opName}",
                    FunctionType.ExpressionBody);
                fn.AddComment("summary", $"Encodes the arguments of operation <c>{op.Identifier}</c> into a request payload.");
                fn.AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.");
                fn.AddComment("returns", "The Slice-encoded payload.");
                fn.SetBody(emptyPayload);
                request.AddBlock(fn.Build());
            }
            else
            {
                var fn = new FunctionBuilder(
                    "public static",
                    "global::System.IO.Pipelines.PipeReader",
                    $"Encode{opName}",
                    FunctionType.BlockBody);
                fn.AddComment(
                    "summary",
                    $"Encodes the argument{(nonStreamedParams.Count > 1 ? "s" : "")} of operation <c>{op.Identifier}</c> into a request payload.");

                foreach (Field param in nonStreamedParams)
                {
                    fn.AddParameter(
                        param.DataType.OutgoingParameterTypeString(param.DataTypeIsOptional, currentNamespace),
                        param.ParameterName);
                }

                fn.AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.");
                fn.AddComment("returns", "The Slice-encoded payload.");
                fn.SetBody(BuildPipeEncodeBody(encodeBody));
                request.AddBlock(fn.Build());
            }

            // Add EncodeStreamOf{Op} method for streamed parameters
            if (op.StreamedParameter is Field streamParam)
            {
                request.AddBlock(BuildEncodeStreamMethod(op, streamParam, currentNamespace));
            }
        }

        return request.Build();
    }

    /// <summary>Builds the EncodeStreamOf{Op} method for a streamed field (parameter or return value).</summary>
    internal static CodeBlock BuildEncodeStreamMethod(Operation op, Field streamParam, string currentNamespace)
    {
        string opName = op.Name;
        string streamType = OperationExtensions.GetStreamTypeString(streamParam, currentNamespace);

        var fn = new FunctionBuilder(
            "public static",
            "global::System.IO.Pipelines.PipeReader",
            $"EncodeStreamOf{opName}",
            FunctionType.ExpressionBody);
        fn.AddComment(
            "summary",
            $"Encodes the stream argument of operation <c>{op.Name}</c> into a request payload continuation.");
        fn.AddParameter(streamType, streamParam.ParameterName);
        fn.AddParameter("SliceEncodeOptions?", "encodeOptions", "null", "The Slice encode options.");
        fn.AddComment("returns", "A new request payload continuation.");

        if (OperationExtensions.IsByteStream(streamParam))
        {
            // Non-optional byte stream: pass-through PipeReader
            fn.SetBody(streamParam.ParameterName);
        }
        else
        {
            // Other streams: use ToPipeReader with encode lambda
            string encodeLambda = streamParam.DataType.GetEncodeLambda(
                streamParam.DataTypeIsOptional,
                currentNamespace);
            bool useSegments = streamParam.DataType.FixedSize is null;
            fn.SetBody(new CodeBlock($$"""
                {{streamParam.ParameterName}}.ToPipeReader(
                    {{encodeLambda}},
                    {{(useSegments ? "true" : "false")}},
                    encodeOptions)
                """));
        }

        return fn.Build();
    }

    private static CodeBlock BuildProxyResponseClass(Interface interfaceDef, string scopedId, string currentNamespace)
    {
        var response = new ContainerBuilder("public static class", "Response");
        response.AddComment(
            "summary",
            $"Provides a <see cref=\"ResponseDecodeFunc{{T}}\" /> for each operation defined in Slice interface {scopedId}.");
        response.AddComment(
            "remarks",
            $"The Slice compiler generated this static class from the Slice interface <c>{scopedId}</c>.");

        foreach (Operation op in interfaceDef.Operations)
        {
            string opName = op.Name;
            ImmutableList<Field> nonStreamedReturns = op.NonStreamedReturns;
            Field? streamReturn = op.StreamedReturn;
            string returnType = GetProxyResponseReturnType(op, currentNamespace);

            if (streamReturn is null)
            {
                // Non-streaming: expression body
                var fn = new FunctionBuilder(
                    "public static",
                    returnType,
                    $"Decode{opName}Async",
                    FunctionType.ExpressionBody);
                fn.AddComment("summary", $"Decodes an incoming response for operation <c>{op.Identifier}</c>.");
                fn.AddParameter("IceRpc.IncomingResponse", "response");
                fn.AddParameter("IceRpc.OutgoingRequest", "request");
                fn.AddParameter("ISliceProxy", "sender");
                fn.AddParameter("global::System.Threading.CancellationToken", "cancellationToken");

                if (nonStreamedReturns.Count == 0)
                {
                    fn.SetBody(
                        """
                        response.DecodeVoidReturnValueAsync(
                            request,
                            cancellationToken)
                        """);
                }
                else
                {
                    string decodeLambda = GenerateDecodeLambda(nonStreamedReturns, currentNamespace);
                    fn.SetBody(
                        $$"""
                        response.DecodeReturnValueAsync(
                            request,
                            sender,
                            {{decodeLambda}},
                            cancellationToken)
                        """);
                }

                response.AddBlock(fn.Build());
            }
            else
            {
                // Streaming: async block body
                var fn = new FunctionBuilder(
                    "public static async",
                    returnType,
                    $"Decode{opName}Async",
                    FunctionType.BlockBody);
                fn.AddComment("summary", $"Decodes an incoming response for operation <c>{op.Identifier}</c>.");
                fn.AddParameter("IceRpc.IncomingResponse", "response");
                fn.AddParameter("IceRpc.OutgoingRequest", "request");
                fn.AddParameter("ISliceProxy", "sender");
                fn.AddParameter("global::System.Threading.CancellationToken", "cancellationToken");

                var body = new CodeBlock();

                // Decode non-streamed returns first
                if (nonStreamedReturns.Count == 0)
                {
                    body.WriteLine(
                        """
                        await response.DecodeVoidReturnValueAsync(
                            request,
                            cancellationToken).ConfigureAwait(false);
                        """);
                }
                else
                {
                    string decodeLambda = GenerateDecodeLambda(nonStreamedReturns, currentNamespace);
                    string varName = nonStreamedReturns.Count == 1
                        ? $"sliceP_{nonStreamedReturns[0].ParameterName}"
                        : $"({string.Join(", ", nonStreamedReturns.Select(r => $"sliceP_{r.ParameterName}"))})";
                    body.WriteLine($$"""
                        var {{varName}} = await response.DecodeReturnValueAsync(
                            request,
                            sender,
                            {{decodeLambda}},
                            cancellationToken).ConfigureAwait(false);
                        """);
                }

                // Decode stream return
                body.Write("\n");
                if (OperationExtensions.IsByteStream(streamReturn))
                {
                    body.WriteLine("var sliceP_returnValue = IceRpc.IncomingFrameExtensions.DetachPayload(response);");
                }
                else
                {
                    body.WriteLine("var payloadContinuation = IceRpc.IncomingFrameExtensions.DetachPayload(response);");
                    string streamElemType = streamReturn.DataType.FieldTypeString(streamReturn.DataTypeIsOptional, currentNamespace);
                    string decodeLambda = streamReturn.DataType.Type.GetDecodeLambda(streamReturn.DataTypeIsOptional, currentNamespace);

                    if (streamReturn.DataType.FixedSize is int fixedSize && !streamReturn.DataTypeIsOptional)
                    {
                        body.WriteLine($$"""
                            var sliceP_returnValue = payloadContinuation.ToAsyncEnumerable<{{streamElemType}}>(
                                {{decodeLambda}},
                                {{fixedSize}});
                            """);
                    }
                    else
                    {
                        body.WriteLine($$"""
                            var sliceP_returnValue = payloadContinuation.ToAsyncEnumerable<{{streamElemType}}>(
                                {{decodeLambda}},
                                sender,
                                sliceFeature: request.Features.Get<IceRpc.Features.ISliceFeature>());
                            """);
                    }
                }

                // Build return statement
                if (nonStreamedReturns.Count == 0)
                {
                    body.Write("\nreturn sliceP_returnValue;");
                }
                else
                {
                    var returnParts = nonStreamedReturns.Select(r => $"sliceP_{r.ParameterName}").ToList();
                    returnParts.Add("sliceP_returnValue");
                    body.Write($"\nreturn ({string.Join(", ", returnParts)});");
                }

                fn.SetBody(body);
                response.AddBlock(fn.Build());
            }
        }

        return response.Build();
    }

    private static CodeBlock BuildProxyOperationImpl(Operation op, string currentNamespace)
    {
        string opName = op.Name;
        string returnType = GetClientReturnType(op, currentNamespace);
        ImmutableList<Field> nonStreamedParams = op.NonStreamedParameters;
        Field? streamParam = op.StreamedParameter;

        bool compressArgs = op.Attributes.FindAttribute("compress") is { } compressAttr
            && compressAttr.Args.Any(a => a == "Args");

        var fn = new FunctionBuilder(
            "public",
            returnType,
            $"{opName}Async",
            compressArgs ? FunctionType.BlockBody : FunctionType.ExpressionBody);
        fn.SetInheritDoc(true);

        foreach (Field param in nonStreamedParams)
        {
            fn.AddParameter(
                param.DataType.OutgoingParameterTypeString(param.DataTypeIsOptional, currentNamespace),
                param.ParameterName);
        }

        if (streamParam is not null)
        {
            fn.AddParameter(OperationExtensions.GetStreamTypeString(streamParam, currentNamespace), streamParam.ParameterName);
        }

        string featuresParam = op.FeaturesParamName;
        fn.AddParameter("IceRpc.Features.IFeatureCollection?", featuresParam, "null");
        fn.AddParameter("global::System.Threading.CancellationToken", "cancellationToken", "default");

        string encodeArgs = nonStreamedParams.Count > 0
            ? string.Join(", ", nonStreamedParams.Select(p => p.ParameterName)) + ", encodeOptions: EncodeOptions"
            : "encodeOptions: EncodeOptions";

        // payloadContinuation: either null or the stream encoder
        string payloadContinuation = streamParam is not null
            ? $"Request.EncodeStreamOf{opName}({streamParam.ParameterName}, encodeOptions: EncodeOptions)"
            : "null";

        var bodyBuilder = new FunctionCallBuilder("this.InvokeOperationAsync");
        bodyBuilder.ArgumentsOnNewLine(true);
        bodyBuilder.UseSemicolon(false);
        bodyBuilder.AddArgument($"\"{op.Identifier}\"");
        bodyBuilder.AddArgument($"payload: Request.Encode{opName}({encodeArgs})");
        bodyBuilder.AddArgument($"payloadContinuation: {payloadContinuation}");
        bodyBuilder.AddArgument($"Response.Decode{opName}Async");
        bodyBuilder.AddArgument(featuresParam);
        if (op.IsIdempotent)
        {
            bodyBuilder.AddArgument("isIdempotent: true");
        }
        bodyBuilder.AddArgument("cancellationToken: cancellationToken");

        if (compressArgs)
        {
            var body = new CodeBlock();
            body.WriteLine($$"""
                if ({{featuresParam}}?.Get<IceRpc.Features.ICompressFeature>() is null)
                {
                    {{featuresParam}} ??= new IceRpc.Features.FeatureCollection();
                    {{featuresParam}} = IceRpc.Features.FeatureCollectionExtensions.With(
                        {{featuresParam}},
                        IceRpc.Features.CompressFeature.Compress);
                }
                """);
            body.Write("\nreturn ");
            body.AddBlock(bodyBuilder.Build());
            body.Write(";");
            fn.SetBody(body);
        }
        else
        {
            fn.SetBody(bodyBuilder.Build());
        }
        return fn.Build();
    }

    // --- Encoder/Decoder Extensions ---

    private static CodeBlock GenerateProxyEncoderExtensions(string name, string accessModifier)
    {
        string proxyName = $"{name}Proxy";

        var container = new ContainerBuilder($"{accessModifier} static class", $"{proxyName}SliceEncoderExtensions");
        container.AddComment(
            "summary",
            $"""Provides an extension method for <see cref="SliceEncoder" /> to encode a <see cref="{proxyName}" />.""");

        var fn = new FunctionBuilder("public static", "void", $"Encode{proxyName}", FunctionType.ExpressionBody);
        fn.AddComment("summary", $"""Encodes a <see cref="{proxyName}" /> as an <see cref="IceRpc.ServiceAddress" />.""");
        fn.AddParameter("this ref SliceEncoder", "encoder", docComment: "The Slice encoder.");
        fn.AddParameter(proxyName, "proxy", docComment: "The proxy to encode as a service address.");
        fn.SetBody("encoder.EncodeServiceAddress(proxy.ServiceAddress)");

        container.AddBlock(fn.Build());
        return container.Build();
    }

    private static CodeBlock GenerateProxyDecoderExtensions(string name, string accessModifier)
    {
        string proxyName = $"{name}Proxy";

        var container = new ContainerBuilder($"{accessModifier} static class", $"{proxyName}SliceDecoderExtensions");
        container.AddComment(
            "summary",
            $"""Provides an extension method for <see cref="SliceDecoder" /> to decode a <see cref="{proxyName}" />.""");

        var fn = new FunctionBuilder("public static", proxyName, $"Decode{proxyName}", FunctionType.ExpressionBody);
        fn.AddComment("summary", $"""Decodes an <see cref="IceRpc.ServiceAddress" /> into a <see cref="{proxyName}" />.""");
        fn.AddParameter("this ref SliceDecoder", "decoder", docComment: "The Slice decoder.");
        fn.AddComment("returns", "The proxy created from the decoded service address.");
        fn.SetBody($"decoder.DecodeProxy<{proxyName}>()");

        container.AddBlock(fn.Build());
        return container.Build();
    }

    // --- Properties and Constructors ---

    private static CodeBlock BuildProxyProperties(string scopedId, string defaultServicePath) =>
        $$"""
        /// <summary>Represents the default path for IceRPC services that implement Slice interface
        /// <c>{{scopedId}}</c>.</summary>
        public const string DefaultServicePath = "{{defaultServicePath}}";

        /// <inheritdoc/>
        public SliceEncodeOptions? EncodeOptions { get; init; }

        /// <inheritdoc/>
        public required IceRpc.IInvoker Invoker { get; init; }

        /// <inheritdoc/>
        public IceRpc.ServiceAddress ServiceAddress { get; init; } = _defaultServiceAddress;

        private static IceRpc.ServiceAddress _defaultServiceAddress =
            new(IceRpc.Protocol.IceRpc) { Path = DefaultServicePath };
        """;

    private static CodeBlock BuildProxyConstructors(string proxyName)
    {
        var fromPath = new FunctionBuilder("public static", proxyName, "FromPath", FunctionType.ExpressionBody);
        fromPath.AddComment("summary", "Creates a relative proxy from a path.");
        fromPath.AddParameter("string", "path", docComment: "The path.");
        fromPath.AddComment("returns", "The new relative proxy.");
        fromPath.SetBody("new(IceRpc.InvalidInvoker.Instance, new IceRpc.ServiceAddress { Path = path })");

        var mainCtor = new FunctionBuilder("public", "", proxyName, FunctionType.BlockBody);
        mainCtor.AddComment("summary", "Constructs a proxy from an invoker, a service address and encode options.");
        mainCtor.AddParameter("IceRpc.IInvoker", "invoker", docComment: "The invocation pipeline of the proxy.");
        mainCtor.AddParameter(
            "IceRpc.ServiceAddress?",
            "serviceAddress",
            "null",
            """
            The service address. <see langword="null" /> is equivalent to an icerpc service address
            with path <see cref="DefaultServicePath" />.
            """);
        mainCtor.AddParameter(
            "SliceEncodeOptions?",
            "encodeOptions",
            "null",
            "The encode options, used to customize the encoding of request payloads.");
        mainCtor.AddSetsRequiredMembersAttribute();
        mainCtor.SetBody(new CodeBlock("""
            Invoker = invoker;
            ServiceAddress = serviceAddress ?? _defaultServiceAddress;
            EncodeOptions = encodeOptions;
            """));

        var uriCtor = new FunctionBuilder("public", "", proxyName, FunctionType.BlockBody);
        uriCtor.AddComment("summary", "Constructs a proxy from an invoker, a service address URI and encode options.");
        uriCtor.AddParameter("IceRpc.IInvoker", "invoker", docComment: "The invocation pipeline of the proxy.");
        uriCtor.AddParameter("System.Uri", "serviceAddressUri", docComment: "A URI that represents a service address.");
        uriCtor.AddParameter(
            "SliceEncodeOptions?",
            "encodeOptions",
            "null",
            "The encode options, used to customize the encoding of request payloads.");
        uriCtor.AddSetsRequiredMembersAttribute();
        uriCtor.AddBaseParameters(["invoker", "new IceRpc.ServiceAddress(serviceAddressUri)", "encodeOptions"]);

        var defaultCtor = new FunctionBuilder("public", "", proxyName, FunctionType.BlockBody);
        defaultCtor.AddComment(
            "summary",
            @"Constructs a proxy with an icerpc service address with path <see cref=""DefaultServicePath"" />.");

        return CodeBlock.FromBlocks([fromPath.Build(), mainCtor.Build(), uriCtor.Build(), defaultCtor.Build()]);
    }

    /// <summary>Wraps an encode body in the standard pipe creation/size-placeholder boilerplate.</summary>
    internal static CodeBlock BuildPipeEncodeBody(CodeBlock encodeBody)
    {
        return new CodeBlock($$"""
            var pipe_ = new global::System.IO.Pipelines.Pipe(
                encodeOptions?.PipeOptions ?? SliceEncodeOptions.Default.PipeOptions);
            var encoder_ = new SliceEncoder(pipe_.Writer);

            Span<byte> sizePlaceholder_ = encoder_.GetPlaceholderSpan(4);
            int startPos_ = encoder_.EncodedByteCount;

            {{encodeBody}}

            SliceEncoder.EncodeVarUInt62((ulong)(encoder_.EncodedByteCount - startPos_), sizePlaceholder_);

            pipe_.Writer.Complete();
            return pipe_.Reader;
            """);
    }
}
