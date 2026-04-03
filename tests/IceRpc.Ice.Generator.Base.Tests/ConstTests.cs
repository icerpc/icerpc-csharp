// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Ice.Generator.Base.Tests;

public class ConstTests
{
    [TestCase(ConstBool.Value, true)]
    [TestCase(ConstInt.Value, 3)]
    [TestCase(ConstLong.Value, 4L)]
    [TestCase(ConstFloat.Value, 5.1F)]
    [TestCase(ConstDouble.Value, 6.2D)]
    public void Const_has_the_expected_value(object value, object expected) =>
        Assert.That(value, Is.EqualTo(expected));

    [Test]
    public void Byte_const_has_the_expected_value() =>
        Assert.That(ConstByte.Value, Is.EqualTo((byte)254));

    [Test]
    public void Short_const_has_the_expected_value() =>
        Assert.That(ConstShort.Value, Is.EqualTo((short)16000));

    [Test]
    public void String_const_preserves_escape_sequences()
    {
        Assert.That(ConstString.Value, Does.StartWith("foo"));
        Assert.That(ConstString.Value, Does.Contain("\\"));   // literal backslash
        Assert.That(ConstString.Value, Does.Contain("\""));   // literal double-quote
        Assert.That(ConstString.Value, Does.Contain("\n"));   // newline
        Assert.That(ConstString.Value, Does.Contain("\t"));   // tab
        Assert.That(ConstString.Value, Does.Contain("\a"));   // bell (from \007 and \x07)
    }

    [TestCase(ConstColor1.Value, Color.Red)]
    [TestCase(ConstColor2.Value, Color.Green)]
    [TestCase(ConstColor3.Value, Color.Blue)]
    public void Enum_const_has_the_expected_value(Color value, Color expected) =>
        Assert.That(value, Is.EqualTo(expected));

    [TestCase(ConstNestedColor1.Value, Nested.Color.Red)]
    [TestCase(ConstNestedColor2.Value, Nested.Color.Green)]
    [TestCase(ConstNestedColor3.Value, Nested.Color.Blue)]
    public void Nested_enum_const_has_the_expected_value(Nested.Color value, Nested.Color expected) =>
        Assert.That(value, Is.EqualTo(expected));

    [Test]
    public void Alias_const_has_the_same_value_as_referenced_const()
    {
        Assert.That(ConstFloatAlias.Value, Is.EqualTo((double)ConstFloat.Value));
        Assert.That(ConstColor2Alias.Value, Is.EqualTo(ConstColor2.Value));
        Assert.That(ConstNestedColor2Alias.Value, Is.EqualTo(ConstNestedColor2.Value));
    }
}
