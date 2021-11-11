// Copyright (c) ZeroC, Inc. All rights reserved.

// Ensure that using the same type names in different modules, doesn't cause any conflicts and generates correct code.
module IceRpc::Tests::Slice::Scope
{
    struct S
    {
        int v;
    }

    typealias SMap = dictionary<string, S>;
    typealias SSeq = sequence<S>;

    class C
    {
        S s;
    }

    typealias CMap = dictionary<string, C>;
    typealias CSeq = sequence<C>;

    enum E1
    {
        v1,
        v2,
        v3
    }

    struct S1
    {
        string s;
    }

    class C1
    {
        string s;
    }

    // Ensure that struct data members can use a type name as a data member name.
    struct S2
    {
        E1 E1;
        S1 S1;
        C1 C1;
    }

    // Ensure that class data members can use a type name as a data member name.
    class C2
    {
        E1 E1;
        S1 S1;
        C1 C1;
    }

    interface Operations
    {
        S opS(S p1);
        SSeq opSSeq(SSeq p1);
        SMap opSMap(SMap p1);

        C opC(C p1);
        CSeq opCSeq(CSeq p1);
        CMap opCMap(CMap p1);

        // Ensure that a type name can be used as a parameter name
        E1 opE1(E1 E1);
        S1 opS1(S1 S1);
        C1 opC1(C1 C1);
    }

    typealias OperationsMap = dictionary<string, Operations>;
    typealias OperationsSeq = sequence<Operations>;

    module Inner
    {
        struct S
        {
            int v;
        }

        module Inner2
        {
            struct S
            {
                int v;
            }

            typealias SMap = dictionary<string, S>;
            typealias SSeq = sequence<S>;

            class C
            {
                S s;
            }

            typealias CMap = dictionary<string, C>;
            typealias CSeq = sequence<C>;

            interface Operations
            {
                S opS(S p1);
                SSeq opSSeq(SSeq p1);
                SMap opSMap(SMap p1);

                C opC(C c1);
                CSeq opCSeq(CSeq p1);
                CMap opCMap(CMap p1);
            }

            typealias OperationsMap = dictionary<string, Operations>;
            typealias OperationsSeq = sequence<Operations>;
        }

        class C
        {
            S s;
        }

        typealias SMap = dictionary<string, Inner2::S>;
        typealias SSeq = sequence<Inner2::S>;

        typealias CMap = dictionary<string, Inner2::C>;
        typealias CSeq = sequence<Inner2::C>;

        interface Operations
        {
            Inner2::S opS(Inner2::S p1);
            Inner2::SSeq opSSeq(Inner2::SSeq p1);
            Inner2::SMap opSMap(Inner2::SMap p1);

            Inner2::C opC(Inner2::C p1);
            Inner2::CSeq opCSeq(Inner2::CSeq p1);
            Inner2::CMap opCMap(Inner2::CMap p1);
        }

        typealias OperationsMap = dictionary<string, Operations>;
        typealias OperationsSeq = sequence<Operations>;
    }
}

module IceRpc::Tests::Slice::Scope::Inner::Test::Inner2
{
    interface Operations
    {
        IceRpc::Tests::Slice::Scope::S opS(IceRpc::Tests::Slice::Scope::S p1);
        IceRpc::Tests::Slice::Scope::SSeq opSSeq(IceRpc::Tests::Slice::Scope::SSeq p1);
        IceRpc::Tests::Slice::Scope::SMap opSMap(IceRpc::Tests::Slice::Scope::SMap p1);

        IceRpc::Tests::Slice::Scope::C opC(IceRpc::Tests::Slice::Scope::C p1);
        IceRpc::Tests::Slice::Scope::CSeq opCSeq(IceRpc::Tests::Slice::Scope::CSeq c1);
        IceRpc::Tests::Slice::Scope::CMap opCMap(IceRpc::Tests::Slice::Scope::CMap p1);
    }
}
