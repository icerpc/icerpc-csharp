// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Tests
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

    interface IceExceptionOperations
    {
        void opThrowsMultipleExceptions() throws MyException, EmptyException;

        void opThrowsMyException() throws MyException;

        void opThrowsNothing();
    }

    // Just like IceExceptionOperations, but with different exception specifications
    interface AltIceExceptionOperations
    {
        void opThrowsMultipleExceptions() throws MyException;

        void opThrowsMyException();
    }
}
