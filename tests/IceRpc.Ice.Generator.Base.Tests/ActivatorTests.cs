// Copyright (c) ZeroC, Inc.

using Ice.Generator.Base.Tests.ReferencedAssemblies;
using IceRpc.Ice.Codec;
using NUnit.Framework;
using System.Reflection;

namespace IceRpc.Ice.Generator.Base.Tests;

public class ActivatorTests
{
    private static IEnumerable<string> ReferencedAssembliesClassTypeIds
    {
        get
        {
            yield return typeof(ClassA).GetIceTypeId()!;
            yield return typeof(ClassB).GetIceTypeId()!;
            yield return typeof(ClassC).GetIceTypeId()!;
            yield return typeof(ClassD).GetIceTypeId()!;
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
            yield return new TestCaseData(typeof(ClassA).Assembly, typeof(ClassA).GetIceTypeId()!, typeof(ClassA));
            yield return new TestCaseData(typeof(ClassB).Assembly, typeof(ClassB).GetIceTypeId()!, typeof(ClassB));
            yield return new TestCaseData(typeof(ClassC).Assembly, typeof(ClassC).GetIceTypeId()!, typeof(ClassC));
            yield return new TestCaseData(typeof(ClassD).Assembly, typeof(ClassD).GetIceTypeId()!, typeof(ClassD));
            yield return new TestCaseData(typeof(CompactClassA).Assembly, "1", typeof(CompactClassA));
            yield return new TestCaseData(typeof(CompactClassB).Assembly, "2", typeof(CompactClassB));
            yield return new TestCaseData(typeof(CompactClassC).Assembly, "3", typeof(CompactClassC));
            yield return new TestCaseData(typeof(CompactClassD).Assembly, "4", typeof(CompactClassD));

            // Loading an assembly also loads its referenced assemblies, here loading the assembly for D instances,
            // should allow create instances for A, B and C variants too.

            yield return new TestCaseData(typeof(ClassD).Assembly, typeof(ClassA).GetIceTypeId()!, typeof(ClassA));
            yield return new TestCaseData(typeof(ClassD).Assembly, typeof(ClassB).GetIceTypeId()!, typeof(ClassB));
            yield return new TestCaseData(typeof(ClassD).Assembly, typeof(ClassC).GetIceTypeId()!, typeof(ClassC));

            yield return new TestCaseData(typeof(CompactClassD).Assembly, "1", typeof(CompactClassA));
            yield return new TestCaseData(typeof(CompactClassD).Assembly, "2", typeof(CompactClassB));
            yield return new TestCaseData(typeof(CompactClassD).Assembly, "3", typeof(CompactClassC));
        }
    }

    public static IEnumerable<TestCaseData> ReferencedAssembliesExceptionTypeIdsWithType
    {
        get
        {
            yield return new TestCaseData(typeof(ExceptionA).Assembly, typeof(ExceptionA).GetIceTypeId()!, typeof(ExceptionA));
            yield return new TestCaseData(typeof(ExceptionB).Assembly, typeof(ExceptionB).GetIceTypeId()!, typeof(ExceptionB));
            yield return new TestCaseData(typeof(ExceptionC).Assembly, typeof(ExceptionC).GetIceTypeId()!, typeof(ExceptionC));
            yield return new TestCaseData(typeof(ExceptionD).Assembly, typeof(ExceptionD).GetIceTypeId()!, typeof(ExceptionD));

            yield return new TestCaseData(typeof(ExceptionD).Assembly, typeof(ExceptionA).GetIceTypeId()!, typeof(ExceptionA));
            yield return new TestCaseData(typeof(ExceptionD).Assembly, typeof(ExceptionB).GetIceTypeId()!, typeof(ExceptionB));
            yield return new TestCaseData(typeof(ExceptionD).Assembly, typeof(ExceptionC).GetIceTypeId()!, typeof(ExceptionC));
        }
    }

    [Test, TestCaseSource(nameof(ReferencedAssembliesClassTypeIds))]
    public void Activator_cannot_create_instances_of_classes_defined_in_unknown_assemblies(string typeId)
    {
        var sut = IActivator.FromAssembly(typeof(IceDecoder).Assembly);
        object? instance = sut.CreateInstance(typeId);

        Assert.That(instance, Is.Null);
    }

    [Test, TestCaseSource(nameof(ReferencedAssembliesClassTypeIds))]
    public void Activator_cannot_create_instances_of_exceptions_defined_in_unknown_assemblies(string typeId)
    {
        var sut = IActivator.FromAssembly(typeof(IceDecoder).Assembly);

        object? instance = sut.CreateInstance(typeId);

        Assert.That(instance, Is.Null);
    }

    [Test, TestCaseSource(nameof(ReferencedAssembliesClassTypeIdsWithType))]
    public void Activator_can_create_instances_of_classes_defined_in_known_assemblies(
        Assembly assembly,
        string typeId,
        Type expectedType)
    {
        var sut = IActivator.FromAssembly(assembly);

        object? instance = sut.CreateInstance(typeId);

        Assert.That(instance, Is.Not.Null);
        Assert.That(instance!.GetType(), Is.EqualTo(expectedType));
    }

    [Test, TestCaseSource(nameof(ReferencedAssembliesExceptionTypeIdsWithType))]
    public void Activator_can_create_instances_of_exceptions_defined_in_known_assemblies(
        Assembly assembly,
        string typeId,
        Type expectedType)
    {
        var decoder = new IceDecoder(ReadOnlyMemory<byte>.Empty);
        var sut = IActivator.FromAssembly(assembly);

        object? instance = sut.CreateInstance(typeId);

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
