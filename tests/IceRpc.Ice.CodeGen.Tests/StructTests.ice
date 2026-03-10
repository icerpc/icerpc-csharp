// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::CodeGen::Tests
{
    interface AnotherPingable
    {
        void ping();
    }

    sequence<AnotherPingable*> AnotherPingableSeq;
    dictionary<int, AnotherPingable*> AnotherPingableDict;

    struct MyCompactStructWithNullableProxy
    {
        int a;
        AnotherPingable* i;
    }

    struct MyCompactStructWithSequenceOfNullableProxies
    {
        AnotherPingableSeq i;
    }

    struct MyCompactStructWithDictionaryOfNullableProxies
    {
        AnotherPingableDict i;
    }
}
