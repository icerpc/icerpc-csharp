// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Tests.Common;

public sealed class CommonSetUpFixture
{
    private static readonly EventHandler<UnobservedTaskExceptionEventArgs> _handler = HandleUnobservedTaskException;

    // Ignore UTE when the stack trace starts with
    private static readonly string[] _ignoreList = new string[]
    {
        "at System.Net.Quic.QuicConnection.HandleEventShutdownComplete(",
        "at System.Net.Quic.QuicConnection.HandleEventShutdownInitiatedByPeer(",
        "at System.Net.Quic.QuicConnection.HandleEventShutdownInitiatedByTransport(",
        "at System.Net.Quic.QuicListener.DisposeAsync(",
        "at System.Net.Quic.ResettableValueTaskSource.TryComplete(",
        "at System.Net.Quic.ValueTaskSource.TryComplete("
    };

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
                Console.Error.WriteLine($"\n+++ Unobserved task exception {sender}:\n{exception}");
            }
        }
    }

    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Exception exception = e.Exception.InnerException ?? e.Exception;
        string stackTrace = exception.StackTrace?.TrimStart() ?? "";
        if (!_ignoreList.Any(item => stackTrace.StartsWith(item, StringComparison.Ordinal)))
        {
            _unobservedTaskExceptions.Add((sender, exception));
        }
    }
}
