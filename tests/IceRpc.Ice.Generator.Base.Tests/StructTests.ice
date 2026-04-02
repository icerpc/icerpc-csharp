// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Base::Tests
{
    struct MyStruct
    {
        int i;
        int j;
    }

    struct MyStructWithFieldAttributes
    {
        ["cs:attribute:System.ComponentModel.Description(\"An integer\")"] int i;
        int j;
    }

    ["cs:readonly"] struct KeyValuePair
    {
        int key;
        string value;
    }
}
