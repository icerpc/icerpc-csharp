// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Tests.ReferencedAssemblies;
using NUnit.Framework;
using System.Reflection;

namespace IceRpc.Tests.Slice;

public class ActivatorTests
{
    private static IEnumerable<string> ReferencedAssembliesClassTypeIds
    {
        get
        {
            yield return typeof(ClassA).GetSliceTypeId()!;
            yield return typeof(ClassB).GetSliceTypeId()!;
            yield return typeof(ClassC).GetSliceTypeId()!;
            yield return typeof(ClassD).GetSliceTypeId()!;
            yield return "1";
            yield return "2";
            yield return "3";
            yield return "4";
        }
    }
    public static IEnumerable<TestCaseData> ReferencedAssembliesClassTypeIdsWithType
    {
        get
        {
            yield return new TestCaseData(typeof(ClassA).Assembly, typeof(ClassA).GetSliceTypeId()!, typeof(ClassA));
            yield return new TestCaseData(typeof(ClassB).Assembly, typeof(ClassB).GetSliceTypeId()!, typeof(ClassB));
            yield return new TestCaseData(typeof(ClassC).Assembly, typeof(ClassC).GetSliceTypeId()!, typeof(ClassC));
            yield return new TestCaseData(typeof(ClassD).Assembly, typeof(ClassD).GetSliceTypeId()!, typeof(ClassD));
            yield return new TestCaseData(typeof(CompactClassA).Assembly, "1", typeof(CompactClassA));
            yield return new TestCaseData(typeof(CompactClassB).Assembly, "2", typeof(CompactClassB));
            yield return new TestCaseData(typeof(CompactClassC).Assembly, "3", typeof(CompactClassC));
            yield return new TestCaseData(typeof(CompactClassD).Assembly, "4", typeof(CompactClassD));

            // Loading an assembly also loads its referenced assemblies, here loading the assembly for D instances,
            // should allow create instances for A, B and C variants too.

            yield return new TestCaseData(typeof(ClassD).Assembly, typeof(ClassA).GetSliceTypeId()!, typeof(ClassA));
            yield return new TestCaseData(typeof(ClassD).Assembly, typeof(ClassB).GetSliceTypeId()!, typeof(ClassB));
            yield return new TestCaseData(typeof(ClassD).Assembly, typeof(ClassC).GetSliceTypeId()!, typeof(ClassC));

            yield return new TestCaseData(typeof(CompactClassD).Assembly, "1", typeof(CompactClassA));
            yield return new TestCaseData(typeof(CompactClassD).Assembly, "2", typeof(CompactClassB));
            yield return new TestCaseData(typeof(CompactClassD).Assembly, "3", typeof(CompactClassC));
        }
    }

    public static IEnumerable<TestCaseData> ReferencedAssembliesExceptionTypeIdsWithType
    {
        get
        {
            yield return new TestCaseData(typeof(ExceptionA).Assembly, typeof(ExceptionA).GetSliceTypeId()!, typeof(ExceptionA));
            yield return new TestCaseData(typeof(ExceptionB).Assembly, typeof(ExceptionB).GetSliceTypeId()!, typeof(ExceptionB));
            yield return new TestCaseData(typeof(ExceptionC).Assembly, typeof(ExceptionC).GetSliceTypeId()!, typeof(ExceptionC));
            yield return new TestCaseData(typeof(ExceptionD).Assembly, typeof(ExceptionD).GetSliceTypeId()!, typeof(ExceptionD));

            yield return new TestCaseData(typeof(ExceptionD).Assembly, typeof(ExceptionA).GetSliceTypeId()!, typeof(ExceptionA));
            yield return new TestCaseData(typeof(ExceptionD).Assembly, typeof(ExceptionB).GetSliceTypeId()!, typeof(ExceptionB));
            yield return new TestCaseData(typeof(ExceptionD).Assembly, typeof(ExceptionC).GetSliceTypeId()!, typeof(ExceptionC));
        }
    }

    [Test, TestCaseSource(nameof(ReferencedAssembliesClassTypeIds))]
    public void Activator_cannot_create_instances_of_classes_defined_in_unknown_assemblies(string typeId)
    {
        var decoder = new SliceDecoder(ReadOnlyMemory<byte>.Empty, SliceEncoding.Slice1);
        var sut = IActivator.FromAssembly(typeof(SliceDecoder).Assembly);

        object? instance = sut.CreateClassInstance(typeId, ref decoder);

        Assert.That(instance, Is.Null);
    }

    [Test, TestCaseSource(nameof(ReferencedAssembliesClassTypeIds))]
    public void Activator_cannot_create_instances_of_exceptions_defined_in_unknown_assemblies(string typeId)
    {
        var decoder = new SliceDecoder(ReadOnlyMemory<byte>.Empty, SliceEncoding.Slice1);
        var sut = IActivator.FromAssembly(typeof(SliceDecoder).Assembly);

        object? instance = sut.CreateExceptionInstance(typeId, ref decoder, message: null);

        Assert.That(instance, Is.Null);
    }

    [Test, TestCaseSource(nameof(ReferencedAssembliesClassTypeIdsWithType))]
    public void Activator_can_create_instances_of_classes_defined_in_known_assemblies(
        Assembly assembly,
        string typeId,
        Type expectedType)
    {
        var decoder = new SliceDecoder(ReadOnlyMemory<byte>.Empty, SliceEncoding.Slice1);
        var sut = IActivator.FromAssembly(assembly);

        object? instance = sut.CreateClassInstance(typeId, ref decoder);

        Assert.That(instance, Is.Not.Null);
        Assert.That(instance!.GetType(), Is.EqualTo(expectedType));
    }

    [Test, TestCaseSource(nameof(ReferencedAssembliesExceptionTypeIdsWithType))]
    public void Activator_can_create_instances_of_exceptions_defined_in_known_assemblies(
        Assembly assembly,
        string typeId,
        Type expectedType)
    {
        var decoder = new SliceDecoder(ReadOnlyMemory<byte>.Empty, SliceEncoding.Slice1);
        var sut = IActivator.FromAssembly(assembly);

        object? instance = sut.CreateExceptionInstance(typeId, ref decoder, message: null);

        Assert.That(instance, Is.Not.Null);
        Assert.That(instance!.GetType(), Is.EqualTo(expectedType));
    }

    [TestCase(typeof(ClassB), typeof(ClassA))]
    [TestCase(typeof(ClassC))]
    [TestCase(typeof(ClassD), typeof(ClassDPrime))]
    public void Create_activator_from_assemblies(params Type[] types) =>
        Assert.That(
            () => IActivator.FromAssemblies(types.Select(type => type.Assembly).ToArray()),
            Throws.Nothing);
}
