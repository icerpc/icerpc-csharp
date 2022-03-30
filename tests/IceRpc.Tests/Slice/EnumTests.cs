// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Slice.Tests;

public class EnumTests
{
    [TestCase((int)MyEnum.enum1, 0)]
    [TestCase((int)MyEnum.enum2, 1)]
    [TestCase((int)MyEnum.enum3, 2)]
    [TestCase((short)MyFixedLengthEnum.senum1, 0)]
    [TestCase((short)MyFixedLengthEnum.senum2, 1)]
    [TestCase((short)MyFixedLengthEnum.senum3, 2)]
    [TestCase((int)MyEnumWithCustomEnumerators.enum1, -10)]
    [TestCase((int)MyEnumWithCustomEnumerators.enum2, 20)]
    [TestCase((int)MyEnumWithCustomEnumerators.enum3, 30)]
    [TestCase((uint)MyUncheckedEnum.E0, 1)]
    [TestCase((uint)MyUncheckedEnum.E4, 16)]
    public void Enumerator_has_the_expected_value(object value, object expectedValue) =>
        Assert.That(value, Is.EqualTo(expectedValue));

    [TestCase(-10, MyEnumWithCustomEnumerators.enum1)]
    [TestCase(30, MyEnumWithCustomEnumerators.enum3)]
    public void As_enum_for_an_enum_with_non_contiguous_enumerators(int value, MyEnumWithCustomEnumerators expected) =>
        Assert.That(
            IntMyEnumWithCustomEnumeratorsExtensions.AsMyEnumWithCustomEnumerators(value),
            Is.EqualTo(expected));

    [TestCase(-30)]
    [TestCase(40)]
    public void As_enum_for_an_enum_with_non_contiguous_enumerators_fails_for_invalid_value(int value) =>
        Assert.That(
            () => IntMyEnumWithCustomEnumeratorsExtensions.AsMyEnumWithCustomEnumerators(value),
            Throws.TypeOf<InvalidDataException>());

    [TestCase(0, MyEnum.enum1)]
    [TestCase(2, MyEnum.enum3)]
    public void As_enum_for_an_enum_with_contiguous_enumerators(int value, MyEnum expected) =>
        Assert.That(IntMyEnumExtensions.AsMyEnum(value), Is.EqualTo(expected));

    [TestCase(-11)]
    [TestCase(3)]
    public void As_enum_for_an_enum_with_contiguous_enumerators_fails_for_invalid_values(int value) =>
       Assert.That(() => IntMyEnumExtensions.AsMyEnum(value), Throws.TypeOf<InvalidDataException>());

    [TestCase(sizeof(MyFixedLengthEnum), sizeof(short))]
    [TestCase(sizeof(MyUncheckedEnum), sizeof(uint))]
    public void Enum_has_the_expected_size(int size, int expectedSize) =>
        Assert.That(size, Is.EqualTo(expectedSize));
}
