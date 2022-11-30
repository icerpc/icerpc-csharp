// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::cs_attributes;
use crate::slicec_ext::*;

use slice::ast::node::Node;
use slice::compilation_result::{CompilationData, CompilationResult};
use slice::grammar::{Attribute, AttributeKind};

pub fn patch_attributes(mut compilation_data: CompilationData) -> CompilationResult {
    let ast = &mut compilation_data.ast;
    let diagnostic_reporter = &mut compilation_data.diagnostic_reporter;

    unsafe {
        ast.as_mut_slice().iter_mut().for_each(|node| match node {
            Node::Module(module_ptr) => patch_language_kind(&mut module_ptr.borrow_mut().attributes),
            Node::Struct(struct_ptr) => patch_language_kind(&mut struct_ptr.borrow_mut().attributes),
            Node::Class(class_ptr) => patch_language_kind(&mut class_ptr.borrow_mut().attributes),
            Node::Exception(exception_ptr) => patch_language_kind(&mut exception_ptr.borrow_mut().attributes),
            Node::DataMember(data_member_ptr) => {
                patch_language_kind(&mut data_member_ptr.borrow_mut().data_type.attributes);
                patch_language_kind(&mut data_member_ptr.borrow_mut().attributes);
            }
            Node::Interface(interface_ptr) => patch_language_kind(&mut interface_ptr.borrow_mut().attributes),
            Node::Operation(operation_ptr) => patch_language_kind(&mut operation_ptr.borrow_mut().attributes),
            Node::Parameter(parameter_ptr) => {
                patch_language_kind(&mut parameter_ptr.borrow_mut().data_type.attributes);
                patch_language_kind(&mut parameter_ptr.borrow_mut().attributes);
            }
            Node::Enum(enum_ptr) => patch_language_kind(&mut enum_ptr.borrow_mut().attributes),
            Node::Enumerator(enumerator_ptr) => patch_language_kind(&mut enumerator_ptr.borrow_mut().attributes),
            Node::CustomType(custom_type_ptr) => patch_language_kind(&mut custom_type_ptr.borrow_mut().attributes),
            Node::TypeAlias(type_alias_ptr) => patch_language_kind(&mut type_alias_ptr.borrow_mut().attributes),
            Node::Sequence(sequence_ptr) => patch_language_kind(&mut sequence_ptr.borrow_mut().element_type.attributes),
            Node::Dictionary(dictionary_ptr) => {
                patch_language_kind(&mut dictionary_ptr.borrow_mut().key_type.attributes);
                patch_language_kind(&mut dictionary_ptr.borrow_mut().value_type.attributes);
            }
            _ => (),
        });
    }

    compilation_data.into()
}

fn patch_language_kind(attributes: &mut [Attribute]) {
    for attribute in attributes.iter_mut() {
        match &attribute.kind {
            AttributeKind::Other { directive, arguments } if directive.starts_with(cs_attributes::ATTRIBUTE_PREFIX) => {
                attribute.kind = AttributeKind::LanguageKind {
                    directive: directive.clone(),
                    kind: Box::new(map_language_kind(directive, arguments)),
                };
            }
            _ => (),
        }
    }
}

fn map_language_kind(directive: &str, arguments: &[String]) -> CsAttributeKind {
    match directive {
        cs_attributes::ATTRIBUTE => CsAttributeKind::Attribute {
            attributes: arguments.to_vec(),
        },
        cs_attributes::ENCODED_RESULT => CsAttributeKind::EncodedResult,
        cs_attributes::GENERIC => CsAttributeKind::Generic {
            generic_type: arguments[0].clone(),
        },
        cs_attributes::IDENTIFIER => CsAttributeKind::Identifier {
            identifier: arguments[0].clone(),
        },
        cs_attributes::INTERNAL => CsAttributeKind::Internal,
        cs_attributes::NAMESPACE => CsAttributeKind::Namespace {
            namespace: arguments[0].clone(),
        },
        cs_attributes::READONLY => CsAttributeKind::Readonly,
        cs_attributes::TYPE => CsAttributeKind::Type {
            name: arguments[0].clone(),
        },

        _ => panic!("Unknown directive: {}", directive),
    }
}
