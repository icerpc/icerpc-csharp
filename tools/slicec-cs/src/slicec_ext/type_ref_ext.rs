// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::ast::{Ast, Node};
use slice::grammar::*;
use slice::util::TypeContext;

use super::interface_ext::InterfaceExt;
use super::named_symbol_ext::NamedSymbolExt;

pub trait TypeRefExt {
    /// Is the type a reference type (eg. Class)
    fn is_reference_type(&self, ast: &Ast) -> bool;

    /// Is the type a value type (eg. Struct)
    fn is_value_type(&self, ast: &Ast) -> bool;

    /// The C# mapped type for this type reference.
    fn to_type_string(&self, namespace: &str, ast: &Ast, context: TypeContext) -> String;
}

impl TypeRefExt for TypeRef {
    fn is_reference_type(&self, ast: &Ast) -> bool {
        !self.is_value_type(ast)
    }

    fn is_value_type(&self, ast: &Ast) -> bool {
        match self.definition(ast) {
            Node::Primitive(_, primitive) => !matches!(primitive, Primitive::String),
            Node::Enum(_, _) | Node::Struct(_, _) | Node::Interface(_, _) => true,
            _ => false,
        }
    }

    fn to_type_string(&self, namespace: &str, ast: &Ast, context: TypeContext) -> String {
        let type_str = match self.definition(ast) {
            Node::Struct(_, struct_def) => struct_def.escape_scoped_identifier(namespace),
            Node::Class(_, class_def) => class_def.escape_scoped_identifier(namespace),
            Node::Exception(_, exception_def) => exception_def.escape_scoped_identifier(namespace),
            Node::Enum(_, enum_def) => enum_def.escape_scoped_identifier(namespace),
            Node::Interface(_, interface_def) => {
                interface_def.scoped_proxy_implementation_name(namespace)
            }
            Node::Sequence(_, sequence) => {
                sequence_type_to_string(self, sequence, namespace, ast, context)
            }
            Node::Dictionary(_, dictionary) => {
                dictionary_type_to_string(self, dictionary, namespace, ast, context)
            }
            Node::Primitive(_, primitive) => match primitive {
                Primitive::Bool => "bool",
                Primitive::Byte => "byte",
                Primitive::Short => "short",
                Primitive::UShort => "ushort",
                Primitive::Int => "int",
                Primitive::UInt => "uint",
                Primitive::VarInt => "int",
                Primitive::VarUInt => "uint",
                Primitive::Long => "long",
                Primitive::ULong => "ulong",
                Primitive::VarLong => "long",
                Primitive::VarULong => "ulong",
                Primitive::Float => "float",
                Primitive::Double => "double",
                Primitive::String => "string",
            }
            .to_owned(),
            node => {
                panic!("Node does not represent a type: '{:?}'!", node);
            }
        };

        if self.is_streamed {
            match type_str.as_str() {
                "byte" => "global::System.IO.Stream".to_owned(),
                _ => format!(
                    "global::System.Collections.Generic.IAsyncEnumerable<{}>",
                    type_str
                ),
            }
        } else if self.is_optional {
            format!("{}?", type_str)
        } else {
            type_str
        }
    }
}

/// Helper method to convert a sequence type into a string
fn sequence_type_to_string(
    type_ref: &TypeRef,
    sequence: &Sequence,
    namespace: &str,
    ast: &Ast,
    context: TypeContext,
) -> String {
    let element_type = sequence
        .element_type
        .to_type_string(namespace, ast, TypeContext::Nested);

    match context {
        TypeContext::DataMember | TypeContext::Nested => {
            format!("global::System.Collections.Generic.IList<{}>", element_type)
        }
        TypeContext::Incoming => match type_ref.find_attribute("cs:generic") {
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
            if is_fixed_size_numeric_sequence(sequence, ast) {
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

fn is_fixed_size_numeric_sequence(sequence: &Sequence, ast: &Ast) -> bool {
    match sequence.element_type.definition(ast) {
        Node::Primitive(_, primitive) if primitive.is_fixed_size(ast) => true,
        Node::Enum(_, enum_def) => matches!(&enum_def.underlying, Some(t) if t.is_fixed_size(ast)),
        _ => false,
    }
}

/// Helper method to convert a dictionary type into a string
fn dictionary_type_to_string(
    type_ref: &TypeRef,
    dictionary: &Dictionary,
    namespace: &str,
    ast: &Ast,
    context: TypeContext,
) -> String {
    let key_type = &dictionary
        .key_type
        .to_type_string(namespace, ast, TypeContext::Nested);
    let value_type = dictionary
        .value_type
        .to_type_string(namespace, ast, TypeContext::Nested);

    match context {
        TypeContext::DataMember | TypeContext::Nested => {
            format!(
                "global::System.Collections.Generic.IDictionary<{}, {}>",
                key_type, value_type,
            )
        }
        TypeContext::Incoming => match type_ref.find_attribute("cs:generic") {
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
