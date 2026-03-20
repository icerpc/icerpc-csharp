// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Tests
{
    interface Base
    {
        void op1();
    }

    interface Derived : Base
    {
        void op2();
    }

    interface MoreDerived : Derived
    {
        void op3();
    }
}
