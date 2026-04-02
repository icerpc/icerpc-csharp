// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Base::Tests
{
    class SlicingBaseClass
    {
        string m1;
    }

    class SlicingDerivedClass : SlicingBaseClass
    {
        string m2;
        SlicingBaseClass m3;
    }

    class SlicingMostDerivedClass : SlicingDerivedClass
    {
        SlicingBaseClass m4;
    }

    class SlicingClassWithTaggedFields : SlicingDerivedClass
    {
        SlicingBaseClass m4;
        optional(1) string m5;
    }

    class SlicingBaseClassWithCompactId(1)
     {
        string m1;
    }

    class SlicingDerivedClassWithCompactId(2) : SlicingBaseClassWithCompactId
    {
        string m2;
    }

    class SlicingMostDerivedClassWithCompactId(3) : SlicingDerivedClassWithCompactId
    {
        SlicingBaseClassWithCompactId m3;
    }

    exception SlicingBaseException
    {
        string m1;
    }

    exception SlicingDerivedException : SlicingBaseException
    {
        string m2;
    }

    exception SlicingMostDerivedException : SlicingDerivedException
    {
        SlicingBaseClass m3;
    }
}
