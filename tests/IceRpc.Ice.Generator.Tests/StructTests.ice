// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Tests
{
    interface AnotherPingable
    {
        void ping();
    }

    sequence<AnotherPingable*> AnotherPingableSeq;
    dictionary<int, AnotherPingable*> AnotherPingableDict;

    struct MyStructWithNullableProxy
    {
        int a;
        AnotherPingable* i;
    }

    struct MyStructWithSequenceOfNullableProxies
    {
        AnotherPingableSeq i;
    }

    struct MyStructWithDictionaryOfNullableProxies
    {
        AnotherPingableDict i;
    }
}
