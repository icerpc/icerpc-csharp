// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Tests
{
    interface MyBase
    {
        void op1();
    }

    interface MyDerived : MyBase
    {
        void op2();
    }

    interface MyMoreDerived : MyDerived
    {
        void op3();
    }
}
