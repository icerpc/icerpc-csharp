// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.ReferencedAssemblies;
using NUnit.Framework;
using System.Reflection;

namespace IceRpc.Slice.Tests;

public class ActivatorTests
{
    public static IEnumerable<string> ReferencedAssembliesTypeIds
    {
        get
        {
            yield return ClassA.SliceTypeId;
            yield return ClassB.SliceTypeId;
            yield return ClassC.SliceTypeId;
            yield return ClassD.SliceTypeId;
            yield return "1";
            yield return "2";
            yield return "3";
            yield return "4";
            yield return ExceptionA.SliceTypeId;
            yield return ExceptionB.SliceTypeId;
            yield return ExceptionC.SliceTypeId;
            yield return ExceptionD.SliceTypeId;
        }
    }

    public static IEnumerable<TestCaseData> ReferencedAssembliesTypeIdsWithType
    {
        get
        {
            yield return new TestCaseData(typeof(ClassA).Assembly, ClassA.SliceTypeId, typeof(ClassA));
            yield return new TestCaseData(typeof(ClassB).Assembly, ClassB.SliceTypeId, typeof(ClassB));
            yield return new TestCaseData(typeof(ClassC).Assembly, ClassC.SliceTypeId, typeof(ClassC));
            yield return new TestCaseData(typeof(ClassD).Assembly, ClassD.SliceTypeId, typeof(ClassD));
            yield return new TestCaseData(typeof(CompactClassA).Assembly, "1", typeof(CompactClassA));
            yield return new TestCaseData(typeof(CompactClassB).Assembly, "2", typeof(CompactClassB));
            yield return new TestCaseData(typeof(CompactClassC).Assembly, "3", typeof(CompactClassC));
            yield return new TestCaseData(typeof(CompactClassD).Assembly, "4", typeof(CompactClassD));
            yield return new TestCaseData(typeof(ExceptionA).Assembly, ExceptionA.SliceTypeId, typeof(ExceptionA));
            yield return new TestCaseData(typeof(ExceptionB).Assembly, ExceptionB.SliceTypeId, typeof(ExceptionB));
            yield return new TestCaseData(typeof(ExceptionC).Assembly, ExceptionC.SliceTypeId, typeof(ExceptionC));
            yield return new TestCaseData(typeof(ExceptionD).Assembly, ExceptionD.SliceTypeId, typeof(ExceptionD));

            // Loading an assembly also loads its referenced assemblies, here loading the assembly for D instances,
            // should allow create instances for A, B and C variants too.

            yield return new TestCaseData(typeof(ClassD).Assembly, ClassA.SliceTypeId, typeof(ClassA));
            yield return new TestCaseData(typeof(ClassD).Assembly, ClassB.SliceTypeId, typeof(ClassB));
            yield return new TestCaseData(typeof(ClassD).Assembly, ClassC.SliceTypeId, typeof(ClassC));

            yield return new TestCaseData(typeof(CompactClassD).Assembly, "1", typeof(CompactClassA));
            yield return new TestCaseData(typeof(CompactClassD).Assembly, "2", typeof(CompactClassB));
            yield return new TestCaseData(typeof(CompactClassD).Assembly, "3", typeof(CompactClassC));

            yield return new TestCaseData(typeof(ExceptionD).Assembly, ExceptionA.SliceTypeId, typeof(ExceptionA));
            yield return new TestCaseData(typeof(ExceptionD).Assembly, ExceptionB.SliceTypeId, typeof(ExceptionB));
            yield return new TestCaseData(typeof(ExceptionD).Assembly, ExceptionC.SliceTypeId, typeof(ExceptionC));
        }
    }

    [Test, TestCaseSource(nameof(ReferencedAssembliesTypeIds))]
    public void Activator_cannot_create_instances_of_types_defined_in_unknown_assemblies(string typeId)
    {
        var decoder = new SliceDecoder(ReadOnlyMemory<byte>.Empty, SliceEncoding.Slice1);
        IActivator sut = SliceDecoder.GetActivator(typeof(SliceDecoder).Assembly);

        object? instance = sut.CreateInstance(typeId, ref decoder);

        Assert.That(instance, Is.Null);
    }

    [Test, TestCaseSource(nameof(ReferencedAssembliesTypeIdsWithType))]
    public void Activator_can_create_instances_of_types_defined_in_known_assemblies(
        Assembly assembly,
        string typeId,
        Type expectedType)
    {
        var decoder = new SliceDecoder(ReadOnlyMemory<byte>.Empty, SliceEncoding.Slice1);
        IActivator sut = SliceDecoder.GetActivator(assembly);

        object? instance = sut.CreateInstance(typeId, ref decoder);

        Assert.That(instance, Is.Not.Null);
        Assert.That(instance.GetType(), Is.EqualTo(expectedType));
    }
}
