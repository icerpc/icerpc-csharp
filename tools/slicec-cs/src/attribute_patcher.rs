// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::cs_attributes;
use crate::slicec_ext::*;

use slice::ast::node::Node;
use slice::compilation_result::{CompilationData, CompilationResult};
use slice::diagnostics::{DiagnosticReporter, Error, ErrorKind};
use slice::grammar::{Attribute, AttributeKind};
use slice::slice_file::Span;

pub fn patch_attributes(mut compilation_data: CompilationData) -> CompilationResult {
    let ast = &mut compilation_data.ast;
    let diagnostic_reporter = &mut compilation_data.diagnostic_reporter;

    unsafe {
        ast.as_mut_slice().iter_mut().for_each(|node| match node {
            Node::Module(module_ptr) => {
                patch_language_kind(&mut module_ptr.borrow_mut().attributes, diagnostic_reporter)
            }
            Node::Struct(struct_ptr) => {
                patch_language_kind(&mut struct_ptr.borrow_mut().attributes, diagnostic_reporter)
            }
            Node::Class(class_ptr) => patch_language_kind(&mut class_ptr.borrow_mut().attributes, diagnostic_reporter),
            Node::Exception(exception_ptr) => {
                patch_language_kind(&mut exception_ptr.borrow_mut().attributes, diagnostic_reporter)
            }
            Node::DataMember(data_member_ptr) => {
                patch_language_kind(
                    &mut data_member_ptr.borrow_mut().data_type.attributes,
                    diagnostic_reporter,
                );
                patch_language_kind(&mut data_member_ptr.borrow_mut().attributes, diagnostic_reporter);
            }
            Node::Interface(interface_ptr) => {
                patch_language_kind(&mut interface_ptr.borrow_mut().attributes, diagnostic_reporter)
            }
            Node::Operation(operation_ptr) => {
                patch_language_kind(&mut operation_ptr.borrow_mut().attributes, diagnostic_reporter)
            }
            Node::Parameter(parameter_ptr) => {
                patch_language_kind(
                    &mut parameter_ptr.borrow_mut().data_type.attributes,
                    diagnostic_reporter,
                );
                patch_language_kind(&mut parameter_ptr.borrow_mut().attributes, diagnostic_reporter);
            }
            Node::Enum(enum_ptr) => patch_language_kind(&mut enum_ptr.borrow_mut().attributes, diagnostic_reporter),
            Node::Enumerator(enumerator_ptr) => {
                patch_language_kind(&mut enumerator_ptr.borrow_mut().attributes, diagnostic_reporter)
            }
            Node::CustomType(custom_type_ptr) => {
                patch_language_kind(&mut custom_type_ptr.borrow_mut().attributes, diagnostic_reporter)
            }
            Node::TypeAlias(type_alias_ptr) => {
                patch_language_kind(&mut type_alias_ptr.borrow_mut().attributes, diagnostic_reporter)
            }
            Node::Sequence(sequence_ptr) => patch_language_kind(
                &mut sequence_ptr.borrow_mut().element_type.attributes,
                diagnostic_reporter,
            ),
            Node::Dictionary(dictionary_ptr) => {
                patch_language_kind(
                    &mut dictionary_ptr.borrow_mut().key_type.attributes,
                    diagnostic_reporter,
                );
                patch_language_kind(
                    &mut dictionary_ptr.borrow_mut().value_type.attributes,
                    diagnostic_reporter,
                );
            }
            _ => (),
        });
    }

    compilation_data.into()
}

fn patch_language_kind(attributes: &mut [Attribute], diagnostic_reporter: &mut DiagnosticReporter) {
    for attribute in attributes.iter_mut() {
        match &attribute.kind {
            AttributeKind::Other { directive, arguments } if directive.starts_with(cs_attributes::ATTRIBUTE_PREFIX) => {
                attribute.kind = map_language_kind(directive, arguments, &attribute.span, diagnostic_reporter)
            }
            _ => (),
        }
    }
}

fn map_language_kind(
    directive: &str,
    arguments: &[String],
    span: &Span,
    diagnostic_reporter: &mut DiagnosticReporter,
) -> AttributeKind {
    // Check for known attributes, if a parsing error occurs return an unknown attribute.
    let unmatched_attribute = AttributeKind::Other {
        directive: directive.to_owned(),
        arguments: arguments.to_owned(),
    };

    match directive {
        cs_attributes::ATTRIBUTE => single_argument(directive, arguments, span, diagnostic_reporter)
            .map_or(unmatched_attribute, |argument| {
                CsAttributeKind::Attribute { attribute: argument }.into()
            }),
        cs_attributes::ENCODED_RESULT => match no_arguments(directive, arguments, span, diagnostic_reporter) {
            true => CsAttributeKind::EncodedResult.into(),
            false => unmatched_attribute,
        },
        cs_attributes::GENERIC => single_argument(directive, arguments, span, diagnostic_reporter)
            .map_or(unmatched_attribute, |argument| {
                CsAttributeKind::Generic { generic_type: argument }.into()
            }),
        cs_attributes::IDENTIFIER => single_argument(directive, arguments, span, diagnostic_reporter)
            .map_or(unmatched_attribute, |argument| {
                CsAttributeKind::Identifier { identifier: argument }.into()
            }),
        cs_attributes::INTERNAL => match no_arguments(directive, arguments, span, diagnostic_reporter) {
            true => CsAttributeKind::Internal.into(),
            false => unmatched_attribute,
        },
        cs_attributes::NAMESPACE => single_argument(directive, arguments, span, diagnostic_reporter)
            .map_or(unmatched_attribute, |argument| {
                CsAttributeKind::Namespace { namespace: argument }.into()
            }),
        cs_attributes::READONLY => match no_arguments(directive, arguments, span, diagnostic_reporter) {
            true => CsAttributeKind::Readonly.into(),
            false => unmatched_attribute,
        },
        cs_attributes::TYPE => single_argument(directive, arguments, span, diagnostic_reporter)
            .map_or(unmatched_attribute, |argument| {
                CsAttributeKind::Type { name: argument }.into()
            }),

        _ => {
            diagnostic_reporter.report_error(Error::new(
                ErrorKind::UnexpectedAttribute(directive.to_owned()),
                Some(span),
            ));
            unmatched_attribute
        }
    }
}

fn no_arguments(
    directive: &str,
    arguments: &[String],
    span: &Span,
    diagnostic_reporter: &mut DiagnosticReporter,
) -> bool {
    if arguments.is_empty() {
        true
    } else {
        error_too_many(directive.to_owned(), span, diagnostic_reporter);
        false
    }
}

fn single_argument(
    directive: &str,
    arguments: &[String],
    span: &Span,
    diagnostic_reporter: &mut DiagnosticReporter,
) -> Option<String> {
    match arguments {
        [argument] => Some(argument.clone()),
        [] => {
            error_missing(directive.to_owned() + r#"("<argument>")"#, span, diagnostic_reporter);
            None
        }
        [..] => {
            error_too_many(directive.to_owned() + r#"("<argument>")"#, span, diagnostic_reporter);
            None
        }
    }
}

fn error_missing(required_argument: String, span: &Span, diagnostic_reporter: &mut DiagnosticReporter) {
    diagnostic_reporter.report_error(Error::new(ErrorKind::TooManyArguments(required_argument), Some(span)));
}
fn error_too_many(expected: String, span: &Span, diagnostic_reporter: &mut DiagnosticReporter) {
    diagnostic_reporter.report_error(Error::new(ErrorKind::TooManyArguments(expected), Some(span)));
}
