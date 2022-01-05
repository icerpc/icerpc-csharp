// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    class MyClassA
    {
        theB: MyClassB?,
        theC: MyClassC?,
    }

    class MyClassB : MyClassA
    {
        theA: MyClassA?,
    }

    class MyClassC
    {
        theB: MyClassB?,
    }

    class MyClassD
    {
        theA: MyClassA?,
        theB: MyClassB?,
        theC: MyClassC?,
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
        name: string,
    }

    class MyClassB1
    {
        a1: MyClassA1,
        a2: MyClassA1,
    }

    class MyClassD1 : MyClassB1
    {
        a3: MyClassA1,
        a4: MyClassA1,
    }

    exception MyException
    {
        a1: MyClassA1,
        a2: MyClassA1,
    }

    exception MyDerivedException : MyException
    {
        a3: MyClassA1,
        a4: MyClassA1,
    }

    class MyClassRecursive
    {
        v: MyClassRecursive?,
    }

    class MyClassK
    {
        value: AnyClass?,
    }

    class MyClassL
    {
        data: string,
    }

    typealias ClassSeq = sequence<AnyClass?>;
    typealias ClassMap = dictionary<string, AnyClass?>;

    class MyClassM
    {
        v: dictionary<MyStruct, MyClassL>,
    }

    class MyBaseClass1
    {
        id: string,
    }

    class MyDerivedClass1 : MyBaseClass1
    {
        name: string,
    }

    class MyDerivedClass2 : MyBaseClass1
    {
    }

    class MyClass2
    {
    }

    interface ClassOperations
    {
        getB1() -> MyClassB;
        getB2() -> MyClassB;
        getC() -> MyClassC;
        getD() -> MyClassD;

        getAll() -> (r1: MyClassB, r2: MyClassB, r3: MyClassC, r4: MyClassD);

        getK() -> MyClassK;

        opClass(p1: AnyClass?) -> (r1: AnyClass?, r2: AnyClass?);
        opClassSeq(p1: ClassSeq) -> (r1: ClassSeq, r2: ClassSeq);
        opClassMap(p1: ClassMap) -> (r1: ClassMap, r2: ClassMap);

        getD1(p1: MyClassD1) -> MyClassD1;

        throwMyDerivedException();

        opRecursive(p1: MyClassRecursive);

        getCompact() -> MyCompactClass;

        opM(p1: MyClassM) -> (r1: MyClassM, r2: MyClassM);
        opE(p1: MyClassE, p2: int) -> (r1: MyClassE, r2: MyClassE);

        getMyDerivedClass1() -> MyDerivedClass1;
        getMyDerivedClass2() -> MyDerivedClass2;
        getMyClass2() -> MyClass2;
    }

    class MyClassEmpty
    {
    }

    class MyClassAlsoEmpty
    {
    }

    interface ClassOperationsUnexpectedClass
    {
        op() -> MyClassEmpty;
    }
}
