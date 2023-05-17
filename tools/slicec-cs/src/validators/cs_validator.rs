// Copyright (c) ZeroC, Inc.

use crate::cs_attributes::{self, match_cs_custom, CsAttributeKind};
use slicec::compilation_state::CompilationState;
use slicec::diagnostics::{Diagnostic, DiagnosticReporter, Error};
use slicec::grammar::*;
use slicec::slice_file::{SliceFile, Span};
use slicec::visitor::Visitor;

pub(crate) fn validate_cs_attributes(compilation_state: &mut CompilationState) {
    let diagnostic_reporter = &mut compilation_state.diagnostic_reporter;
    let mut visitor = CsValidator { diagnostic_reporter };

    for slice_file in compilation_state.files.values() {
        slice_file.visit_with(&mut visitor);
    }
}

/// CsValidator visits all the elements in a slice file to check for errors and warnings specific to
/// the slicec-cs compiler. This is the final validation step, and the last phase of compilation
/// before code generation occurs.
#[derive(Debug)]
struct CsValidator<'a> {
    pub diagnostic_reporter: &'a mut DiagnosticReporter,
}

/// Returns an iterator of C# specific attributes with any non-C# attributes filtered out.
fn get_cs_attributes(attributable: &impl Attributable) -> impl Iterator<Item = (&CsAttributeKind, &Span)> {
    attributable.attributes(false).into_iter().filter_map(|attribute| {
        cs_attributes::as_cs_attribute(attribute).map(|cs_attribute| (cs_attribute, &attribute.span))
    })
}

fn report_unexpected_attribute(attribute: &CsAttributeKind, span: &Span, diagnostic_reporter: &mut DiagnosticReporter) {
    let note = match attribute {
        CsAttributeKind::Generic { .. } => {
            Some("the cs::generic attribute can only be applied to sequences and dictionaries")
        }
        _ => None,
    };

    let mut diagnostic = Diagnostic::new(Error::UnexpectedAttribute {
        attribute: attribute.directive().to_owned(),
    })
    .set_span(span);

    if let Some(note) = note {
        diagnostic = diagnostic.add_note(note, None);
    }

    diagnostic.report(diagnostic_reporter);
}

fn validate_cs_encoded_result(operation: &Operation, span: &Span, diagnostic_reporter: &mut DiagnosticReporter) {
    if operation.non_streamed_return_members().is_empty() {
        Diagnostic::new(Error::UnexpectedAttribute {
            attribute: cs_attributes::ENCODED_RESULT.to_owned(),
        })
        .set_span(span)
        .add_note(
            if operation.streamed_return_member().is_some() {
                format!(
                    "The '{}' attribute is not applicable to an operation that only returns a stream.",
                    cs_attributes::ENCODED_RESULT,
                )
            } else {
                format!(
                    "The '{}' attribute is not applicable to an operation that does not return anything.",
                    cs_attributes::ENCODED_RESULT,
                )
            },
            None,
        )
        .report(diagnostic_reporter);
    }
}

fn validate_common_attributes(attribute: &CsAttributeKind, span: &Span, diagnostic_reporter: &mut DiagnosticReporter) {
    match attribute {
        CsAttributeKind::Identifier { .. } => {}
        _ => report_unexpected_attribute(attribute, span, diagnostic_reporter),
    }
}

/// Validates attributes on constructed types other than custom.
fn validate_non_custom_type_attributes(
    attribute: &CsAttributeKind,
    span: &Span,
    diagnostic_reporter: &mut DiagnosticReporter,
) {
    match attribute {
        CsAttributeKind::Internal { .. } => {}
        _ => validate_common_attributes(attribute, span, diagnostic_reporter),
    }
}

impl Visitor for CsValidator<'_> {
    fn visit_file(&mut self, slice_file: &SliceFile) {
        for (attribute, span) in get_cs_attributes(slice_file) {
            report_unexpected_attribute(attribute, span, self.diagnostic_reporter);
        }
    }

    fn visit_module(&mut self, module_def: &Module) {
        for (attribute, span) in get_cs_attributes(module_def) {
            match attribute {
                CsAttributeKind::Namespace { .. } => {}
                CsAttributeKind::Identifier { .. } => {
                    let attribute = cs_attributes::IDENTIFIER.to_owned();
                    Diagnostic::new(Error::UnexpectedAttribute { attribute })
                        .set_span(span)
                        .add_note(
                            format!("To rename a module use {} instead", cs_attributes::NAMESPACE),
                            None,
                        )
                        .report(self.diagnostic_reporter);
                }
                _ => validate_common_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_struct(&mut self, struct_def: &Struct) {
        for (attribute, span) in get_cs_attributes(struct_def) {
            match attribute {
                CsAttributeKind::Readonly { .. } => {}
                _ => validate_non_custom_type_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_class(&mut self, class_def: &Class) {
        for (attribute, span) in get_cs_attributes(class_def) {
            validate_non_custom_type_attributes(attribute, span, self.diagnostic_reporter);
        }
    }

    fn visit_exception(&mut self, exception_def: &Exception) {
        for (attribute, span) in get_cs_attributes(exception_def) {
            validate_non_custom_type_attributes(attribute, span, self.diagnostic_reporter);
        }
    }

    fn visit_interface(&mut self, interface_def: &Interface) {
        for (attribute, span) in get_cs_attributes(interface_def) {
            validate_non_custom_type_attributes(attribute, span, self.diagnostic_reporter);
        }
    }

    fn visit_enum(&mut self, enum_def: &Enum) {
        for (attribute, span) in get_cs_attributes(enum_def) {
            match attribute {
                CsAttributeKind::Attribute { .. } => {}
                _ => validate_non_custom_type_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_operation(&mut self, operation: &Operation) {
        for (attribute, span) in get_cs_attributes(operation) {
            match attribute {
                CsAttributeKind::EncodedResult {} => {
                    validate_cs_encoded_result(operation, span, self.diagnostic_reporter);
                }
                _ => validate_common_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_custom_type(&mut self, custom_type: &CustomType) {
        // We require 'cs::custom' on custom types to know how to encode/decode it.
        if !custom_type.has_attribute(false, match_cs_custom) {
            Diagnostic::new(Error::MissingRequiredAttribute {
                attribute: cs_attributes::CUSTOM.to_owned(),
            })
            .set_span(custom_type.span())
            .report(self.diagnostic_reporter);
        }

        for (attribute, span) in get_cs_attributes(custom_type) {
            match attribute {
                CsAttributeKind::Custom { .. } => {}
                _ => validate_common_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_type_alias(&mut self, type_alias: &TypeAlias) {
        for (attribute, span) in get_cs_attributes(type_alias) {
            report_unexpected_attribute(attribute, span, self.diagnostic_reporter);
        }
    }

    fn visit_field(&mut self, field: &Field) {
        for (attribute, span) in get_cs_attributes(field) {
            match attribute {
                CsAttributeKind::Attribute { .. } => {}
                _ => validate_common_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_parameter(&mut self, parameter: &Parameter) {
        for (attribute, span) in get_cs_attributes(parameter) {
            validate_common_attributes(attribute, span, self.diagnostic_reporter);
        }
    }

    fn visit_enumerator(&mut self, enumerator: &Enumerator) {
        for (attribute, span) in get_cs_attributes(enumerator) {
            validate_common_attributes(attribute, span, self.diagnostic_reporter);
        }
    }

    fn visit_type_ref(&mut self, type_ref: &TypeRef) {
        for (attribute, span) in get_cs_attributes(type_ref) {
            match attribute {
                CsAttributeKind::Generic { .. }
                    if matches!(type_ref.concrete_type(), Types::Sequence(_) | Types::Dictionary(_)) => {}
                _ => report_unexpected_attribute(attribute, span, self.diagnostic_reporter),
            }
        }
    }
}
