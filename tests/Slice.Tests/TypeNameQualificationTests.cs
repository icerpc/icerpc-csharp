// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class TypeNameQualificationTests
{
    /// <summary>Verifies that when a type is defined in multiple modules, the generated code doesn't mix up the
    /// type names, and use the correct qualified type names.</summary>
    [Test]
    public void Struct_with_field_type_name_defined_in_multiple_modules()
    {
        var s2 = new S2();

        Assert.That(s2.S.GetType(), Is.EqualTo(typeof(S)));
        Assert.That(s2.InnerS.GetType(), Is.EqualTo(typeof(Inner.S)));
    }
}
