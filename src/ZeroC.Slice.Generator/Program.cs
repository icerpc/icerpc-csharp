// Copyright (c) ZeroC, Inc.

using ZeroC.Slice.Generator;
using ZeroC.Slice.Symbols;

if (args.Length > 0)
{
    Console.Error.WriteLine("ZeroC.Slice.Generator does not accept any command-line arguments.");
    return 1;
}

await GeneratorDriver.RunAsync(
    generateCode: (symbol, currentNamespace) => symbol switch
    {
        Struct s => StructGenerator.Generate(s),
        BasicEnum e => BasicEnumGenerator.Generate(e),
        VariantEnum e => VariantEnumGenerator.Generate(e),
        _ => null,
    },
    mapOutputPath: path => Path.ChangeExtension(Path.GetFileName(path), ".cs"),
    usings: ["ZeroC.Slice.Codec"]).ConfigureAwait(false);

return 0;
