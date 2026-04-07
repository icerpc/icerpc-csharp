// Copyright (c) ZeroC, Inc.

using System.Collections.Immutable;
using System.IO.Pipelines;
using ZeroC.Slice.Codec;

namespace ZeroC.Slice.Symbols;

/// <summary>Handles the Slice compiler protocol: decoding the request, converting compiler types to rich symbols,
/// invoking a transform function, and encoding the response.</summary>
public static class Generator
{
    /// <summary>Reads a Slice-encoded request from <paramref name="input"/>, converts the decoded types into rich
    /// symbols, invokes <paramref name="transform"/> with the converted source files, and writes the Slice-encoded
    /// response to <paramref name="output"/>.</summary>
    /// <param name="input">The pipe reader to read the Slice-encoded request from.</param>
    /// <param name="output">The pipe writer to write the Slice-encoded response to.</param>
    /// <param name="transform">A function that receives the converted source files and the options dictionary, and
    /// returns the generator response.</param>
    public static async Task RunAsync(
        PipeReader input,
        PipeWriter output,
        Func<ImmutableList<SliceFile>, Dictionary<string, string>, GeneratorResponse> transform)
    {
        // Read all data from input.
        ReadResult readResult;
        do
        {
            readResult = await input.ReadAsync().ConfigureAwait(false);
            if (!readResult.IsCompleted)
            {
                input.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
            }
        }
        while (!readResult.IsCompleted);

        // Decode the Slice-encoded request.
        var decoder = new SliceDecoder(readResult.Buffer);
        _ = decoder.DecodeString(); // operation name

        Compiler.SliceFile[] sourceFiles =
            decoder.DecodeSequence((ref decoder) => new Compiler.SliceFile(ref decoder));
        Compiler.SliceFile[] referenceFiles =
            decoder.DecodeSequence((ref decoder) => new Compiler.SliceFile(ref decoder));

        Dictionary<string, string> options = decoder.DecodeDictionary(
            count => new Dictionary<string, string>(count),
            (ref decoder) => decoder.DecodeString(),
            (ref decoder) => decoder.DecodeString());

        input.AdvanceTo(readResult.Buffer.End);
        input.Complete();

        // Convert to rich symbols.
        ImmutableList<SliceFile> symbolFiles = SymbolConverter.ConvertFiles(sourceFiles, referenceFiles);

        // Invoke the transform.
        GeneratorResponse response = transform(symbolFiles, options);

        // Convert public types to internal compiler types and encode the response.
        var encoder = new SliceEncoder(output);
        encoder.EncodeSequence(
            response.GeneratedFiles,
            (ref encoder, file) =>
                new Compiler.GeneratedFile(file.Path, file.Contents).Encode(ref encoder));
        encoder.EncodeSequence(
            response.Diagnostics,
            (ref encoder, diagnostic) =>
                new Compiler.Diagnostic(
                    (Compiler.DiagnosticLevel)(byte)diagnostic.Level,
                    diagnostic.Message,
                    diagnostic.Source).Encode(ref encoder));
        await output.FlushAsync().ConfigureAwait(false);
        output.Complete();
    }
}
