// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Protobuf.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class AccessTests
{
    /// <summary>Verifies that the generated types are public by default.</summary>
    [TestCase(typeof(MyWidgetClient))]
    [TestCase(typeof(IMyWidget))]
    [TestCase(typeof(IMyWidgetService))]
    public void Generated_types_are_public_by_default(System.Type type) => Assert.That(type.IsPublic, Is.True);

    /// <summary>Verifies that the Access=internal metadata makes the generated types internal, the messages and the
    /// services alike.</summary>
    [TestCase(typeof(InternalWidgetClient))]
    [TestCase(typeof(IInternalWidget))]
    [TestCase(typeof(IInternalWidgetService))]
    [TestCase(typeof(InternalMessage))]
    public void Generated_types_are_internal_with_internal_access(System.Type type) =>
        Assert.That(type.IsPublic, Is.False);
}
