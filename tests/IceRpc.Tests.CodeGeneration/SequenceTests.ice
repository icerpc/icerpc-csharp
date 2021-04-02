// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

#pragma once

#include <OperationsTests.ice>
#include <EnumTests.ice>
#include <StructTests.ice>

module IceRpc::Tests::CodeGeneration
{
    sequence<byte> ByteSeq;
    sequence<bool> BoolSeq;
    sequence<short> ShortSeq;
    sequence<ushort> UShortSeq;
    sequence<int> IntSeq;
    sequence<varint> VarIntSeq;
    sequence<uint> UIntSeq;
    sequence<varuint> VarUIntSeq;
    sequence<long> LongSeq;
    sequence<varlong> VarLongSeq;
    sequence<ulong> ULongSeq;
    sequence<varulong> VarULongSeq;
    sequence<float> FloatSeq;
    sequence<double> DoubleSeq;
    sequence<string> StringSeq;

    sequence<MyEnum> MyEnumSeq;
    sequence<MyFixedLengthEnum> MyFixedLengthEnumSeq;
    sequence<MyUncheckedEnum> MyUncheckedEnumSeq;

    sequence<MyStruct> MyStructSeq;
    sequence<Operations> OperationsSeq;
    sequence<AnotherStruct> AnotherStructSeq;

    // Sequence mapping
    [cs:generic(List)] sequence<byte> ByteList;
    [cs:generic(LinkedList)] sequence<byte> ByteLinkedList;
    [cs:generic(Queue)] sequence<byte> ByteQueue;
    [cs:generic(Stack)] sequence<byte> ByteStack;
    [cs:generic(IceRpc.Tests.CodeGeneration.Custom)] sequence<byte> ByteCustomSeq;

    [cs:generic(List)] sequence<bool> BoolList;
    [cs:generic(LinkedList)] sequence<bool> BoolLinkedList;
    [cs:generic(Queue)] sequence<bool> BoolQueue;
    [cs:generic(Stack)] sequence<bool> BoolStack;
    [cs:generic(IceRpc.Tests.CodeGeneration.Custom)] sequence<bool> BoolCustomSeq;

    [cs:generic(List)] sequence<int> IntList;
    [cs:generic(LinkedList)] sequence<int> IntLinkedList;
    [cs:generic(Queue)] sequence<int> IntQueue;
    [cs:generic(Stack)] sequence<int> IntStack;
    [cs:generic(IceRpc.Tests.CodeGeneration.Custom)] sequence<int> IntCustomSeq;

    [cs:generic(List)] sequence<long> LongList;
    [cs:generic(LinkedList)] sequence<long> LongLinkedList;
    [cs:generic(Queue)] sequence<long> LongQueue;
    [cs:generic(Stack)] sequence<long> LongStack;
    [cs:generic(IceRpc.Tests.CodeGeneration.Custom)] sequence<long> LongCustomSeq;

    [cs:generic(List)] sequence<float> FloatList;
    [cs:generic(LinkedList)] sequence<float> FloatLinkedList;
    [cs:generic(Queue)] sequence<float> FloatQueue;
    [cs:generic(Stack)] sequence<float> FloatStack;
    [cs:generic(IceRpc.Tests.CodeGeneration.Custom)] sequence<float> FloatCustomSeq;

    [cs:generic(List)] sequence<string> StringList;
    [cs:generic(LinkedList)] sequence<string> StringLinkedList;
    [cs:generic(Queue)] sequence<string> StringQueue;
    [cs:generic(Stack)] sequence<string> StringStack;
    [cs:generic(IceRpc.Tests.CodeGeneration.Custom)] sequence<string> StringCustomSeq;

    [cs:generic(List)] sequence<MyEnum> MyEnumList;
    [cs:generic(LinkedList)] sequence<MyEnum> MyEnumLinkedList;
    [cs:generic(Queue)] sequence<MyEnum> MyEnumQueue;
    [cs:generic(Stack)] sequence<MyEnum> MyEnumStack;
    [cs:generic(IceRpc.Tests.CodeGeneration.Custom)] sequence<MyEnum> MyEnumCustomSeq;

    [cs:generic(List)] sequence<MyFixedLengthEnum> MyFixedLengthEnumList;
    [cs:generic(LinkedList)] sequence<MyFixedLengthEnum> MyFixedLengthEnumLinkedList;
    [cs:generic(Queue)] sequence<MyFixedLengthEnum> MyFixedLengthEnumQueue;
    [cs:generic(Stack)] sequence<MyFixedLengthEnum> MyFixedLengthEnumStack;
    [cs:generic(IceRpc.Tests.CodeGeneration.Custom)] sequence<MyFixedLengthEnum> MyFixedLengthEnumCustomSeq;

    [cs:generic(List)] sequence<MyUncheckedEnum> MyUncheckedEnumList;
    [cs:generic(LinkedList)] sequence<MyUncheckedEnum> MyUncheckedEnumLinkedList;
    [cs:generic(Queue)] sequence<MyUncheckedEnum> MyUncheckedEnumQueue;
    [cs:generic(Stack)] sequence<MyUncheckedEnum> MyUncheckedEnumStack;
    [cs:generic(IceRpc.Tests.CodeGeneration.Custom)] sequence<MyUncheckedEnum> MyUncheckedEnumCustomSeq;

    [cs:generic(List)] sequence<MyStruct> MyStructList;
    [cs:generic(LinkedList)] sequence<MyStruct> MyStructLinkedList;
    [cs:generic(Queue)] sequence<MyStruct> MyStructQueue;
    [cs:generic(Stack)] sequence<MyStruct> MyStructStack;
    [cs:generic(IceRpc.Tests.CodeGeneration.Custom)] sequence<MyStruct> MyStructCustomSeq;

    [cs:generic(List)] sequence<Operations> OperationsList;
    [cs:generic(LinkedList)] sequence<Operations> OperationsLinkedList;
    [cs:generic(Queue)] sequence<Operations> OperationsQueue;
    [cs:generic(Stack)] sequence<Operations> OperationsStack;
    [cs:generic(IceRpc.Tests.CodeGeneration.Custom)] sequence<Operations> OperationsCustomSeq;

    [cs:generic(List)] sequence<AnotherStruct> AnotherStructList;
    [cs:generic(LinkedList)] sequence<AnotherStruct> AnotherStructLinkedList;
    [cs:generic(Queue)] sequence<AnotherStruct> AnotherStructQueue;
    [cs:generic(Stack)] sequence<AnotherStruct> AnotherStructStack;
    [cs:generic(IceRpc.Tests.CodeGeneration.Custom)] sequence<AnotherStruct> AnotherStructCustomSeq;

    interface SequenceOperations
    {
        // Builtin types sequences
        (ByteSeq r1, ByteSeq r2) opByteSeq(ByteSeq p1, ByteSeq p2);
        (BoolSeq r1, BoolSeq r2) opBoolSeq(BoolSeq p1, BoolSeq p2);
        (ShortSeq r1, ShortSeq r2) opShortSeq(ShortSeq p1, ShortSeq p2);
        (UShortSeq r1, UShortSeq r2) opUShortSeq(UShortSeq p1, UShortSeq p2);
        (IntSeq r1, IntSeq r2) opIntSeq(IntSeq p1, IntSeq p2);
        (VarIntSeq r1, VarIntSeq r2) opVarIntSeq(VarIntSeq p1, VarIntSeq p2);
        (UIntSeq r1, UIntSeq r2) opUIntSeq(UIntSeq p1, UIntSeq p2);
        (VarUIntSeq r1, VarUIntSeq r2) opVarUIntSeq(VarUIntSeq p1, VarUIntSeq p2);
        (LongSeq r1, LongSeq r2) opLongSeq(LongSeq p1, LongSeq p2);
        (VarLongSeq r1, VarLongSeq r2) opVarLongSeq(VarLongSeq p1, VarLongSeq p2);
        (ULongSeq r1, ULongSeq r2) opULongSeq(ULongSeq p1, ULongSeq p2);
        (VarULongSeq r1, VarULongSeq r2) opVarULongSeq(VarULongSeq p1, VarULongSeq p2);
        (FloatSeq r1, FloatSeq r2) opFloatSeq(FloatSeq p1, FloatSeq p2);
        (DoubleSeq r1, DoubleSeq r2) opDoubleSeq(DoubleSeq p1, DoubleSeq p2);
        (StringSeq r1, StringSeq r2) opStringSeq(StringSeq p1, StringSeq p2);

        // Defined types sequences
        (MyEnumSeq r1, MyEnumSeq r2) opMyEnumSeq(MyEnumSeq p1, MyEnumSeq p2);
        (MyFixedLengthEnumSeq r1, MyFixedLengthEnumSeq r2) opMyFixedLengthEnumSeq(
            MyFixedLengthEnumSeq p1,
            MyFixedLengthEnumSeq p2);
        (MyUncheckedEnumSeq r1, MyUncheckedEnumSeq r2) opMyUncheckedEnumSeq(
            MyUncheckedEnumSeq p1,
            MyUncheckedEnumSeq p2);
        (MyStructSeq r1, MyStructSeq r2) opMyStructSeq(MyStructSeq p1, MyStructSeq p2);
        (OperationsSeq r1, OperationsSeq r2) opOperationsSeq(OperationsSeq p1, OperationsSeq p2);
        (AnotherStructSeq r1, AnotherStructSeq r2) opAnotherStructSeq(AnotherStructSeq p1, AnotherStructSeq p2);

        // Sequence mapping
        (ByteList r1, ByteList r2) opByteList(ByteList p1, ByteList p2);
        (ByteLinkedList r1, ByteLinkedList r2) opByteLinkedList(ByteLinkedList p1, ByteLinkedList p2);
        (ByteQueue r1, ByteQueue r2) opByteQueue(ByteQueue p1, ByteQueue p2);
        (ByteStack r1, ByteStack r2) opByteStack(ByteStack p1, ByteStack p2);
        (ByteCustomSeq r1, ByteCustomSeq r2) opByteCustomSeq(ByteCustomSeq p1, ByteCustomSeq p2);

        (BoolList r1, BoolList r2) opBoolList(BoolList p1, BoolList p2);
        (BoolLinkedList r1, BoolLinkedList r2) opBoolLinkedList(BoolLinkedList p1, BoolLinkedList p2);
        (BoolQueue r1, BoolQueue r2) opBoolQueue(BoolQueue p1, BoolQueue p2);
        (BoolStack r1, BoolStack r2) opBoolStack(BoolStack p1, BoolStack p2);
        (BoolCustomSeq r1, BoolCustomSeq r2) opBoolCustomSeq(BoolCustomSeq p1, BoolCustomSeq p2);

        (IntList r1, IntList r2) opIntList(IntList p1, IntList p2);
        (IntLinkedList r1, IntLinkedList r2) opIntLinkedList(IntLinkedList p1, IntLinkedList p2);
        (IntQueue r1, IntQueue r2) opIntQueue(IntQueue p1, IntQueue p2);
        (IntStack r1, IntStack r2) opIntStack(IntStack p1, IntStack p2);
        (IntCustomSeq r1, IntCustomSeq r2) opIntCustomSeq(IntCustomSeq p1, IntCustomSeq p2);

        (LongList r1, LongList r2) opLongList(LongList p1, LongList p2);
        (LongLinkedList r1, LongLinkedList r2) opLongLinkedList(LongLinkedList p1, LongLinkedList p2);
        (LongQueue r1, LongQueue r2) opLongQueue(LongQueue p1, LongQueue p2);
        (LongStack r1, LongStack r2) opLongStack(LongStack p1, LongStack p2);
        (LongCustomSeq r1, LongCustomSeq r2) opLongCustomSeq(LongCustomSeq p1, LongCustomSeq p2);

        (FloatList r1, FloatList r2) opFloatList(FloatList p1, FloatList p2);
        (FloatLinkedList r1, FloatLinkedList r2) opFloatLinkedList(FloatLinkedList p1, FloatLinkedList p2);
        (FloatQueue r1, FloatQueue r2) opFloatQueue(FloatQueue p1, FloatQueue p2);
        (FloatStack r1, FloatStack r2) opFloatStack(FloatStack p1, FloatStack p2);
        (FloatCustomSeq r1, FloatCustomSeq r2) opFloatCustomSeq(FloatCustomSeq p1, FloatCustomSeq p2);

        (StringList r1, StringList r2) opStringList(StringList p1, StringList p2);
        (StringLinkedList r1, StringLinkedList r2) opStringLinkedList(StringLinkedList p1, StringLinkedList p2);
        (StringQueue r1, StringQueue r2) opStringQueue(StringQueue p1, StringQueue p2);
        (StringStack r1, StringStack r2) opStringStack(StringStack p1, StringStack p2);
        (StringCustomSeq r1, StringCustomSeq r2) opStringCustomSeq(StringCustomSeq p1, StringCustomSeq p2);

        (MyEnumList r1, MyEnumList r2) opMyEnumList(MyEnumList p1, MyEnumList p2);
        (MyEnumLinkedList r1, MyEnumLinkedList r2) opMyEnumLinkedList(MyEnumLinkedList p1, MyEnumLinkedList p2);
        (MyEnumQueue r1, MyEnumQueue r2) opMyEnumQueue(MyEnumQueue p1, MyEnumQueue p2);
        (MyEnumStack r1, MyEnumStack r2) opMyEnumStack(MyEnumStack p1, MyEnumStack p2);
        (MyEnumCustomSeq r1, MyEnumCustomSeq r2) opMyEnumCustomSeq(MyEnumCustomSeq p1, MyEnumCustomSeq p2);

        (MyFixedLengthEnumList r1, MyFixedLengthEnumList r2) opMyFixedLengthEnumList(
            MyFixedLengthEnumList p1,
            MyFixedLengthEnumList p2);
        (MyFixedLengthEnumLinkedList r1, MyFixedLengthEnumLinkedList r2) opMyFixedLengthEnumLinkedList(
            MyFixedLengthEnumLinkedList p1,
            MyFixedLengthEnumLinkedList p2);
        (MyFixedLengthEnumQueue r1, MyFixedLengthEnumQueue r2) opMyFixedLengthEnumQueue(
            MyFixedLengthEnumQueue p1,
            MyFixedLengthEnumQueue p2);
        (MyFixedLengthEnumStack r1, MyFixedLengthEnumStack r2) opMyFixedLengthEnumStack(
            MyFixedLengthEnumStack p1,
            MyFixedLengthEnumStack p2);
        (MyFixedLengthEnumCustomSeq r1, MyFixedLengthEnumCustomSeq r2) opMyFixedLengthEnumCustomSeq(
            MyFixedLengthEnumCustomSeq p1,
            MyFixedLengthEnumCustomSeq p2);

        (MyUncheckedEnumList r1, MyUncheckedEnumList r2) opMyUncheckedEnumList(
            MyUncheckedEnumList p1,
            MyUncheckedEnumList p2);
        (MyUncheckedEnumLinkedList r1, MyUncheckedEnumLinkedList r2) opMyUncheckedEnumLinkedList(
            MyUncheckedEnumLinkedList p1,
            MyUncheckedEnumLinkedList p2);
        (MyUncheckedEnumQueue r1, MyUncheckedEnumQueue r2) opMyUncheckedEnumQueue(
            MyUncheckedEnumQueue p1,
            MyUncheckedEnumQueue p2);
        (MyUncheckedEnumStack r1, MyUncheckedEnumStack r2) opMyUncheckedEnumStack(
            MyUncheckedEnumStack p1,
            MyUncheckedEnumStack p2);
        (MyUncheckedEnumCustomSeq r1, MyUncheckedEnumCustomSeq r2) opMyUncheckedEnumCustomSeq(
            MyUncheckedEnumCustomSeq p1,
            MyUncheckedEnumCustomSeq p2);

        (MyStructList r1, MyStructList r2) opMyStructList(MyStructList p1, MyStructList p2);
        (MyStructLinkedList r1, MyStructLinkedList r2) opMyStructLinkedList(
            MyStructLinkedList p1,
            MyStructLinkedList p2);
        (MyStructQueue r1, MyStructQueue r2) opMyStructQueue(MyStructQueue p1, MyStructQueue p2);
        (MyStructStack r1, MyStructStack r2) opMyStructStack(MyStructStack p1, MyStructStack p2);
        (MyStructCustomSeq r1, MyStructCustomSeq r2) opMyStructCustomSeq(
            MyStructCustomSeq p1,
            MyStructCustomSeq p2);

        (OperationsList r1, OperationsList r2) opOperationsList(OperationsList p1, OperationsList p2);
        (OperationsLinkedList r1, OperationsLinkedList r2) opOperationsLinkedList(
            OperationsLinkedList p1,
            OperationsLinkedList p2);
        (OperationsQueue r1, OperationsQueue r2) opOperationsQueue(OperationsQueue p1, OperationsQueue p2);
        (OperationsStack r1, OperationsStack r2) opOperationsStack(OperationsStack p1, OperationsStack p2);
        (OperationsCustomSeq r1, OperationsCustomSeq r2) opOperationsCustomSeq(
            OperationsCustomSeq p1,
            OperationsCustomSeq p2);

        (AnotherStructList r1, AnotherStructList r2) opAnotherStructList(AnotherStructList p1, AnotherStructList p2);
        (AnotherStructLinkedList r1, AnotherStructLinkedList r2) opAnotherStructLinkedList(
            AnotherStructLinkedList p1,
            AnotherStructLinkedList p2);
        (AnotherStructQueue r1, AnotherStructQueue r2) opAnotherStructQueue(
            AnotherStructQueue p1,
            AnotherStructQueue p2);
        (AnotherStructStack r1, AnotherStructStack r2) opAnotherStructStack(
            AnotherStructStack p1,
            AnotherStructStack p2);
        (AnotherStructCustomSeq r1, AnotherStructCustomSeq r2) opAnotherStructCustomSeq(
            AnotherStructCustomSeq p1,
            AnotherStructCustomSeq p2);
    }
}
