// Copyright (c) ZeroC, Inc.

#pragma once

module IceRpc::Ice::Generator::Base::Tests
{
    enum Color { red, green, blue }

    module Nested
    {
        enum Color { red, green, blue }
    }

    /// My bool constant.
    const bool ConstBool = true;

    const byte ConstByte = 254;
    const short ConstShort = 16000;
    const int ConstInt = 3;
    const long ConstLong = 4;
    const float ConstFloat = 5.1;
    const double ConstDouble = 6.2;
    const double ConstFloatAlias = ConstFloat;

    const string ConstString = "foo \\ \"bar\n \r\n\t\v\f\a\b\? \007 \x07";

    const Color ConstColor1 = ::IceRpc::Ice::Generator::Base::Tests::Color::red;
    const Color ConstColor2 = Tests::green;
    const Color ConstColor3 = blue;
    const Color ConstColor2Alias = ConstColor2;
    const Nested::Color ConstNestedColor1 = Tests::Nested::Color::red;
    const Nested::Color ConstNestedColor2 = Tests::Nested::green;
    const Nested::Color ConstNestedColor3 = blue;
    const Nested::Color ConstNestedColor2Alias = ConstNestedColor2;

    const int ConstZeroI = 0;
    const long ConstZeroL = 0;
    const float ConstZeroF = 0;
    const float ConstZeroDotF = 0.0;
    const double ConstZeroD = 0;
    const double ConstZeroDotD = 0.0;
}
