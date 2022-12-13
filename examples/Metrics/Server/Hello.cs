// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    private volatile int _totalRequests;
    private bool _hasOutput;

    private void OutputRequestInformation()
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

    public ValueTask SayHelloAsync(IFeatureCollection features, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _totalRequests);
        if (_totalRequests % 1000 == 0)
        {
            // Update the output
            OutputRequestInformation();
        }
        return default;
    }
}
