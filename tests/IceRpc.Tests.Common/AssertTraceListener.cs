// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System.Diagnostics;

namespace IceRpc.Tests.Common;

public class AssertTraceListener : DefaultTraceListener
{
    private static readonly AssertTraceListener _instance = new();

    public override void Fail(string? message) => Assert.Fail(message);

    public override void Fail(string? message, string? detailMessage)
    {
        Console.WriteLine($"message = {message}, detailMessage = {detailMessage}");

        if (detailMessage is null || detailMessage.Length == 0)
        {
            Assert.Fail(message);
        }
        else
        {
            Assert.Fail(message, detailMessage);
        }
    }

    public static void Setup() => Trace.Listeners[0] = _instance;
}
