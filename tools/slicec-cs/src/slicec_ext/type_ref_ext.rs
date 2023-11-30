// Copyright (c) ZeroC, Inc.

use super::{EntityExt, PrimitiveExt};
use crate::cs_attributes::CsType;
use slicec::grammar::*;
use slicec::utils::code_gen_util::TypeContext;

pub trait TypeRefExt {
    /// Is this type known to map to a C# value type?
    fn is_value_type(&self) -> bool;

    /// Is this type known to map to a C# reference type?
    /// It's the opposite of `is_value_type()` except both `is_value_type()` and `is_reference_type()` return false for
    /// a custom type since we don't know what the user selected.
    fn is_reference_type(&self) -> bool;

    /// The C# mapped type for this type reference.
    fn cs_type_string(&self, namespace: &str, context: TypeContext, ignore_optional: bool) -> String;
}

impl<T: Type + ?Sized> TypeRefExt for TypeRef<T> {
    fn is_value_type(&self) -> bool {
        match self.concrete_type() {
            Types::Primitive(primitive) => !matches!(primitive, Primitive::String | Primitive::AnyClass),
            Types::Enum(_) | Types::Struct(_) => true,
            _ => false,
        }
    }

    fn is_reference_type(&self) -> bool {
        !self.is_value_type() && !matches!(self.concrete_type(), Types::CustomType(_))
    }

    fn cs_type_string(&self, namespace: &str, context: TypeContext, mut ignore_optional: bool) -> String {
        let type_str = match &self.concrete_typeref() {
            TypeRefs::Struct(struct_ref) => struct_ref.escape_scoped_identifier(namespace),
            TypeRefs::Class(class_ref) => class_ref.escape_scoped_identifier(namespace),
            TypeRefs::Enum(enum_ref) => enum_ref.escape_scoped_identifier(namespace),
            TypeRefs::CustomType(custom_type_ref) => {
                let attribute = custom_type_ref.definition().find_attribute::<CsType>();
                attribute.unwrap().type_string.clone()
            }
            TypeRefs::Sequence(sequence_ref) => {
                // For readonly sequences of fixed size numeric elements the mapping is the
                // same for optional an non optional types.
                if context == TypeContext::Encode
                    && sequence_ref.has_fixed_size_primitive_elements()
                    && !self.has_attribute::<CsType>()
                {
                    ignore_optional = true;
                }
                sequence_type_to_string(sequence_ref, namespace, context)
            }
            TypeRefs::Dictionary(dictionary_ref) => dictionary_type_to_string(dictionary_ref, namespace, context),
            TypeRefs::Primitive(primitive_ref) => primitive_ref.cs_type().to_owned(),
        };

        if self.is_optional && !ignore_optional {
            type_str + "?"
        } else {
            type_str
        }
    }
}

/// Helper method to convert a sequence type into a string
fn sequence_type_to_string(sequence_ref: &TypeRef<Sequence>, namespace: &str, context: TypeContext) -> String {
    let element_type = sequence_ref
        .element_type
        .cs_type_string(namespace, TypeContext::Nested, false);

    let cs_type_attribute = sequence_ref.find_attribute::<CsType>();

    match context {
        TypeContext::Field | TypeContext::Nested => {
            format!("global::System.Collections.Generic.IList<{element_type}>")
        }
        TypeContext::Decode => match cs_type_attribute {
            Some(arg) => arg.type_string.clone(),
            None => format!("{element_type}[]"),
        },
        TypeContext::Encode => {
            // If the underlying type is of fixed size, we map to `ReadOnlyMemory` instead.
            if sequence_ref.has_fixed_size_primitive_elements() && cs_type_attribute.is_none() {
                format!("global::System.ReadOnlyMemory<{element_type}>")
            } else {
                format!("global::System.Collections.Generic.IEnumerable<{element_type}>")
            }
        }
    }
}

/// Helper method to convert a dictionary type into a string
fn dictionary_type_to_string(dictionary_ref: &TypeRef<Dictionary>, namespace: &str, context: TypeContext) -> String {
    let key_type = dictionary_ref
        .key_type
        .cs_type_string(namespace, TypeContext::Nested, false);
    let value_type = dictionary_ref
        .value_type
        .cs_type_string(namespace, TypeContext::Nested, false);

    let cs_type_attribute = dictionary_ref.find_attribute::<CsType>();

    match context {
        TypeContext::Field | TypeContext::Nested => {
            format!("global::System.Collections.Generic.IDictionary<{key_type}, {value_type}>")
        }
        TypeContext::Decode => match cs_type_attribute {
            Some(arg) => arg.type_string.clone(),
            None => format!("global::System.Collections.Generic.Dictionary<{key_type}, {value_type}>"),
        },
        TypeContext::Encode =>
            format!(
                "global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<{key_type}, {value_type}>>"
            )
    }
}
