// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::None::Tests::Inner
{
    struct S
    {
        int v;
    }
}

module IceRpc::Ice::Generator::None::Tests
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
