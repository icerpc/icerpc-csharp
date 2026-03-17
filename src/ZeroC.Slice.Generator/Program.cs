// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using System.IO.Pipelines;
using ZeroC.CodeBuilder;
using ZeroC.Slice.Codec;
using ZeroC.Slice.Generator;
using ZeroC.Slice.Symbols;

using Compiler = ZeroC.Slice.Symbols.Compiler;

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
var generatedFiles = new List<Compiler.GeneratedFile>();
var diagnostics = new List<Compiler.Diagnostic>();
foreach (SliceFile file in symbolFiles)
{
    var codeBlocks = new List<CodeBlock>();
    foreach (ISymbol symbol in file.Contents)
    {
        CodeBlock? code = symbol switch
        {
            Struct s => StructGenerator.Generate(s),
            EnumWithUnderlying e => EnumWithUnderlyingGenerator.Generate(e),
            EnumWithFields e => EnumWithFieldsGenerator.Generate(e),
            _ => null,
        };

        if (code is not null)
        {
            codeBlocks.Add(code);
        }
    }

    if (codeBlocks.Count > 0)
    {
        var fileCode = new CodeBlock($"// Generated from '{file.Path}'");
        fileCode.AddBlock(CodeBlock.FromBlocks(codeBlocks));
        generatedFiles.Add(new Compiler.GeneratedFile(
            Path.ChangeExtension(file.Path, ".slice"), fileCode.ToString()));
    }
}

// Encode and write the response to stdout.
var pipe = new Pipe();
var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2);
encoder.EncodeSequence(
    generatedFiles, 
    (ref encoder, file) =>
    {
        encoder.EncodeString(file.Path);
        encoder.EncodeString(file.Contents);
    });

encoder.EncodeSequence(
    diagnostics,
    (ref encoder, diagnostic) => diagnostic.Encode(ref encoder));

await pipe.Writer.FlushAsync().ConfigureAwait(false);
pipe.Writer.Complete();

/*using Stream stdout = Console.OpenStandardOutput();
await pipe.Reader.CopyToAsync(stdout).ConfigureAwait(false);
pipe.Reader.Complete();*/
