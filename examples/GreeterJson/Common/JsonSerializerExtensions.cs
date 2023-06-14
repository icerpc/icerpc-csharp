// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text.Json;

namespace IceRpc.Json;

/// <summary>A helper class for serializing and deserializing JSON objects into a from <see cref="PipeReader"/>.
/// </summary>
public static class JsonSerializerExtensions
{
    /// <summary>Write the JSON representation of the <paramref name="value"/> and returns a <see cref="PipeReader"/>
    /// that holds the serialized data.</summary>
    /// <typeparam name="TValue">The type of the value to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">The options for the <see cref="Utf8JsonWriter"/>.</param>
    /// <returns>A <see cref="PipeReader"/> holding the serialized data.</returns>
    public static PipeReader Serialize<TValue>(TValue value, JsonWriterOptions? options = null)
    {
        var pipe = new Pipe();
        using var jsonWriter = new Utf8JsonWriter(pipe.Writer, options ?? new JsonWriterOptions());
        JsonSerializer.Serialize(jsonWriter, value);
        pipe.Writer.Complete();
        return pipe.Reader;
    }

    /// <summary>Reads one JSON value of type <typeparamref name="TValue"/> and returns it.</summary>
    /// <typeparam name="TValue">The type of the value to read.</typeparam>
    /// <param name="reader">The <see cref="PipeReader"/> holding the JSON data.</param>
    /// <param name="options">The options for the <see cref="Utf8JsonReader"/>.</param>
    /// <param name="cancellationToken">A cancellation token that receives the cancellation requests.</param>
    /// <returns>The read value.</returns>
    public static async Task<TValue> DeserializeAsync<TValue>(
        PipeReader reader,
        JsonReaderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ReadResult readResult = await reader.ReadAtLeastAsync(int.MaxValue, cancellationToken);
        Debug.Assert(readResult.IsCompleted && !readResult.IsCanceled);

        TValue value = Decode(readResult.Buffer);
        reader.Complete();
        return value;

        TValue Decode(ReadOnlySequence<byte> buffer)
        {
            var jsonReader = new Utf8JsonReader(buffer, options ?? new());
            return JsonSerializer.Deserialize(ref jsonReader, typeof(TValue)) is TValue value ?
                value : throw new InvalidDataException($"Unable to decode {typeof(TValue)}.");
        }
    }
}
