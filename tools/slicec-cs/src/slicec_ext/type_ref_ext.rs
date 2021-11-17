// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::code_gen_util::TypeContext;
use slice::grammar::*;

use super::entity_ext::EntityExt;
use super::interface_ext::InterfaceExt;
use super::primitive_ext::PrimitiveExt;

pub trait TypeRefExt {
    /// Is the type a reference type (eg. Class)
    fn is_reference_type(&self) -> bool;

    /// Is the type a value type (eg. Struct)
    fn is_value_type(&self) -> bool;

    /// The C# mapped type for this type reference.
    fn to_type_string(
        &self,
        namespace: &str,
        context: TypeContext,
        ignore_optional: bool,
    ) -> String;
}

impl<T: Type + ?Sized> TypeRefExt for TypeRef<T> {
    fn is_reference_type(&self) -> bool {
        !self.is_value_type()
    }

    fn is_value_type(&self) -> bool {
        match self.concrete_type() {
            Types::Primitive(primitive) => {
                !matches!(primitive, Primitive::String | Primitive::AnyClass)
            }
            Types::Enum(_) | Types::Struct(_) | Types::Interface(_) => true,
            _ => false,
        }
    }

    fn to_type_string(
        &self,
        namespace: &str,
        context: TypeContext,
        mut ignore_optional: bool,
    ) -> String {
        let type_str = match self.concrete_type() {
            Types::Struct(struct_def) => struct_def.escape_scoped_identifier(namespace),
            Types::Class(class_def) => class_def.escape_scoped_identifier(namespace),
            Types::Enum(enum_def) => enum_def.escape_scoped_identifier(namespace),
            Types::Interface(interface_def) => {
                interface_def.scoped_proxy_implementation_name(namespace)
            }
            Types::Sequence(sequence) => {
                sequence_type_to_string(self, sequence, namespace, context)
            }
            Types::Dictionary(dictionary) => {
                dictionary_type_to_string(self, dictionary, namespace, context)
            }
            Types::Primitive(primitive) => primitive.cs_keyword().to_owned(),
        };

        // For readonly sequences of fixed size numeric elements the mapping is the
        // same for optional an non optional types.
        if let Types::Sequence(sequence) = self.concrete_type() {
            if context == TypeContext::Outgoing
                && sequence.has_fixed_size_numeric_elements()
                && !self.has_attribute("cs:generic", false)
            {
                ignore_optional = true;
            }
        }

        if self.is_optional && !ignore_optional {
            type_str + "?"
        } else {
            type_str
        }
    }
}

/// Helper method to convert a sequence type into a string
fn sequence_type_to_string(
    type_ref: &TypeRef<impl Type + ?Sized>, // TODO change this to Sequence
    sequence: &Sequence,
    namespace: &str,
    context: TypeContext,
) -> String {
    let element_type = sequence
        .element_type
        .to_type_string(namespace, TypeContext::Nested, false);

    match context {
        TypeContext::DataMember | TypeContext::Nested => {
            format!("global::System.Collections.Generic.IList<{}>", element_type)
        }
        TypeContext::Incoming => match type_ref.get_attribute("cs:generic", false) {
            Some(args) => match args.first().unwrap().as_str() {
                value @ "List" | value @ "LinkedList" | value @ "Queue" | value @ "Stack" => {
                    format!(
                        "global::System.Collections.Generic.{}<{}>",
                        value, element_type
                    )
                }
                value => format!("{}<{}>", value, element_type),
            },
            None => format!("{}[]", element_type),
        },
        TypeContext::Outgoing => {
            // If the underlying type is of fixed size, we map to `ReadOnlyMemory` instead.
            if sequence.has_fixed_size_numeric_elements()
                && !type_ref.has_attribute("cs:generic", false)
            {
                format!("global::System.ReadOnlyMemory<{}>", element_type)
            } else {
                format!(
                    "global::System.Collections.Generic.IEnumerable<{}>",
                    element_type
                )
            }
        }
    }
}

/// Helper method to convert a dictionary type into a string
fn dictionary_type_to_string(
    type_ref: &TypeRef<impl Type + ?Sized>, // TODO change this to Dictionary
    dictionary: &Dictionary,
    namespace: &str,
    context: TypeContext,
) -> String {
    let key_type = dictionary
        .key_type
        .to_type_string(namespace, TypeContext::Nested, false);
    let value_type = dictionary
        .value_type
        .to_type_string(namespace, TypeContext::Nested, false);

    match context {
        TypeContext::DataMember | TypeContext::Nested => {
            format!(
                "global::System.Collections.Generic.IDictionary<{}, {}>",
                key_type, value_type,
            )
        }
        TypeContext::Incoming => match type_ref.get_attribute("cs:generic", false) {
            Some(args) => {
                let prefix = match args.first().unwrap().as_str() {
                    "SortedDictionary" => "global::System.Collections.Generic.",
                    _ => "",
                };
                format!(
                    "{}{}<{}, {}>",
                    prefix,
                    args.first().unwrap(),
                    key_type,
                    value_type
                )
            }
            None => format!(
                "global::System.Collections.Generic.Dictionary<{}, {}>",
                key_type, value_type,
            ),
        },
        TypeContext::Outgoing => {
            format!(
                "global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<{}, {}>>",
                key_type, value_type,
            )
        }
    }
}
