// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Base::Tests::Inner
{
    struct S
    {
        int v;
    }
}

module IceRpc::Ice::Generator::Base::Tests
{
    struct S
    {
        string v;
    }

    struct S2
    {
        S s;
        Inner::S innerS;
    }
}
