// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Generator::Tests
{
    interface MyWidget {}

     // Unusual casing.
    interface myOtherWidget {}

    interface MyDerivedWidget : MyWidget, myOtherWidget {}
}
