// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#include <StructTests.ice>

module IceRpc::Tests::CodeGeneration
{
    class MyClassB;
    class MyClassC;

    class MyClassA
    {
        MyClassB? theB;
        MyClassC? theC;
    }

    class MyClassB : MyClassA
    {
        MyClassA? theA;
    }

    class MyClassC
    {
        MyClassB? theB;
    }

    class MyClassD
    {
        MyClassA? theA;
        MyClassB? theB;
        MyClassC? theC;
    }

    // Exercise empty class with non-empty base
    class MyClassE : MyClassA
    {
    }

    class MyCompactClass(1)
    {
    }

    class MyDerivedCompactClass(789) : MyCompactClass
    {
    }

    class MyClassA1
    {
        string name;
    }

    class MyClassB1
    {
        MyClassA1 a1;
        MyClassA1 a2;
    }

    class MyClassD1 : MyClassB1
    {
        MyClassA1 a3;
        MyClassA1 a4;
    }

    exception MyException
    {
        MyClassA1 a1;
        MyClassA1 a2;
    }

    exception MyDerivedException : MyException
    {
        MyClassA1 a3;
        MyClassA1 a4;
    }

    class MyClassRecursive
    {
        MyClassRecursive? v;
    }

    class MyClassK
    {
        AnyClass? value;
    }

    class MyClassL
    {
        string data;
    }

    sequence<AnyClass?> ClassSeq;
    dictionary<string, AnyClass?> ClassMap;


    dictionary<MyStruct, MyClassL> MyClassLMap;

    class MyClassM
    {
        MyClassLMap v;
    }

    interface ObjectOperations
    {
        MyClassB getB1();
        MyClassB getB2();
        MyClassC getC();
        MyClassD getD();

        (MyClassB r1, MyClassB r2, MyClassC r3, MyClassD r4) getAll();

        MyClassK getK();

        (AnyClass? r1, AnyClass? r2) opClass(AnyClass? p1);
        (ClassSeq r1, ClassSeq r2) opClassSeq(ClassSeq p1);
        (ClassMap r1, ClassMap r2) opClassMap(ClassMap p1);

        MyClassD1 getD1(MyClassD1 p1);

        void throwMyDerivedException();

        void opRecursive(MyClassRecursive p1);

        MyCompactClass getCompact();

        (MyClassM r1, MyClassM r2) opM(MyClassM p1);
        (MyClassE r1, MyClassE r2) opE(MyClassE p1);
    }

    class MyClassEmpty
    {
    }

    class MyClassAlsoEmpty
    {
    }

    interface ObjectOperationsUnexpectedObject
    {
        MyClassEmpty op();
    }

    class MyBaseClass1
    {
        string id = "";
    }

    class MyDerivedClass1 : MyBaseClass1
    {
        string name = "";
    }

    class MyDerivedClass2 : MyBaseClass1
    {
    }

    class MyClass2
    {
    }
}
