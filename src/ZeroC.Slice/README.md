# The Slice serialization library

Slice is a serialization format for structured data: it allows you to encode structured data into streams of bytes and
later decode these bytes to recreate the structured data.

Slice is also a modern [Interface Definition Language][idl] (IDL): you define your structured data with the Slice IDL
and then use the Slice compiler to generate code that encodes/decodes this structured data to/from bytes in Slice
format.

In C#, the [Slice compiler for C#][slicec-cs] generates code that relies on this Slice serialization library.

Unlike older C# libraries, Slice relies on [System.Buffers][cs-buffers] (`IBufferWriter<byte>`,
`ReadOnlySequence<byte>`) and is typically used with the [Pipelines][pipelines] library.

[Package][package] | [Source code][source] | [Documentation][docs] | [API reference][api]

## Sample Code

```slice
// Slice definition

struct Person {
    name: string
    id: int32
    tag(1) email: string?
}
```

```csharp
// Generated by the Slice compiler for C#

public partial record struct Person
{
    public required string Name { get; set; }

    public int Id { get; set; }

    public string? Email { get; set; }

    /// <summary>Constructs a new instance of <see cref="Person" />.</summary>
    [global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public Person(string name, int id, string? email)
    {
        this.Name = name;
        ...
    }

    /// <summary>Constructs a new instance of <see cref="Person" /> and decodes its fields
    /// from a Slice decoder.</summary>
    /// <param name="decoder">The Slice decoder.</param>
    [global::System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public Person(ref SliceDecoder decoder)
    {
        this.Name = decoder.DecodeString();
        ...
    }

    /// <summary>Encodes the fields of this struct with a Slice encoder.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    public readonly void Encode(ref SliceEncoder encoder)
    {
        encoder.EncodeString(this.Name);
        ...
    }
}
```

[api]: https://docs.icerpc.dev/api/csharp/api/ZeroC.Slice.html
[cs-buffers]: https://learn.microsoft.com/en-us/dotnet/standard/io/buffers
[docs]: https://docs.icerpc.dev/slice2
[idl]: https://en.wikipedia.org/wiki/Interface_description_language
[package]: https://www.nuget.org/packages/ZeroC.Slice
[pipelines]: https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines
[slicec-cs]: https://github.com/icerpc/icerpc-csharp/tree/main/tools/IceRpc.Slice.Tools
[source]: https://github.com/icerpc/icerpc-csharp/tree/main/src/ZeroC.Slice
