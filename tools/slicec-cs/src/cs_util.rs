// Copyright (c) ZeroC, Inc.

use convert_case::Case;

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
    (if CS_KEYWORDS.contains(&identifier) { "@" } else { "" }.to_owned()) + identifier
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
/// escapes it if necessary by appending a "Slice" prefix to the identifier.
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
        FieldType::Exception => OBJECT_BASE_NAMES.contains(&identifier) | EXCEPTION_BASE_NAMES.contains(&identifier),
        FieldType::Class => OBJECT_BASE_NAMES.contains(&identifier),
        FieldType::NonMangled => false,
    };

    // If the name conflicts with a base method, add a Slice prefix to it.
    if needs_mangling {
        format!("Slice{identifier}")
    } else {
        identifier.to_owned()
    }
}

pub trait CsCase {
    fn to_cs_case(&self, case: Case) -> String;
}

impl<T: AsRef<str>> CsCase for T
where
    String: PartialEq<T>,
{
    fn to_cs_case(&self, case: Case) -> String {
        match case {
            Case::Pascal => cs_pascal_case(self.as_ref()),
            _ => convert_case::Casing::to_case(self, case),
        }
    }
}

// Converts a string to PascalCase using and preserves the original case of any two letter
// acronyms.
/// The behavior of `convert_case::Casing::to_case` is to convert all letter acronyms to the form
/// where the first letter is uppercase and the rest are lowercase.
///
/// Two letter acronym examples:
///   "IP" -> "IP"
///   "POBox" -> "POBox"
fn cs_pascal_case(original: &str) -> String {
    // First convert the string to PascalCase and collect the characters.
    let converted = convert_case::Casing::to_case(&original, Case::Pascal);
    let mut characters = converted.chars().collect::<Vec<_>>();

    // Replace underscores with nothing and collect the chars.
    // This size of this string should be the same as the converted string as
    // PascalCase strings do not have underscores.
    let original_chars = original.chars().filter(|c| *c != '_').collect::<Vec<_>>();
    assert_eq!(original_chars.len(), converted.len());

    // The number of consecutive uppercase letters we've seen.
    let mut uppercase_word_len = 0;

    // Iterator over the characters of the converted string and the original string.
    let mut iter = original_chars.iter().enumerate().peekable();

    while let Some((index, original_char)) = iter.next() {
        if original_char.is_uppercase() {
            uppercase_word_len += 1;
        } else {
            // We have a lowercase letter, reset the uppercase word length.
            uppercase_word_len = 0;
        }

        let next_char = iter.peek();

        match next_char {
            // If this is the third uppercase character followed by a lowercase character
            // then we have a string like "ABCd". In this case we want to replace the
            // previous character with the original uppercase character.
            Some((_, next_char)) if uppercase_word_len == 3 && next_char.is_lowercase() => {
                let pos = index - 1;
                characters[pos] = original_chars[pos];
            }

            // If this is the second uppercase letter and we're at the end of the string
            // then replace the last character with the original uppercase character.
            // This means we have a string like "AbCD" or "AB".
            None if uppercase_word_len == 2 => {
                characters[index] = *original_char;
            }

            // Otherwise do nothing.
            _ => {}
        }
    }

    characters.into_iter().collect()
}

#[cfg(test)]
mod test {
    use super::*;
    use test_case::test_case;

    #[test_case("IP", "IP"; "two_letter_uppercase")]
    #[test_case("ip", "Ip"; "two_letter_lowercase")]
    #[test_case("Ip", "Ip"; "two_letter_mixed")]
    #[test_case("POBox", "POBox"; "two_letter_uppercase_acronym_at_start")]
    #[test_case("po_box", "PoBox"; "two_letter_lowercase_acronym_at_start")]
    #[test_case("TCP", "Tcp"; "three_letter_uppercase")]
    #[test_case("tcp", "Tcp"; "three_letter_lowercase")]
    #[test_case("TcpIPPacket", "TcpIPPacket"; "two_letter_acronym_in_middle")]
    #[test_case("AbCD", "AbCD"; "two_letter_acronym_at_end")]
    fn cs_pascal_case(input: &str, expected: &str) {
        assert_eq!(input.to_cs_case(Case::Pascal), expected);
    }
}
