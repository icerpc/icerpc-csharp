// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using System.Threading;

namespace Demo;

public class Hello : Service, IHello, IDisposable
{
    volatile private int _totalRequests = 0;
    volatile private int _requestsInLastTimeSpan = 0;

    private readonly PeriodicTimer _timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

    public Hello()
    {
        _ = Task.Run(async () =>
        {
            var firstOutput = true;
            while (await _timer.WaitForNextTickAsync())
            {
                if (!firstOutput)
                {
                    Console.SetCursorPosition(0, Console.CursorTop - 2);
                }
                else
                {
                    firstOutput = false;
                }
                Console.WriteLine("{0,-60}: {1}", "Total received `SayHelloAsync` invocations", _totalRequests);
                Console.WriteLine("{0,-60}: {1}", "Number of received `SayHelloAsync` invocations in last 5s", _requestsInLastTimeSpan);
                _requestsInLastTimeSpan = 0;
            }
        });
    }

    public ValueTask SayHelloAsync(IFeatureCollection features, CancellationToken cancel)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _requestsInLastTimeSpan);
        return default;
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
