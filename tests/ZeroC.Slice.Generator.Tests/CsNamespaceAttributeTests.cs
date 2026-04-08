// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace ZeroC.Slice.Generator.Tests.CustomNamespace.MyNamespace;

[Parallelizable(scope: ParallelScope.All)]
public class CsNamespaceAttributeTests
{
    [Test]
    public void Slice_module_using_cs_identifier_attribute() =>
        Assert.That(typeof(S1).Namespace, Is.EqualTo("ZeroC.Slice.Generator.Tests.CustomNamespace.MyNamespace"));
}
