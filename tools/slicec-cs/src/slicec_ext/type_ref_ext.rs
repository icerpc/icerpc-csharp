// Copyright (c) ZeroC, Inc.

use super::{EntityExt, EnumExt, PrimitiveExt};
use crate::cs_attributes::CsType;
use slicec::grammar::*;
use slicec::utils::code_gen_util::TypeContext;

pub trait TypeRefExt {
    /// Is this type known to map to a C# value type?
    fn is_value_type(&self) -> bool;

    /// The C# mapped type for this type reference.
    fn cs_type_string(&self, namespace: &str, context: TypeContext, ignore_optional: bool) -> String;
}

impl<T: Type + ?Sized> TypeRefExt for TypeRef<T> {
    fn is_value_type(&self) -> bool {
        match self.concrete_type() {
            Types::Primitive(primitive) => !matches!(primitive, Primitive::String | Primitive::AnyClass),
            Types::Struct(_) => true,
            Types::Enum(enum_ref) => enum_ref.is_mapped_to_cs_enum(),
            _ => false,
        }
    }

    fn cs_type_string(&self, namespace: &str, context: TypeContext, mut ignore_optional: bool) -> String {
        let type_str = match &self.concrete_typeref() {
            TypeRefs::Struct(struct_ref) => struct_ref.escape_scoped_identifier(namespace),
            TypeRefs::Class(class_ref) => class_ref.escape_scoped_identifier(namespace),
            TypeRefs::Enum(enum_ref) => enum_ref.escape_scoped_identifier(namespace),
            TypeRefs::ResultType(result_type_ref) => result_type_to_string(result_type_ref, namespace),
            TypeRefs::CustomType(custom_type_ref) => {
                let attribute = custom_type_ref.definition().find_attribute::<CsType>();
                attribute.unwrap().type_string.clone()
            }
            TypeRefs::Sequence(sequence_ref) => {
                // For readonly sequences of fixed size numeric elements the mapping is the
                // same for optional an non optional types.
                if context == TypeContext::OutgoingParam
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
        .cs_type_string(namespace, TypeContext::Field, false);

    let cs_type_attribute = sequence_ref.find_attribute::<CsType>();

    match context {
        TypeContext::Field => {
            format!("global::System.Collections.Generic.IList<{element_type}>")
        }
        TypeContext::IncomingParam => match cs_type_attribute {
            Some(arg) => arg.type_string.clone(),
            None => format!("{element_type}[]"),
        },
        TypeContext::OutgoingParam => {
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
        .cs_type_string(namespace, TypeContext::Field, false);
    let value_type = dictionary_ref
        .value_type
        .cs_type_string(namespace, TypeContext::Field, false);

    let cs_type_attribute = dictionary_ref.find_attribute::<CsType>();

    match context {
        TypeContext::Field => {
            format!("global::System.Collections.Generic.IDictionary<{key_type}, {value_type}>")
        }
        TypeContext::IncomingParam => match cs_type_attribute {
            Some(arg) => arg.type_string.clone(),
            None => format!("global::System.Collections.Generic.Dictionary<{key_type}, {value_type}>"),
        },
        TypeContext::OutgoingParam =>
            format!(
                "global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<{key_type}, {value_type}>>"
            )
    }
}

/// Helper method to convert a result type into a string
fn result_type_to_string(result_type_ref: &TypeRef<ResultType>, namespace: &str) -> String {
    let success_type = result_type_ref
        .success_type
        .cs_type_string(namespace, TypeContext::Field, false);
    let failure_type = result_type_ref
        .failure_type
        .cs_type_string(namespace, TypeContext::Field, false);

    format!("Result<{success_type}, {failure_type}>")
}
