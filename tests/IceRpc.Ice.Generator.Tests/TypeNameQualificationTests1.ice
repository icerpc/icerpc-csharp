// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Tests::Inner
{
    struct S
    {
        int v;
    }
}

module IceRpc::Ice::Generator::Tests
{
    struct S
    {
        string v;
    }

    interface TypeNameQualificationOperations
    {
        S opWithTypeNamesDefinedInMultipleModules(Inner::S s);
    }
}
