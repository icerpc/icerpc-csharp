// Copyright (c) ZeroC, Inc.

using NUnit.Framework;
using System.Diagnostics;

namespace IceRpc.Tests.Common;

/// <summary>A <see cref="TraceListener"/> implementation that calls <see cref="Assert.Fail(string?)"/> upon
/// failure</summary>
public class AssertTraceListener : DefaultTraceListener
{
    private static readonly AssertTraceListener _instance = new();

    /// <summary>Delegate to <see cref="Assert.Fail(string?)"/></summary>
    public override void Fail(string? message) => Assert.Fail(message);

    /// <summary>Delegate to <see cref="Assert.Fail(string?, object?[])"/></summary>
    public override void Fail(string? message, string? detailMessage)
    {
        if (detailMessage is null || detailMessage.Length == 0)
        {
            Assert.Fail(message);
        }
        else
        {
            Assert.Fail(message, detailMessage);
        }
    }

    /// <summary>Sets the AssertTraceListener singleton as the default trace listener.</summary>
    public static void Setup() => Trace.Listeners[0] = _instance;
}
