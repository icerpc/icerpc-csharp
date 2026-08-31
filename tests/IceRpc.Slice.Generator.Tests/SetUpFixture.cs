// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Slice.Generator.Tests;

[SetUpFixture]
public sealed class SetUpFixture
{
    [OneTimeSetUp]
    public void OneTimeSetUp() => CommonSetUpFixture.OneTimeSetUp();

    [OneTimeTearDown]
    public void OneTimeTearDown() => CommonSetUpFixture.OneTimeTearDown();
}
