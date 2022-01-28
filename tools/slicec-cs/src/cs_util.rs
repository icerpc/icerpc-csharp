// Copyright (c) ZeroC, Inc. All rights reserved.

/// Checks if the provided string is a C# keyword, and escapes it if necessary (by appending a '@').
pub fn escape_keyword(identifier: &str) -> String {
    const CS_KEYWORDS: [&str; 79] = [
        "abstract",
        "as",
        "async",
        "await",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
    ];

    // Add a '@' prefix if the identifier matched a C# keyword.
    (if CS_KEYWORDS.contains(&identifier) {
        "@"
    } else {
        ""
    }
    .to_owned())
        + identifier
}

/// The field container type, Class, Exception or NonMangled (currently used for structs),
/// `mangle_name` operation use this enum to decide what names needs mangling.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum FieldType {
    NonMangled,
    Class,
    Exception,
}

/// Checks if the provided identifier would shadow a base method in an object or exception, and
/// escapes it if necessary by appending a Slice prefix to the identifier.
pub fn mangle_name(identifier: &str, field_type: FieldType) -> String {
    // The names of all the methods defined on the Object base class.
    const OBJECT_BASE_NAMES: [&str; 7] = [
        "Equals",
        "Finalize",
        "GetHashCode",
        "GetType",
        "MemberwiseClone",
        "ReferenceEquals",
        "ToString",
    ];
    // The names of all the methods and properties defined on the Exception base class.
    const EXCEPTION_BASE_NAMES: [&str; 10] = [
        "Data",
        "GetBaseException",
        "GetObjectData",
        "HelpLink",
        "HResult",
        "InnerException",
        "Message",
        "Source",
        "StackTrace",
        "TargetSite",
    ];

    let needs_mangling = match field_type {
        FieldType::Exception => {
            OBJECT_BASE_NAMES.contains(&identifier) | EXCEPTION_BASE_NAMES.contains(&identifier)
        }
        FieldType::Class => OBJECT_BASE_NAMES.contains(&identifier),
        FieldType::NonMangled => false,
    };

    // If the name conflicts with a base method, add a Slice prefix to it.
    if needs_mangling {
        format!("Slice{}", identifier)
    } else {
        identifier.to_owned()
    }
}
