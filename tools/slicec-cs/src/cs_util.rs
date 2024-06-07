// Copyright (c) ZeroC, Inc.

use crate::slicec_ext::EntityExt;
use convert_case::Case;
use slicec::grammar::{Entities, Message, MessageComponent, NamedSymbol};

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

pub trait CsCase {
    fn to_cs_case(&self, case: Case) -> String;
}

impl<T: AsRef<str>> CsCase for T
where
    String: PartialEq<T>,
{
    fn to_cs_case(&self, case: Case) -> String {
        cs_case(self.as_ref(), case)
    }
}

/// Convert the string using `convert_case::Casing::to_case`, then compare the
/// new string with the original string and update any two letter acronyms to be uppercase where
/// appropriate for the selected case.
///
/// Two letter acronym examples:
///   "IP" -> "IP"       // changed
///   "POBox" -> "POBox" // changed
///   "Ip" -> "Ip"       // unchanged
///   "PoBox" -> "PoBox" // unchanged
fn cs_case(original: &str, case: Case) -> String {
    // First convert the string to PascalCase and collect the characters.
    let converted = convert_case::Casing::to_case(&original, case);

    if !matches!(case, Case::Pascal | Case::Camel) {
        return converted;
    }

    let mut characters = converted.chars().collect::<Vec<_>>();

    // Replace underscores with nothing and collect the chars.
    // The size of this string should be the same as the converted string as
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

        // If we're converting the string to camel case then we need to skip the first
        // two letter acronym (if it exists).
        //
        // If the strings starts with a two letter acronym then the index will always be less
        // than the uppercase word length.
        //
        // AB - B at index=1, uppercase_word_len=2
        // ABCd - C at index 2, uppercase_word_len=3
        // aBC - C at index 2, uppercase_word_len=2
        // abCD - D at index 3, uppercase_word_len=1
        if case == Case::Camel && index < uppercase_word_len {
            continue;
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

pub fn format_comment_message(message: &Message, namespace: &str) -> String {
    // Iterate through the components of the message and append them into a string.
    // If the component is text, escape XML entities and then append it. If the component is a link, format it first,
    // then append it.
    let message_components = message.value.iter();
    message_components.fold(String::new(), |s, component| match &component {
        MessageComponent::Text(text) => s + &xml_escape(text),
        MessageComponent::Link(link_tag) => match link_tag.linked_entity() {
            Ok(entity) => {
                if let Entities::TypeAlias(type_alias) = entity.concrete_entity() {
                    // We don't generate any C# code for type-aliases, so if a user tries to link to one,
                    // instead of generating a `see` tag, we just output the type-alias' identifier as raw text.
                    s + type_alias.identifier()
                } else {
                    // If the link is to a valid (non type-alias) entity, run the link formatter on it.
                    s + &entity.get_formatted_link(namespace)
                }
            }

            // If the link was broken, just output it's raw text.
            Err(identifier) => s + &identifier.value,
        },
    })
}

fn xml_escape(text: &str) -> String {
    // We don't need to escape the single-quote character because 'slicec-cs' always generates double-quoted strings.
    text.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
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

    #[test_case("IP", "ip")]
    #[test_case("aIP", "aIP")]
    #[test_case("ABCDEf", "abcdEf")]
    #[test_case("POBox", "poBox")]
    #[test_case("aPOBox", "aPOBox")]
    #[test_case("abPOBox", "abPOBox")]
    #[test_case("TCPPacket", "tcpPacket")]

    fn cs_camel_case(input: &str, expected: &str) {
        assert_eq!(input.to_cs_case(Case::Camel), expected);
    }
}
