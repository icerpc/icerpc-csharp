// Copyright (c) ZeroC, Inc.

#include "ConstTests.ice"

module IceRpc::Ice::Generator::Base::Tests
{
    struct Point { int x; int y = 42; }

    struct StructWithDefaultValues
    {
        bool boolFalse = false;
        bool boolTrue = true;
        byte b = 254;
        short s = 16000;
        int i = 3;
        long l = 4;
        float f = 5.1;
        double d = 6.2;
        string str = "foo \\ \"bar\n \r\n\t\v\f\a\b\? \007 \x07";
        Color c1 = ::IceRpc::Ice::Generator::Base::Tests::Color::red;
        Color c2 = Tests::green;
        Color c3 = blue;
        Nested::Color nc1 = Tests::Nested::Color::red;
        Nested::Color nc2 = Nested::green;
        Nested::Color nc3 = blue;
        string noDefault;
        int zeroI = 0;
        long zeroL = 0;
        float zeroF = 0;
        float zeroDotF = 0.0;
        double zeroD = 0;
        double zeroDotD = 0.0;
    }

    struct StructWithConstDefaultValues
    {
        bool boolTrue = ConstBool;
        byte b = ConstByte;
        short s = ConstShort;
        int i = ConstInt;
        long l = ConstLong;
        float f = ConstFloat;
        double d = ConstDouble;
        string str = ConstString;
        Color c1 = ConstColor1;
        Color c2 = ConstColor2;
        Color c3 = ConstColor3;
        Nested::Color nc1 = ConstNestedColor1;
        Nested::Color nc2 = ConstNestedColor2;
        Nested::Color nc3 = ConstNestedColor3;
        int zeroI = ConstZeroI;
        long zeroL = ConstZeroL;
        float zeroF = ConstZeroF;
        float zeroDotF = ConstZeroDotF;
        double zeroD = ConstZeroD;
        double zeroDotD = ConstZeroDotD;
    }

    class BaseWithDefaultValues
    {
        bool boolFalse = false;
        bool boolTrue = true;
        byte b = 1;
        short s = 2;
        int i = 3;
        long l = 4;
        float f = 5.1;
        double d = 6.2;
        string str = "foo \\ \"bar\n \r\n\t\v\f\a\b\? \007 \x07";
        string noDefault;
        int zeroI = 0;
        long zeroL = 0;
        float zeroF = 0;
        float zeroDotF = 0.0;
        double zeroD = 0;
        double zeroDotD = 0.0;
        StructWithDefaultValues st1;
    }

    class DerivedWithDefaultValues extends BaseWithDefaultValues
    {
        Color c1 = ::IceRpc::Ice::Generator::Base::Tests::Color::red;
        Color c2 = Tests::green;
        Color c3 = blue;

        Nested::Color nc1 = Tests::Nested::Color::red;
        Nested::Color nc2 = Nested::green;
        Nested::Color nc3 = blue;
    }

    exception BaseExceptionWithDefaultValues
    {
        bool boolFalse = false;
        bool boolTrue = true;
        byte b = 1;
        short s = 2;
        int i = 3;
        long l = 4;
        float f = 5.1;
        double d = 6.2;
        string str = "foo \\ \"bar\n \r\n\t\v\f\a\b\? \007 \x07";
        string noDefault;
        int zeroI = 0;
        long zeroL = 0;
        float zeroF = 0;
        float zeroDotF = 0.0;
        double zeroD = 0;
        double zeroDotD = 0.0;
    }

    exception DerivedExceptionWithDefaultValues extends BaseExceptionWithDefaultValues
    {
        Color c1 = ConstColor1;
        Color c2 = ConstColor2;
        Color c3 = ConstColor3;

        Nested::Color nc1 = ConstNestedColor1;
        Nested::Color nc2 = ConstNestedColor2;
        Nested::Color nc3 = ConstNestedColor3;
    }
}