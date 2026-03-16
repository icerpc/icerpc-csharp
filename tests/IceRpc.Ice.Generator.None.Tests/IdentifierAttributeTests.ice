// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::None::Tests
{
    ["cs:identifier:REnamedClass"]
    class OriginalClass
    {
        ["cs:identifier:renamedX"] int x;
    }

    ["cs:identifier:REnamedException"]
    exception OriginalException {}

    exception DerivedException : OriginalException {}
}
