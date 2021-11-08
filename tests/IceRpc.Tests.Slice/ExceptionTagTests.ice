[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::Slice
{
    struct TaggedExceptionStruct
    {
        string s;
        int? v;
    }

    exception TaggedException
    {
        tag(50) TaggedExceptionStruct? mStruct;
        tag(1) int? mInt;
        bool mBool;
        tag(2) string? mString;
    }

    exception TaggedExceptionPlus
    {
        tag(3) float? mFloat;
        tag(50) TaggedExceptionStruct? mStruct;
        tag(1) int? mInt;
        bool mBool;
        tag(2) string? mString;
    }

    exception TaggedExceptionMinus
    {
        bool mBool;
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
