// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Base::Tests
{
    class MyClassB;
    class MyClassC;

    class MyClassA
    {
        MyClassB theB;
        MyClassC theC;
    }

    class MyClassB : MyClassA
    {
        MyClassA theA;
    }

    class MyClassC
    {
        MyClassB theB;
    }

    class Person
    {
        Person spouse;
        Person emergencyContact;
    }

    class MyCompactClass(15) {}

    class MyClassWithTaggedFields
    {
        optional(10) int a;
    }

    class MyDerivedClassWithTaggedFields : MyClassWithTaggedFields
    {
        optional(20) string b;
    }

    class MyDerivedCompactClass(789) : MyCompactClass {}

    class MyClassWithValues
    {
        int a;
        optional(5) int b;
        Value c;
        optional(3) int d;
        int e;
    }

    // TODO: this metadata doesn't exist yet.
    // ["cs:internal"]
    class MyInternalBaseClass
    {
        string m1;
    }

    // TODO: this metadata doesn't exist yet.
    // ["cs:internal"]
    class MyInternalClass : MyInternalBaseClass
    {
        string m2;
    }
}
