[[suppress-warning(reserved-identifier)]]

#pragma once

#include <StructTests.ice>

module IceRpc::Tests::CodeGeneration
{
    exception TaggedException
    {
        bool mBool;
        tag(1) int? mInt;
        tag(2) string? mString;
        tag(50) AnotherStruct? mAnotherStruct;
    }

    exception DerivedException : TaggedException
    {
        tag(600) string? mString1;
        tag(601) AnotherStruct? mAnotherStruct1;
    }

    exception RequiredException : TaggedException
    {
        string mString1;
        AnotherStruct mAnotherStruct1;
    }

    interface ExceptionTag
    {
        void opTaggedException(tag(1) int? p1, tag(2) string? p2, tag(3) AnotherStruct? p3);

        void opDerivedException(tag(1) int? p1, tag(2) string? p2, tag(3) AnotherStruct? p3);

        void opRequiredException(tag(1) int? p1, tag(2) string? p2, tag(3) AnotherStruct? p3);
    }
}
