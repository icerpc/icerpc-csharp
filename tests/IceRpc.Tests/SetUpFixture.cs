// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Tests;

[SetUpFixture]
public sealed class SetUpFixture
{
    // private static readonly EventHandler<UnobservedTaskExceptionEventArgs> _handler = HandleUnobservedTaskException;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        AssertTraceListener.Setup();
       // TaskScheduler.UnobservedTaskException += _handler;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        // TaskScheduler.UnobservedTaskException -= _handler;
    }

   // private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
   //     Assert.Fail($"Unobserved task exception {sender}\n: {e.Exception.InnerException}");
}
