// Copyright (c) ZeroC, Inc.

module ZeroC::Slice::IceGen::Tests
{
    exception MyException
    {
        int i;
        int j;
    }

    exception MyExceptionWithTaggedFields
    {
        int i;
        int j;
        optional(1) int k;
        optional(255) int l;
    }

    exception MyDerivedException : MyException
    {
        int k;
        int l;
    }
}
