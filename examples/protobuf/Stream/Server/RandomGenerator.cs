// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc.Features;
using IceRpc.Protobuf;
using StreamExample;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace StreamServer;

[ProtobufService]
internal partial class RandomGenerator : IGeneratorService
{
    public ValueTask<IAsyncEnumerable<GenerateResponse>> GenerateNumbersAsync(
        Empty args,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        return new(GetRandomNumbersAsync(default));

        // The EnumeratorCancellation attribute is required to allow the IceRPC runtime to cancel the asynchronous
        // iteration. The runtime cancels the iteration once the client stops iterating over the enumerable it received.
        static async IAsyncEnumerable<GenerateResponse> GetRandomNumbersAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Console.WriteLine("Starting to stream random numbers to the client...");
            while (true)
            {
                yield return new GenerateResponse
                {
                    Value = RandomNumberGenerator.GetInt32(int.MaxValue)
                };

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("The client stopped reading random numbers from the stream.");
                    yield break;
                }
            }
        }
    }
}
