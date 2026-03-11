// Copyright (c) ZeroC, Inc.

module ZeroC::Ice::CodeGen::Tests::TypeIdAttributeTestNamespace
{
    class MyClass {}

    // This construct doesn't use the same case convention as its corresponding C# generated type,
    // but its type ID shouldn't be affected by case normalization of the generated types.
    class myOtherClass {}

    class MyDerivedClass : MyClass {}
}
