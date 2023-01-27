// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Tests.Common;

public sealed class CommonSetUpFixture
{
    private static readonly EventHandler<UnobservedTaskExceptionEventArgs> _handler = HandleUnobservedTaskException;
    private static readonly List<(object?, Exception)> _unobservedTaskExceptions = new();

    public static void OneTimeSetUp()
    {
        AssertTraceListener.Setup();
        TaskScheduler.UnobservedTaskException += _handler;
    }

    public static void OneTimeTearDown()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        TaskScheduler.UnobservedTaskException -= _handler;

        if (_unobservedTaskExceptions.Count > 0)
        {
            Console.Error.WriteLine($"Tests triggered {_unobservedTaskExceptions.Count} unobserved task exceptions");
            foreach ((object? sender, Exception exception) in _unobservedTaskExceptions)
            {
                Console.Error.WriteLine($"Unobserved task exception {sender}:\n{exception}");
            }
        }
    }

    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        _unobservedTaskExceptions.Add((sender, e.Exception.InnerException ?? e.Exception));
}
