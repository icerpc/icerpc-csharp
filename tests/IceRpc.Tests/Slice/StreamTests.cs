// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests.Slice;

public class StreamTests
{
    /// <summary>Verifies that we correctly encode an async enumerable of fixed size elements.</summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the payload stream pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Encode_stream_of_fixed_size_elements(int size, int yieldThreshold)
    {
        // Arrange
        var expected = Enumerable.Range(0, size).Select(i => i).ToArray();

        // Act
        PipeReader payload = SliceEncoding.Slice2.CreatePayloadStream(
            GetDataAsync(size),
            encodeOptions: null,
            (ref SliceEncoder encoder, int value) => encoder.EncodeInt32(value),
            useSegments: false);

        // Assert
        Assert.That(async () => await DecodeDataAsync(payload), Is.EqualTo(expected));

        async IAsyncEnumerable<int> GetDataAsync(int size)
        {
            for (int i = 0; i < size; i++)
            {
                if (yieldThreshold > 0 && i % yieldThreshold == 0)
                {
                    await Task.Yield();
                }
                yield return i;
            }
        }

        async Task<int[]> DecodeDataAsync(PipeReader payload)
        {
            var data = new List<int>();
            ReadResult readResult;
            do
            {
                readResult = await payload.ReadAsync();
                data.AddRange(DecodeIntStream(readResult.Buffer));
                payload.AdvanceTo(readResult.Buffer.End);
            }
            while (!readResult.IsCompleted);
            return data.ToArray();
        }

        static List<int> DecodeIntStream(ReadOnlySequence<byte> buffer)
        {
            var data = new List<int>();
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
            if (buffer.Length > 0)
            {
                while (decoder.Consumed < buffer.Length)
                {
                    data.Add(decoder.DecodeInt32());
                }
            }
            return data;
        }
    }

    /// <summary>Verifies that we can create a payload stream over an async enumerable of variable size elements.
    /// </summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the payload stream pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Encode_stream_of_variable_size_elements(int size, int yieldThreshold)
    {
        // Arrange
        var expected = Enumerable.Range(0, size).Select(i => $"hello-{i}").ToArray();

        // Act
        PipeReader payload = SliceEncoding.Slice2.CreatePayloadStream(
            GetDataAsync(size),
            encodeOptions: null,
            (ref SliceEncoder encoder, string value) => encoder.EncodeString(value),
            useSegments: true);

        // Assert
        Assert.That(async () => await DecodeDataAsync(payload), Is.EqualTo(expected));

        async IAsyncEnumerable<string> GetDataAsync(int size)
        {
            for (int i = 0; i < size; i++)
            {
                if (yieldThreshold > 0 && i % yieldThreshold == 0)
                {
                    await Task.Yield();
                }
                yield return $"hello-{i}";
            }
        }

        async Task<string[]> DecodeDataAsync(PipeReader payload)
        {
            var data = new List<string>();
            ReadResult readResult;
            do
            {
                readResult = await payload.ReadSegmentAsync(SliceEncoding.Slice2, int.MaxValue - 1, default);
                data.AddRange(DecodeStringStream(readResult.Buffer));
                payload.AdvanceTo(readResult.Buffer.End);
            }
            while (!readResult.IsCompleted);
            return data.ToArray();
        }

        static List<string> DecodeStringStream(ReadOnlySequence<byte> buffer)
        {
            var data = new List<string>();
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
            if (buffer.Length > 0)
            {
                while (decoder.Consumed < buffer.Length)
                {
                    data.Add(decoder.DecodeString());
                }
            }
            return data;
        }
    }

    /// <summary>Verifies that we correctly decode an async enumerable of fixed size elements.</summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the payload stream pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Decode_stream_of_fixed_size_elements(int size, int yieldThreshold)
    {
        // Arrange
        var pipe = new Pipe();
        var request = new IncomingRequest(FakeConnectionContext.IceRpc)
        {
            Payload = pipe.Reader
        };
        int[] expected = Enumerable.Range(0, size).Select(i => i).ToArray();
        Task.Run(() => _ = EncodeDataAsync(pipe.Writer));

        // Act
        IAsyncEnumerable<int> decoded = request.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            SliceFeature.Default,
            (ref SliceDecoder decoder) => decoder.DecodeInt32(),
            elementSize: 4);

        // Assert
        Assert.That(async () => await ToArrayAsync(decoded), Is.EqualTo(expected));

        async Task<int[]> ToArrayAsync(IAsyncEnumerable<int> enumerable)
        {
            var data = new List<int>();
            await foreach (int i in enumerable)
            {
                data.Add(i);
            }
            return data.ToArray();
        }

        async Task EncodeDataAsync(PipeWriter writer)
        {
            for (int i = 0; i < size; i++)
            {
                if (yieldThreshold > 0 && i % yieldThreshold == 0)
                {
                    await writer.FlushAsync();
                    await Task.Yield();
                }
                EncodeElement(i);
            }
            await writer.CompleteAsync();

            void EncodeElement(int value)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
                encoder.EncodeInt32(value);
            }
        }
    }

    /// <summary>Verifies that we correctly decode an async enumerable of variable size elements.</summary>
    /// <param name="size">The size of the async enumerable.</param>
    /// <param name="yieldThreshold">The yield threshold ensures that we test both synchronous and asynchronous
    /// iteration code paths in the payload stream pipe reader.</param>
    [TestCase(0, 0)]
    [TestCase(100, 7)]
    [TestCase(64 * 1024, 0)]
    public void Decode_stream_of_variable_size_elements(int size, int yieldThreshold)
    {
        // Arrange
        var pipe = new Pipe();
        var request = new IncomingRequest(FakeConnectionContext.IceRpc)
        {
            Payload = pipe.Reader
        };
        string[] expected = Enumerable.Range(0, size).Select(i => $"hello-{i}").ToArray();
        Task.Run(() => _ = EncodeDataAsync(pipe.Writer));

        // Act
        IAsyncEnumerable<string> decoded = request.ToAsyncEnumerable(
            SliceEncoding.Slice2,
            defaultActivator: null,
            (ref SliceDecoder decoder) => decoder.DecodeString());

        // Assert
        Assert.That(async () => await ToArrayAsync(decoded), Is.EqualTo(expected));

        async Task<string[]> ToArrayAsync(IAsyncEnumerable<string> enumerable)
        {
            var data = new List<string>();
            await foreach (string i in enumerable)
            {
                data.Add(i);
            }
            return data.ToArray();
        }

        async Task EncodeDataAsync(PipeWriter writer)
        {
            if (size > 0)
            {
                int encodedByteCount = 0;
                Memory<byte> sizePlaceHolder = writer.GetMemory(4)[0..4];
                writer.Advance(4);
                for (int i = 0; i < size; i++)
                {
                    if (encodedByteCount > 0 && yieldThreshold > 0 && i % yieldThreshold == 0)
                    {
                        SliceEncoder.EncodeVarUInt62((ulong)encodedByteCount, sizePlaceHolder.Span);
                        encodedByteCount = 0;
                        await writer.FlushAsync();
                        await Task.Yield();
                        sizePlaceHolder = writer.GetMemory(4)[0..4];
                        writer.Advance(4);
                    }
                    encodedByteCount += EncodeElement($"hello-{i}");
                }
                SliceEncoder.EncodeVarUInt62((ulong)encodedByteCount, sizePlaceHolder.Span);
            }
            await writer.CompleteAsync();

            int EncodeElement(string value)
            {
                var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
                encoder.EncodeString(value);
                return encoder.EncodedByteCount;
            }
        }
    }

    /// <summary>Test that the payload stream of the outgoing request and incoming request are completed with
    /// <see cref="InvalidDataException"/> after the async enumerable decoding action throws
    /// <see cref="InvalidDataException"/>. Even in the case that the exception is throw after the response has been
    /// received.</summary>
    [Test]
    public async Task Stream_payload_completes_with_invalid_data_exception_after_receiving_invalid_data()
    {
        var results = new List<MyEnum>();
        WaitForCompletionPipeReaderDecorator? incomingPayloadStream = null;
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new InlineDispatcher(
                async (request, cancelationToken) =>
                {
                    await request.DecodeEmptyArgsAsync(SliceEncoding.Slice2, cancelationToken);
                    incomingPayloadStream = new WaitForCompletionPipeReaderDecorator(request.Payload);
                    request.Payload = incomingPayloadStream;
                    IAsyncEnumerable<MyEnum> myEnums = request.ToAsyncEnumerable(
                        SliceEncoding.Slice2,
                        null,
                        (ref SliceDecoder decoder) => MyEnumSliceDecoderExtensions.DecodeMyEnum(ref decoder));
                    _ = Task.Run(async () =>
                        {
                            await foreach (var myEnum in myEnums)
                            {
                                results.Add(myEnum);
                            }
                        },
                        CancellationToken.None);

                    return new OutgoingResponse(request);
                }))
            .BuildServiceProvider(validateScopes: true);

        ClientConnection connection = provider.GetRequiredService<ClientConnection>();
        provider.GetRequiredService<Server>().Listen();

        var tcs = new TaskCompletionSource();
        var outgoingPayloadStream = new WaitForCompletionPipeReaderDecorator(
            SliceEncoding.Slice2.CreatePayloadStream(
                GetData(),
                encodeOptions: null,
                (ref SliceEncoder encoder, uint value) => encoder.EncodeVarUInt62(value),
                false));
        var request = new OutgoingRequest(ServiceProxy.DefaultServiceAddress)
        {
            Payload = SliceEncoding.Slice2.CreateSizeZeroPayload(),
            PayloadStream = outgoingPayloadStream,
        };

        IncomingResponse response = await connection.InvokeAsync(request);

        // Act, let's streaming start
        tcs.SetResult();

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(incomingPayloadStream, Is.Not.Null);
        Assert.That(results.Count, Is.Empty);
        Assert.That(async () => await outgoingPayloadStream.Completed, Throws.TypeOf<InvalidDataException>());
        Assert.That(async () => await incomingPayloadStream.Completed, Throws.TypeOf<InvalidDataException>());

        async IAsyncEnumerable<uint> GetData()
        {
            await tcs.Task; // Don't start streaming until we receive the response
            yield return 1;
            yield return 4; // Not valid value for MyEnum
            while (true)
            {
                yield return 1;
            }
        }
    }

    private class WaitForCompletionPipeReaderDecorator : PipeReader
    {
        public Task Completed => _completionTcs.Task;

        private readonly PipeReader _decoratee;
        private readonly TaskCompletionSource _completionTcs = new();

        internal WaitForCompletionPipeReaderDecorator(PipeReader decoratee) => _decoratee = decoratee;

        public override void AdvanceTo(SequencePosition consumed) => _decoratee.AdvanceTo(consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _decoratee.AdvanceTo(consumed, examined);

        public override void CancelPendingRead() => _decoratee.CancelPendingRead();
        public override void Complete(Exception? exception = null)
        {
            if (exception is not null)
            {
                _completionTcs.SetException(exception);
            }
            else
            {
                _completionTcs.SetResult();
            }
            _decoratee.Complete(exception);
        }
        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) =>
            _decoratee.ReadAsync(cancellationToken);
        public override bool TryRead(out ReadResult result) => _decoratee.TryRead(out result);
    }
}
