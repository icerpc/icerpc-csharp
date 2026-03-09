// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::CodeGen::Tests
{
    interface MyWidget {}

     // Unusual casing.
    interface myOtherWidget {}

    interface MyDerivedWidget : MyWidget, myOtherWidget {}
}
