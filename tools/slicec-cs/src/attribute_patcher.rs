// Copyright (c) ZeroC, Inc.

use crate::cs_attributes;
use crate::cs_attributes::CsAttributeKind;

use slice::ast::node::Node;
use slice::compilation_result::{CompilationData, CompilationResult};
use slice::diagnostics::{DiagnosticReporter, Error, ErrorKind};
use slice::grammar::{Attribute, AttributeKind};
use slice::slice_file::Span;

pub fn patch_attributes(mut compilation_data: CompilationData) -> CompilationResult {
    let ast = &mut compilation_data.ast;
    let reporter = &mut compilation_data.diagnostic_reporter;

    let mut patcher = AttributePatcher { reporter };

    ast.as_mut_slice().iter_mut().for_each(|node| {
        patcher.patch_node(node);
    });

    compilation_data.into()
}

struct GeneralAttribute<'a> {
    directive: &'a str,
    arguments: &'a [String],
    span: &'a Span,
}

struct AttributePatcher<'a> {
    reporter: &'a mut DiagnosticReporter,
}

impl AttributePatcher<'_> {
    fn patch_node(&mut self, node: &mut Node) {
        unsafe {
            match node {
                Node::Module(module_ptr) => self.patch_attributes(&mut module_ptr.borrow_mut().attributes),
                Node::Struct(struct_ptr) => self.patch_attributes(&mut struct_ptr.borrow_mut().attributes),
                Node::Class(class_ptr) => self.patch_attributes(&mut class_ptr.borrow_mut().attributes),
                Node::Exception(exception_ptr) => self.patch_attributes(&mut exception_ptr.borrow_mut().attributes),
                Node::Field(field_ptr) => {
                    self.patch_attributes(&mut field_ptr.borrow_mut().data_type.attributes);
                    self.patch_attributes(&mut field_ptr.borrow_mut().attributes);
                }
                Node::Interface(interface_ptr) => self.patch_attributes(&mut interface_ptr.borrow_mut().attributes),
                Node::Operation(operation_ptr) => self.patch_attributes(&mut operation_ptr.borrow_mut().attributes),
                Node::Parameter(parameter_ptr) => {
                    self.patch_attributes(&mut parameter_ptr.borrow_mut().data_type.attributes);
                    self.patch_attributes(&mut parameter_ptr.borrow_mut().attributes);
                }
                Node::Enum(enum_ptr) => self.patch_attributes(&mut enum_ptr.borrow_mut().attributes),
                Node::Enumerator(enumerator_ptr) => self.patch_attributes(&mut enumerator_ptr.borrow_mut().attributes),
                Node::CustomType(custom_type_ptr) => {
                    self.patch_attributes(&mut custom_type_ptr.borrow_mut().attributes)
                }
                Node::TypeAlias(type_alias_ptr) => self.patch_attributes(&mut type_alias_ptr.borrow_mut().attributes),
                Node::Sequence(sequence_ptr) => {
                    self.patch_attributes(&mut sequence_ptr.borrow_mut().element_type.attributes)
                }
                Node::Dictionary(dictionary_ptr) => {
                    self.patch_attributes(&mut dictionary_ptr.borrow_mut().key_type.attributes);
                    self.patch_attributes(&mut dictionary_ptr.borrow_mut().value_type.attributes);
                }
                _ => (),
            }
        }
    }

    fn patch_attributes(&mut self, attributes: &mut [Attribute]) {
        for attribute in attributes.iter_mut() {
            match &attribute.kind {
                AttributeKind::Other { directive, arguments }
                    if directive.starts_with(cs_attributes::ATTRIBUTE_PREFIX) =>
                {
                    if let Some(cs_attribute) = self.map_language_kind(&GeneralAttribute {
                        directive,
                        arguments,
                        span: &attribute.span,
                    }) {
                        attribute.kind = cs_attribute.into()
                    }
                }
                _ => (),
            }
        }
    }

    fn map_language_kind(&mut self, attribute: &GeneralAttribute) -> Option<CsAttributeKind> {
        match attribute.directive {
            cs_attributes::ATTRIBUTE => self
                .single_argument(attribute)
                .map(|argument| CsAttributeKind::Attribute { attribute: argument }),
            cs_attributes::ENCODED_RESULT => match self.no_arguments(attribute) {
                true => Some(CsAttributeKind::EncodedResult),
                false => None,
            },
            cs_attributes::GENERIC => self
                .single_argument(attribute)
                .map(|argument| CsAttributeKind::Generic { generic_type: argument }),
            cs_attributes::IDENTIFIER => self
                .single_argument(attribute)
                .map(|argument| CsAttributeKind::Identifier { identifier: argument }),
            cs_attributes::INTERNAL => match self.no_arguments(attribute) {
                true => Some(CsAttributeKind::Internal),
                false => None,
            },
            cs_attributes::NAMESPACE => self
                .single_argument(attribute)
                .map(|argument| CsAttributeKind::Namespace { namespace: argument }),
            cs_attributes::READONLY => match self.no_arguments(attribute) {
                true => Some(CsAttributeKind::Readonly),
                false => None,
            },
            cs_attributes::CUSTOM => self
                .single_argument(attribute)
                .map(|argument| CsAttributeKind::Custom { name: argument }),
            _ => {
                Error::new(ErrorKind::UnexpectedAttribute {
                    attribute: attribute.directive.to_owned(),
                })
                .set_span(attribute.span)
                .report(self.reporter);
                None
            }
        }
    }

    // Helper functions to check the number of arguments of an attribute and report any
    // associated diagnostics.

    fn no_arguments(&mut self, attribute: &GeneralAttribute) -> bool {
        if attribute.arguments.is_empty() {
            true
        } else {
            self.error_too_many(attribute.directive.to_owned(), attribute.span);
            false
        }
    }

    fn single_argument(&mut self, attribute: &GeneralAttribute) -> Option<String> {
        match attribute.arguments {
            [argument] => Some(argument.clone()),
            [] => {
                self.error_missing(attribute.directive.to_owned() + r#"("<argument>")"#, attribute.span);
                None
            }
            [..] => {
                self.error_too_many(attribute.directive.to_owned() + r#"("<argument>")"#, attribute.span);
                None
            }
        }
    }

    fn error_missing(&mut self, required_argument: String, span: &Span) {
        Error::new(ErrorKind::MissingRequiredArgument {
            argument: required_argument,
        })
        .set_span(span)
        .report(self.reporter);
    }

    fn error_too_many(&mut self, expected: String, span: &Span) {
        Error::new(ErrorKind::TooManyArguments { expected })
            .set_span(span)
            .report(self.reporter);
    }
}
