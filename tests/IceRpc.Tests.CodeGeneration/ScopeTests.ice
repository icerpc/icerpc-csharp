// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::CodeGeneration::Scope
{
	struct S
    {
        int v;
    }

    dictionary<string, S> SMap;
    sequence<S> SSeq;

    class C
    {
        S s;
    }

    dictionary<string, C> CMap;
    sequence<C> CSeq;

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

    dictionary<string, Operations> OperationsMap;
    sequence<Operations> OperationsSeq;

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

            dictionary<string, S> SMap;
            sequence<S> SSeq;

            class C
            {
                S s;
            }

            dictionary<string, C> CMap;
            sequence<C> CSeq;

            interface Operations
            {
                S opS(S p1);
                SSeq opSSeq(SSeq p1);
                SMap opSMap(SMap p1);

                C opC(C c1);
                CSeq opCSeq(CSeq p1);
                CMap opCMap(CMap p1);
            }

            dictionary<string, Operations> OperationsMap;
            sequence<Operations> OperationsSeq;
        }

        class C
        {
            S s;
        }

        sequence<Inner2::S> SSeq;
        dictionary<string, Inner2::S> SMap;

        dictionary<string, Inner2::C> CMap;
        sequence<Inner2::C> CSeq;

        interface Operations
        {
            Inner2::S opS(Inner2::S p1);
            Inner2::SSeq opSSeq(Inner2::SSeq p1);
            Inner2::SMap opSMap(Inner2::SMap p1);

            Inner2::C opC(Inner2::C p1);
            Inner2::CSeq opCSeq(Inner2::CSeq p1);
            Inner2::CMap opCMap(Inner2::CMap p1);
        }

        dictionary<string, Operations> OperationsMap;
        sequence<Operations> OperationsSeq;
    }
}

module IceRpc::Tests::CodeGeneration::Scope::Inner::Test::Inner2
{
    interface Operations
    {
        IceRpc::Tests::CodeGeneration::Scope::S opS(IceRpc::Tests::CodeGeneration::Scope::S p1);
        IceRpc::Tests::CodeGeneration::Scope::SSeq opSSeq(IceRpc::Tests::CodeGeneration::Scope::SSeq p1);
        IceRpc::Tests::CodeGeneration::Scope::SMap opSMap(IceRpc::Tests::CodeGeneration::Scope::SMap p1);

        IceRpc::Tests::CodeGeneration::Scope::C opC(IceRpc::Tests::CodeGeneration::Scope::C p1);
        IceRpc::Tests::CodeGeneration::Scope::CSeq opCSeq(IceRpc::Tests::CodeGeneration::Scope::CSeq c1);
        IceRpc::Tests::CodeGeneration::Scope::CMap opCMap(IceRpc::Tests::CodeGeneration::Scope::CMap p1);
    }
}
