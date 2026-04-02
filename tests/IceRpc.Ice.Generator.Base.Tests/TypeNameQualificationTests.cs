// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Ice.Generator.Base.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class TypeNameQualificationTests
{
    /// <summary>Verifies that when multiple types with the same name are defined in different modules, the generated
    /// code doesn't mix up the type names, and use the correct qualified type names.</summary>
    [Test]
    public void Struct_with_field_type_name_defined_in_other_module()
    {
        var s2 = new S2();

        Assert.That(s2.S.GetType(), Is.EqualTo(typeof(S)));
        Assert.That(s2.InnerS.GetType(), Is.EqualTo(typeof(Inner.S)));
    }
}
