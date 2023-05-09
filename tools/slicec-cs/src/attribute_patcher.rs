// Copyright (c) ZeroC, Inc.

use crate::cs_attributes;
use crate::cs_attributes::CsAttributeKind;

use slice::ast::node::Node;
use slice::compilation_state::CompilationState;
use slice::diagnostics::{Diagnostic, DiagnosticReporter, Error};
use slice::grammar::{Attribute, AttributeKind};
use slice::slice_file::Span;

pub unsafe fn patch_attributes(compilation_state: &mut CompilationState) {
    let diagnostic_reporter = &mut compilation_state.diagnostic_reporter;
    let mut patcher = AttributePatcher { diagnostic_reporter };

    for node in compilation_state.ast.as_mut_slice() {
        if let Node::Attribute(attribute) = node {
            patcher.patch_attribute(attribute.borrow_mut());
        }
    }
}

struct GeneralAttribute<'a> {
    directive: &'a str,
    arguments: &'a [String],
    span: &'a Span,
}

struct AttributePatcher<'a> {
    diagnostic_reporter: &'a mut DiagnosticReporter,
}

impl AttributePatcher<'_> {
    fn patch_attribute(&mut self, attribute: &mut Attribute) {
        if let AttributeKind::Other { directive, arguments } = &attribute.kind {
            if directive.starts_with(cs_attributes::ATTRIBUTE_PREFIX) {
                if let Some(cs_attribute) = self.map_language_kind(&GeneralAttribute {
                    directive,
                    arguments,
                    span: &attribute.span,
                }) {
                    attribute.kind = AttributeKind::LanguageKind {
                        kind: Box::new(cs_attribute),
                    };
                }
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
                Diagnostic::new(Error::UnexpectedAttribute {
                    attribute: attribute.directive.to_owned(),
                })
                .set_span(attribute.span)
                .report(self.diagnostic_reporter);
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
        Diagnostic::new(Error::MissingRequiredArgument {
            argument: required_argument,
        })
        .set_span(span)
        .report(self.diagnostic_reporter);
    }

    fn error_too_many(&mut self, expected: String, span: &Span) {
        Diagnostic::new(Error::TooManyArguments { expected })
            .set_span(span)
            .report(self.diagnostic_reporter);
    }
}
