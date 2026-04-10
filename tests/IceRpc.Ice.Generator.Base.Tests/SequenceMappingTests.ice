// Copyright (c) ZeroC, Inc.

// Tests for fields and sequence elements with sequence types.
module IceRpc::Ice::Generator::Base::Tests
{
    sequence<string> StringSeq;
    sequence<StringSeq> StringSeqSeq;

    ["cs:generic:List"] sequence<string> StringList;
    ["cs:generic:List"] sequence<StringList> StringListList;

    ["cs:generic:Queue"] sequence<string> StringQueue;
    ["cs:generic:Queue"] sequence<StringQueue> StringQueueQueue;

    ["cs:generic:LinkedList"] sequence<string> StringLinkedList;
    ["cs:generic:LinkedList"] sequence<StringLinkedList> StringLinkedListLinkedList;

    ["cs:generic:Stack"] sequence<string> StringStack;
    ["cs:generic:Stack"] sequence<StringStack> StringStackStack;

    ["cs:generic:IceRpc.Ice.Generator.Base.Tests.CustomSequence"] sequence<string> CustomStringSeq;
    ["cs:generic:IceRpc.Ice.Generator.Base.Tests.CustomSequence"] sequence<CustomStringSeq> CustomStringSeqSeq;

    struct SeqStruct
    {
        StringSeq stringSeq;
        StringSeqSeq stringSeqSeq;

        StringList stringList;
        StringListList stringListList;

        StringQueue stringQueue;
        StringQueueQueue stringQueueQueue;

        StringLinkedList stringLinkedList;
        StringLinkedListLinkedList stringLinkedListLinkedList;

        StringStack stringStack;
        StringStackStack stringStackStack;

        CustomStringSeq customStringSeq;
        CustomStringSeqSeq customStringSeqSeq;
    }
}
