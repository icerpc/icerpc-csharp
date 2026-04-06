// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Ice.Generator.Base.Tests;

public class DefaultValueTests
{
    [Test]
    public void Struct_field_has_expected_default_value()
    {
        var p = new Point();
        Assert.That(p.X, Is.EqualTo(0));
        Assert.That(p.Y, Is.EqualTo(42));
    }

    [Test]
    public void Struct_fields_have_expected_default_values()
    {
        var s = new StructWithDefaultValues { NoDefault = "hello" };

        Assert.That(s.BoolFalse, Is.False);
        Assert.That(s.BoolTrue, Is.True);
        Assert.That(s.B, Is.EqualTo((byte)254));
        Assert.That(s.S, Is.EqualTo((short)16000));
        Assert.That(s.I, Is.EqualTo(3));
        Assert.That(s.L, Is.EqualTo(4L));
        Assert.That(s.F, Is.EqualTo(5.1F));
        Assert.That(s.D, Is.EqualTo(6.2D));
        Assert.That(s.Str, Does.StartWith("foo"));
        Assert.That(s.Str, Does.Contain("\\"));
        Assert.That(s.Str, Does.Contain("\n"));
        Assert.That(s.C1, Is.EqualTo(Color.Red));
        Assert.That(s.C2, Is.EqualTo(Color.Green));
        Assert.That(s.C3, Is.EqualTo(Color.Blue));
        Assert.That(s.Nc1, Is.EqualTo(Nested.Color.Red));
        Assert.That(s.Nc2, Is.EqualTo(Nested.Color.Green));
        Assert.That(s.Nc3, Is.EqualTo(Nested.Color.Blue));
        Assert.That(s.NoDefault, Is.EqualTo("hello"));
        Assert.That(s.ZeroI, Is.EqualTo(0));
        Assert.That(s.ZeroL, Is.EqualTo(0L));
        Assert.That(s.ZeroF, Is.EqualTo(0F));
        Assert.That(s.ZeroDotF, Is.EqualTo(0F));
        Assert.That(s.ZeroD, Is.EqualTo(0D));
        Assert.That(s.ZeroDotD, Is.EqualTo(0D));
    }

    [Test]
    public void Class_fields_have_expected_default_values()
    {
        var c = new BaseWithDefaultValues { NoDefault = "hello" };

        Assert.That(c.BoolFalse, Is.False);
        Assert.That(c.BoolTrue, Is.True);
        Assert.That(c.B, Is.EqualTo((byte)1));
        Assert.That(c.S, Is.EqualTo((short)2));
        Assert.That(c.I, Is.EqualTo(3));
        Assert.That(c.L, Is.EqualTo(4L));
        Assert.That(c.F, Is.EqualTo(5.1F));
        Assert.That(c.D, Is.EqualTo(6.2D));
        Assert.That(c.Str, Does.StartWith("foo"));
        Assert.That(c.Str, Does.Contain("\\"));
        Assert.That(c.Str, Does.Contain("\n"));
        Assert.That(c.NoDefault, Is.EqualTo("hello"));
        Assert.That(c.ZeroI, Is.EqualTo(0));
        Assert.That(c.ZeroL, Is.EqualTo(0L));
        Assert.That(c.ZeroF, Is.EqualTo(0F));
        Assert.That(c.ZeroDotF, Is.EqualTo(0F));
        Assert.That(c.ZeroD, Is.EqualTo(0D));
        Assert.That(c.ZeroDotD, Is.EqualTo(0D));

        // St1 has no default value in Ice, so it's default(StructWithDefaultValues) not new StructWithDefaultValues().
        Assert.That(c.St1, Is.EqualTo(default(StructWithDefaultValues)));
        Assert.That(c.St1, Is.Not.EqualTo(new StructWithDefaultValues { NoDefault = "" }));
    }

    [Test]
    public void Derived_class_fields_have_expected_default_values()
    {
        var d = new DerivedWithDefaultValues { NoDefault = "hello" };

        // Base class fields
        Assert.That(d.BoolTrue, Is.True);
        Assert.That(d.I, Is.EqualTo(3));

        // Derived class enum fields
        Assert.That(d.C1, Is.EqualTo(Color.Red));
        Assert.That(d.C2, Is.EqualTo(Color.Green));
        Assert.That(d.C3, Is.EqualTo(Color.Blue));
        Assert.That(d.Nc1, Is.EqualTo(Nested.Color.Red));
        Assert.That(d.Nc2, Is.EqualTo(Nested.Color.Green));
        Assert.That(d.Nc3, Is.EqualTo(Nested.Color.Blue));
    }

    [Test]
    public void Exception_fields_have_expected_default_values()
    {
        var e = new BaseExceptionWithDefaultValues { NoDefault = "hello" };

        Assert.That(e.BoolFalse, Is.False);
        Assert.That(e.BoolTrue, Is.True);
        Assert.That(e.B, Is.EqualTo((byte)1));
        Assert.That(e.S, Is.EqualTo((short)2));
        Assert.That(e.I, Is.EqualTo(3));
        Assert.That(e.L, Is.EqualTo(4L));
        Assert.That(e.F, Is.EqualTo(5.1F));
        Assert.That(e.D, Is.EqualTo(6.2D));
        Assert.That(e.Str, Does.StartWith("foo"));
        Assert.That(e.Str, Does.Contain("\\"));
        Assert.That(e.Str, Does.Contain("\n"));
        Assert.That(e.NoDefault, Is.EqualTo("hello"));
    }

    [Test]
    public void Derived_exception_fields_default_to_const_values()
    {
        var e = new DerivedExceptionWithDefaultValues { NoDefault = "hello" };

        Assert.That(e.C1, Is.EqualTo(ConstColor1.Value));
        Assert.That(e.C2, Is.EqualTo(ConstColor2.Value));
        Assert.That(e.C3, Is.EqualTo(ConstColor3.Value));
        Assert.That(e.Nc1, Is.EqualTo(ConstNestedColor1.Value));
        Assert.That(e.Nc2, Is.EqualTo(ConstNestedColor2.Value));
        Assert.That(e.Nc3, Is.EqualTo(ConstNestedColor3.Value));
    }

    [Test]
    public void Struct_fields_default_to_const_values()
    {
        var s = new StructWithConstDefaultValues();

        Assert.That(s.BoolTrue, Is.EqualTo(ConstBool.Value));
        Assert.That(s.B, Is.EqualTo(ConstByte.Value));
        Assert.That(s.S, Is.EqualTo(ConstShort.Value));
        Assert.That(s.I, Is.EqualTo(ConstInt.Value));
        Assert.That(s.L, Is.EqualTo(ConstLong.Value));
        Assert.That(s.F, Is.EqualTo(ConstFloat.Value));
        Assert.That(s.D, Is.EqualTo(ConstDouble.Value));
        Assert.That(s.Str, Is.EqualTo(ConstString.Value));
        Assert.That(s.C1, Is.EqualTo(ConstColor1.Value));
        Assert.That(s.C2, Is.EqualTo(ConstColor2.Value));
        Assert.That(s.C3, Is.EqualTo(ConstColor3.Value));
        Assert.That(s.Nc1, Is.EqualTo(ConstNestedColor1.Value));
        Assert.That(s.Nc2, Is.EqualTo(ConstNestedColor2.Value));
        Assert.That(s.Nc3, Is.EqualTo(ConstNestedColor3.Value));
        Assert.That(s.ZeroI, Is.EqualTo(ConstZeroI.Value));
        Assert.That(s.ZeroL, Is.EqualTo(ConstZeroL.Value));
        Assert.That(s.ZeroF, Is.EqualTo(ConstZeroF.Value));
        Assert.That(s.ZeroDotF, Is.EqualTo(ConstZeroDotF.Value));
        Assert.That(s.ZeroD, Is.EqualTo(ConstZeroD.Value));
        Assert.That(s.ZeroDotD, Is.EqualTo(ConstZeroDotD.Value));
    }
}
