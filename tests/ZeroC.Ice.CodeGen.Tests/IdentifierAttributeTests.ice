// Copyright (c) ZeroC, Inc.

module ZeroC::Ice::CodeGen::Tests
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
