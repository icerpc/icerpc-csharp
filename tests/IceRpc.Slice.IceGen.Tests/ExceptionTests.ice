// Copyright (c) ZeroC, Inc.

module IceRpc::Slice::IceGen::Tests
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

    exception EmptyException {}

    interface SliceExceptionOperations
    {
        void opThrowsMultipleExceptions() throws MyException, EmptyException;

        void opThrowsMyException() throws MyException;

        void opThrowsNothing();
    }

    // Just like SliceExceptionOperations, but with different exception specifications
    interface AltSliceExceptionOperations
    {
        void opThrowsMultipleExceptions() throws MyException;

        void opThrowsMyException();
    }
}
