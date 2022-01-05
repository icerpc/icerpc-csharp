// Copyright (c) ZeroC, Inc. All rights reserved.

// Ensure that using the same type names in different modules, doesn't cause any conflicts and generates correct code.
module IceRpc::Tests::Slice::Scope
{
    struct S
    {
        v: int,
    }

    typealias SMap = dictionary<string, S>;
    typealias SSeq = sequence<S>;

    class C
    {
        s: S,
    }

    typealias CMap = dictionary<string, C>;
    typealias CSeq = sequence<C>;

    enum E1
    {
        v1,
        v2,
        v3,
    }

    struct S1
    {
        s: string,
    }

    class C1
    {
        s: string,
    }

    // Ensure that struct data members can use a type name as a data member name.
    struct S2
    {
        E1: E1,
        S1: S1,
        C1: C1,
    }

    // Ensure that class data members can use a type name as a data member name.
    class C2
    {
        E1: E1,
        S1: S1,
        C1: C1,
    }

    interface Operations
    {
        opS(p1: S) -> S;
        opSSeq(p1: SSeq) -> SSeq;
        opSMap(p1: SMap) -> SMap;

        opC(p1: C) -> C;
        opCSeq(p1: CSeq) -> CSeq;
        opCMap(p1: CMap) -> CMap;

        // Ensure that a type name can be used as a parameter name
        opE1(E1: E1) -> E1;
        opS1(S1: S1) -> S1;
        opC1(C1: C1) -> C1;
    }

    typealias OperationsMap = dictionary<string, Operations>;
    typealias OperationsSeq = sequence<Operations>;

    module Inner
    {
        struct S
        {
            v: int,
        }

        module Inner2
        {
            struct S
            {
                v: int,
            }

            typealias SMap = dictionary<string, S>;
            typealias SSeq = sequence<S>;

            class C
            {
                s: S,
            }

            typealias CMap = dictionary<string, C>;
            typealias CSeq = sequence<C>;

            interface Operations
            {
                opS(p1: S) -> S;
                opSSeq(p1: SSeq) -> SSeq;
                opSMap(p1: SMap) -> SMap;

                opC(c1: C) -> C;
                opCSeq(p1: CSeq) -> CSeq;
                opCMap(p1: CMap) -> CMap;
            }

            typealias OperationsMap = dictionary<string, Operations>;
            typealias OperationsSeq = sequence<Operations>;
        }

        class C
        {
            s: S,
        }

        typealias SMap = dictionary<string, Inner2::S>;
        typealias SSeq = sequence<Inner2::S>;

        typealias CMap = dictionary<string, Inner2::C>;
        typealias CSeq = sequence<Inner2::C>;

        interface Operations
        {
            opS(p1: Inner2::S) -> Inner2::S;
            opSSeq(p1: Inner2::SSeq) -> Inner2::SSeq;
            opSMap(p1: Inner2::SMap) -> Inner2::SMap;

            opC(p1: Inner2::C) -> Inner2::C;
            opCSeq(p1: Inner2::CSeq) -> Inner2::CSeq;
            opCMap(p1: Inner2::CMap) -> Inner2::CMap;
        }

        typealias OperationsMap = dictionary<string, Operations>;
        typealias OperationsSeq = sequence<Operations>;
    }
}

module IceRpc::Tests::Slice::Scope::Inner::Test::Inner2
{
    interface Operations
    {
        opS(p1: IceRpc::Tests::Slice::Scope::S) -> IceRpc::Tests::Slice::Scope::S;
        opSSeq(p1: IceRpc::Tests::Slice::Scope::SSeq) -> IceRpc::Tests::Slice::Scope::SSeq;
        opSMap(p1: IceRpc::Tests::Slice::Scope::SMap) -> IceRpc::Tests::Slice::Scope::SMap;

        opC(p1: IceRpc::Tests::Slice::Scope::C) -> IceRpc::Tests::Slice::Scope::C;
        opCSeq(c1: IceRpc::Tests::Slice::Scope::CSeq) -> IceRpc::Tests::Slice::Scope::CSeq;
        opCMap(p1: IceRpc::Tests::Slice::Scope::CMap) -> IceRpc::Tests::Slice::Scope::CMap;
    }
}
