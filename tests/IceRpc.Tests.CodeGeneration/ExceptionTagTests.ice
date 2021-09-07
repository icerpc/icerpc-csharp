[[suppress-warning(reserved-identifier)]]

#pragma once

module IceRpc::Tests::CodeGeneration
{
    struct TaggedExceptionStruct
    {
        string s;
        int? v;
    }

    exception TaggedException
    {
        bool mBool;
        tag(1) int? mInt;
        tag(2) string? mString;
        tag(50) TaggedExceptionStruct? mStruct;
    }

    exception DerivedException : TaggedException
    {
        tag(600) string? mString1;
        tag(601) TaggedExceptionStruct? mStruct1;
    }

    exception RequiredException : TaggedException
    {
        string mString1;
        TaggedExceptionStruct mStruct1;
    }

    interface ExceptionTag
    {
        void opTaggedException(tag(1) int? p1, tag(2) string? p2, tag(3) TaggedExceptionStruct? p3);

        void opDerivedException(tag(1) int? p1, tag(2) string? p2, tag(3) TaggedExceptionStruct? p3);

        void opRequiredException(tag(1) int? p1, tag(2) string? p2, tag(3) TaggedExceptionStruct? p3);
    }
}
