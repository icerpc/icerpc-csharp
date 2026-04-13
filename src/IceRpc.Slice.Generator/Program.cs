// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Generator;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Generator;
using ZeroC.Slice.Symbols;

if (args.Length > 0)
{
    Console.Error.WriteLine("IceRpc.Slice.Generator does not accept any command-line arguments.");
    return 1;
}

await GeneratorDriver.RunAsync(
    generateCode: (symbol, currentNamespace) => symbol is Interface interfaceDef
        ? CodeBlock.FromBlocks([ProxyGenerator.Generate(interfaceDef), ServiceGenerator.Generate(interfaceDef)])
        : null,
    mapOutputPath: path => Path.ChangeExtension(Path.GetFileName(path), ".IceRpc.cs"),
    usings: ["IceRpc.Slice", "IceRpc.Slice.Operations", "ZeroC.Slice.Codec"]).ConfigureAwait(false);

return 0;
