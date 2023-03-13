// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace MetricsExample;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Hello'.</summary>
internal class Chatbot : Service, IHelloService
{
    private volatile int _totalRequests;
    private bool _hasOutput;

    public ValueTask<string> SayHelloAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        int totalRequests = Interlocked.Increment(ref _totalRequests);
        if (totalRequests % 1000 == 0)
        {
            if (_hasOutput)
            {
                Console.SetCursorPosition(0, Console.CursorTop - 1);
            }
            else
            {
                _hasOutput = true;
            }
            Console.WriteLine("{0,-30}: {1}", "Total `SayHelloAsync` dispatches", _totalRequests);
        }
        return new($"Hello, {name}!");
    }
}
