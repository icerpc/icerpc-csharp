// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Generator;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Generator;
using ZeroC.Slice.Symbols;

await GeneratorDriver.RunAsync(
    generateCode: (symbol, currentNamespace) => symbol is Interface interfaceDef
        ? CodeBlock.FromBlocks([ProxyGenerator.Generate(interfaceDef), DispatchGenerator.Generate(interfaceDef)])
        : null,
    mapOutputPath: path => Path.ChangeExtension(Path.GetFileName(path), ".IceRpc.cs"),
    usings: ["IceRpc.Slice", "IceRpc.Slice.Operations", "ZeroC.Slice.Codec"]).ConfigureAwait(false);
