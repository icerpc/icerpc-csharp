// Copyright (c) ZeroC, Inc.

#include "EnumTests.ice"
#include "StructTests.ice"

module IceRpc::Ice::Generator::Tests
{
    dictionary<int, int> IntIntDict;
    ["cs:generic:SortedList"] dictionary<int, int> CustomIntIntDict;
    ["cs:generic:SortedList"] dictionary<MyEnum, MyEnum> CustomMyEnumDict;

    dictionary<string, string> StringStringDict;

    dictionary<MyEnum, MyEnum> MyEnumDict;
    dictionary<MyStruct, MyStruct> MyStructDict;

    interface DictionaryMappingOperations
    {
        IntIntDict returnAndOutDictionary(out IntIntDict r1);

        IntIntDict returnDictionaryOfInt();
        void sendDictionaryOfInt(IntIntDict p);

        StringStringDict returnDictionaryOfString();
        void sendDictionaryOfString(StringStringDict p);

        MyEnumDict returnDictionaryOfMyEnum();
        void sendDictionaryOfMyEnum(MyEnumDict p);

        MyStructDict returnDictionaryOfMyStruct();
        void sendDictionaryOfMyStruct(MyStructDict p);

        // We don't need to add tests for other cs:generic arguments because the code path is always the same.
        // The argument must be a non-abstract generic type that implements ICollection<KeyValuePair<TKey, TValue>> and
        // provides a constructor with an initial capacity parameter.
        CustomIntIntDict returnAndOutCustomDictionary(out CustomIntIntDict r1);

        CustomIntIntDict returnCustomDictionary();
        void sendCustomDictionary(CustomIntIntDict p);
    }

    struct StructWithCustomDictionary
    {
        CustomIntIntDict value;
    }
}
