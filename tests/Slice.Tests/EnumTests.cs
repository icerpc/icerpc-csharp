// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace Slice.Tests;

public class EnumTests
{
    [TestCase((int)MyEnum.Enum1, 0)]
    [TestCase((int)MyEnum.Enum2, 1)]
    [TestCase((int)MyEnum.Enum3, 2)]
    [TestCase((short)MyFixedLengthEnum.SEnum1, 0)]
    [TestCase((short)MyFixedLengthEnum.SEnum2, 1)]
    [TestCase((short)MyFixedLengthEnum.SEnum3, 2)]
    [TestCase((int)MyVarSizeEnum.Enum1, -10)]
    [TestCase((int)MyVarSizeEnum.Enum2, 20)]
    [TestCase((int)MyVarSizeEnum.Enum3, 30)]
    [TestCase((uint)MyUncheckedEnum.E0, 1)]
    [TestCase((uint)MyUncheckedEnum.E4, 16)]
    public void Enumerator_has_the_expected_value(object value, object expectedValue) =>
        Assert.That(value, Is.EqualTo(expectedValue));

    [TestCase(-10, MyVarSizeEnum.Enum1)]
    [TestCase(30, MyVarSizeEnum.Enum3)]
    public void As_enum_for_an_enum_with_non_contiguous_enumerators(int value, MyVarSizeEnum expected) =>
        Assert.That(
            value.AsMyVarSizeEnum(),
            Is.EqualTo(expected));

    [TestCase(-30)]
    [TestCase(40)]
    public void As_enum_for_an_enum_with_non_contiguous_enumerators_fails_for_invalid_value(int value) =>
        Assert.That(
            () => value.AsMyVarSizeEnum(),
            Throws.TypeOf<InvalidDataException>());

    [TestCase(0, MyEnum.Enum1)]
    [TestCase(2, MyEnum.Enum3)]
    public void As_enum_for_an_enum_with_contiguous_enumerators(int value, MyEnum expected) =>
        Assert.That(value.AsMyEnum(), Is.EqualTo(expected));

    [TestCase(-11)]
    [TestCase(3)]
    public void As_enum_for_an_enum_with_contiguous_enumerators_fails_for_invalid_values(int value) =>
       Assert.That(() => value.AsMyEnum(), Throws.TypeOf<InvalidDataException>());

    [TestCase(sizeof(MyFixedLengthEnum), sizeof(short))]
    [TestCase(sizeof(MyUncheckedEnum), sizeof(uint))]
    public void Enum_has_the_expected_size(int size, int expectedSize) =>
        Assert.That(size, Is.EqualTo(expectedSize));

    [Test]
    public void Cs_attribute_on_enumerator()
    {
        // Arrange / Act
        var memberInfos = typeof(MyUncheckedEnum).GetMember("E4");
        var attributes = memberInfos[0].GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
        var description = ((System.ComponentModel.DescriptionAttribute)attributes[0]).Description;

        // Assert
        Assert.That(description, Is.EqualTo("Sixteen"));
    }
}
