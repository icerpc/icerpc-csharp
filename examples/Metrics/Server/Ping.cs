// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using System.Threading;

namespace Demo;

public class Metrics : Service, IMetrics, IDisposable
{
    private volatile int _totalRequests = 0;
    private int _requestsInLastPeriod = 0;

    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(5));

    public Metrics()
    {
        _ = Task.Run(async () =>
        {
            bool hasOutput = false;
            while (await _timer.WaitForNextTickAsync())
            {
                if (hasOutput)
                {
                    Console.SetCursorPosition(0, Console.CursorTop - 2);
                }
                else
                {
                    hasOutput = true;
                }
                Console.WriteLine("{0,-60}: {1}", "Total received `SayHelloAsync` dispatches", _totalRequests);
                Console.WriteLine("{0,-60}: {1}", "Number of received `SayHelloAsync` dispatches in last 5s",
                    _requestsInLastPeriod);
                _requestsInLastPeriod = 0;
            }
        });
    }

    public ValueTask PingAsync(IFeatureCollection features, CancellationToken cancel)
    {
        Interlocked.Increment(ref _totalRequests);
        Interlocked.Increment(ref _requestsInLastPeriod);
        return default;
    }

    public void Dispose() => _timer.Dispose();
}
