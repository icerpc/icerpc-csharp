// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using System.IO.Pipelines;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Codec;
using ZeroC.Slice.Generator;
using ZeroC.Slice.Symbols;

using Compiler = ZeroC.Slice.Symbols.Internal.Compiler;

// The Slice compiler executes this program and writes the Slice2-encoded request to stdin.

using Stream stdin = Console.OpenStandardInput();
var reader = PipeReader.Create(stdin);

// Read until the Slice compiler closes stdin.
ReadResult readResult;
do
{
    readResult = await reader.ReadAsync().ConfigureAwait(false);
    if (!readResult.IsCompleted)
    {
        reader.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
    }
}
while (!readResult.IsCompleted);

var decoder = new SliceDecoder(
    readResult.Buffer,
    SliceEncoding.Slice2,
    maxCollectionAllocation: (int)readResult.Buffer.Length * 16);
string op = decoder.DecodeString();

// Decode source files and reference files.
Compiler.SliceFile[] sourceFiles = decoder.DecodeSequence((ref decoder) => new Compiler.SliceFile(ref decoder));
Compiler.SliceFile[] referenceFiles = decoder.DecodeSequence((ref decoder) => new Compiler.SliceFile(ref decoder));

reader.AdvanceTo(readResult.Buffer.End);
reader.Complete();

// Convert decoded types into rich symbols with resolved references.
ImmutableList<SliceFile> symbolFiles = SymbolConverter.ConvertFiles(sourceFiles, referenceFiles);

// Generate code for each source file.
var structGen = new StructGenerator();
var enumUnderlyingGen = new EnumWithUnderlyingGenerator();
var enumFieldsGen = new EnumWithFieldsGenerator();

foreach (SliceFile file in symbolFiles)
{
    var fileCode = new CodeBlock($"// Generated from '{file.Path}'");
    foreach (ISymbol symbol in file.Contents)
    {
        CodeBlock? code = symbol switch
        {
            Struct s => structGen.Generate(s),
            EnumWithUnderlying e => EnumWithUnderlyingGenerator.Generate(e),
            EnumWithFields e => enumFieldsGen.Generate(e),
            _ => null,
        };

        if (code is not null)
        {
            fileCode.AddBlock(code);
        }
    }
    Console.Error.WriteLine(fileCode.ToString());
}
