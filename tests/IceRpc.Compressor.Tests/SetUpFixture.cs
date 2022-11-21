// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Tests;

[SetUpFixture]
public sealed class SetUpFixture
{
    [OneTimeSetUp]
    public void OneTimeSetup() => AssertTraceListener.Setup();
}
