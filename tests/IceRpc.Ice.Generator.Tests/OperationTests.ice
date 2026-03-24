// Copyright (c) ZeroC, Inc.

#include "ProxyTests.ice"

module IceRpc::Ice::Generator::Tests
{
    sequence<int> IntSeq;

    interface MyOperationsA
    {
        // No parameters and void return
        void opWithoutParametersAndVoidReturn();

        // Single parameter and return
        int opWithSingleParameterAndReturnValue(int p);

        // Multiple parameters, a return value and an out parameter
        int opWithMultipleParametersAndReturnValues(int p1, int p2, out int r2);

        // idempotent operation
        idempotent void idempotentOp();

        // cancel and features as regular parameter names
        void opWithSpecialParameterNames(int cancel, int features);

        // Marshaled result
        ["marshaled-result"] IntSeq opWithSingleReturnValueAndMarshaledResultAttribute();
        ["marshaled-result"] IntSeq opWithMultipleReturnValuesAndMarshaledResultAttribute(out IntSeq r2);

        // C# keyword as operation name
        void continue();

        // ReadOnlyMemory input parameters and return values
        IntSeq opReadOnlyMemory(IntSeq p1);

        // ReadOnlyMemory tagged input parameters and return values
        optional(2) IntSeq opReadOnlyMemoryTagged(optional(1) IntSeq p1);

        // Proxy parameter and return value
        void opWithProxyParameter(Pingable* service);
        Pingable* opWithProxyReturnValue();
    }

    interface MyDerivedOperationsA : MyOperationsA
    {
        // No parameters and void return
        void opDerivedWithoutParametersAndVoidReturn();

        // Single parameter and return
        int opDerivedWithSingleParameterAndReturnValue(int p);
    }

    interface MyTaggedOperations
    {
        void op(optional(1) int x, int y, optional(2) int z);
    }

    // A prior version of MyTaggedOperations which doesn't contain any of the tagged parameters.
    interface MyTaggedOperationsV0
    {
        void op(int y);
    }

    interface MyBaseOperations
    {
        void op();
    }

    interface NoOperation {}

    // Make sure the generated code does not generate a "new" for Request and Response in the service class.
    interface DerivedFromNoOperation : NoOperation
    {
        int op(int x);
    }
}

module IceRpc::Ice::Derived::Generator::Tests
{
    interface MyDerivedOperations : IceRpc::Ice::Generator::Tests::MyBaseOperations
    {
        void opDerived();
    }
}
