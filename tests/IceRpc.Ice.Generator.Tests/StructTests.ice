// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Tests
{
    interface AnotherPingable
    {
        void ping();
    }

    sequence<AnotherPingable*> AnotherPingableSeq;
    dictionary<int, AnotherPingable*> AnotherPingableDict;

    struct MyStructWithProxy
    {
        int a;
        AnotherPingable* i;
    }

    struct MyStructWithSequenceOfProxies
    {
        AnotherPingableSeq i;
    }

    struct MyStructWithDictionaryOfProxies
    {
        AnotherPingableDict i;
    }
}
