// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::cs_attributes::{self, match_cs_type, CsAttributeKind};
use slice::compilation_result::{CompilationData, CompilationResult};
use slice::diagnostics::{DiagnosticReporter, Error, ErrorKind, Note, Warning, WarningKind};
use slice::grammar::*;
use slice::slice_file::Span;
use slice::visitor::Visitor;

pub(crate) fn validate_cs_attributes(mut compilation_data: CompilationData) -> CompilationResult {
    let mut visitor = CsValidator {
        diagnostic_reporter: &mut compilation_data.diagnostic_reporter,
    };
    for slice_file in compilation_data.files.values() {
        slice_file.visit_with(&mut visitor);
    }
    compilation_data.into()
}

/// CsValidator visits all the elements in a slice file to check for errors and warnings specific to
/// the slicec-cs compiler. This is the final validation step, and the last phase of compilation
/// before code generation occurs.
#[derive(Debug)]
struct CsValidator<'a> {
    pub diagnostic_reporter: &'a mut DiagnosticReporter,
}

fn cs_attributes<'a>(attributes: &[&'a Attribute]) -> Vec<(&'a CsAttributeKind, Span)> {
    attributes
        .iter()
        .cloned()
        .filter_map(|attribute| match &attribute.kind {
            AttributeKind::LanguageKind { kind } => Some((
                kind.as_any().downcast_ref::<CsAttributeKind>().unwrap(),
                attribute.span.clone(),
            )),
            _ => None,
        })
        .collect::<Vec<_>>()
}

fn report_unexpected_attribute(attribute: &CsAttributeKind, span: &Span, diagnostic_reporter: &mut DiagnosticReporter) {
    diagnostic_reporter.report_error(Error::new(
        ErrorKind::UnexpectedAttribute(attribute.directive().to_owned()),
        Some(span),
    ));
}

fn validate_cs_encoded_result(operation: &Operation, span: &Span, diagnostic_reporter: &mut DiagnosticReporter) {
    if operation.nonstreamed_return_members().is_empty() {
        diagnostic_reporter.report_error(Error::new_with_notes(
            ErrorKind::UnexpectedAttribute(cs_attributes::ENCODED_RESULT.to_owned()),
            Some(span),
            vec![Note::new(
                if operation.streamed_return_member().is_some() {
                    format!(
                        "The '{}' attribute is not applicable to an operation that only returns a stream.",
                        cs_attributes::ENCODED_RESULT
                    )
                } else {
                    format!(
                        "The '{}' attribute is not applicable to an operation that does not return anything.",
                        cs_attributes::ENCODED_RESULT
                    )
                },
                None,
            )],
        ))
    }
}

fn validate_collection_attributes<T: Attributable>(attributable: &T, diagnostic_reporter: &mut DiagnosticReporter) {
    for (attribute, span) in &cs_attributes(&attributable.attributes(false)) {
        match attribute {
            CsAttributeKind::Generic { .. } => {}
            _ => report_unexpected_attribute(attribute, span, diagnostic_reporter),
        }
    }
}

fn validate_common_attributes(attribute: &CsAttributeKind, span: &Span, diagnostic_reporter: &mut DiagnosticReporter) {
    match attribute {
        CsAttributeKind::Attribute { .. } | CsAttributeKind::Identifier { .. } => {}
        _ => report_unexpected_attribute(attribute, span, diagnostic_reporter),
    }
}

fn validate_cs_encoded_result(
    operation: &Operation,
    attribute: &Attribute,
    diagnostic_reporter: &mut DiagnosticReporter,
) {
    if operation.non_streamed_return_members().is_empty() {
        diagnostic_reporter.report_error(Error::new_with_notes(
            ErrorKind::UnexpectedAttribute(cs_attributes::ENCODED_RESULT.to_owned()),
            Some(attribute.span()),
            vec![Note::new(
                if operation.streamed_return_member().is_some() {
                    format!(
                        "The '{}' attribute is not applicable to an operation that only returns a stream.",
                        cs_attributes::ENCODED_RESULT
                    )
                } else {
                    format!(
                        "The '{}' attribute is not applicable to an operation that does not return anything.",
                        cs_attributes::ENCODED_RESULT
                    )
                },
                None,
            )],
        ))
    }
    if !attribute.arguments.is_empty() {
        diagnostic_reporter.report_error(Error::new(
            ErrorKind::TooManyArguments(cs_attributes::ENCODED_RESULT.to_owned()),
            Some(attribute.span()),
        ));
    }
}

fn report_typeref_unexpected_attributes<T: Attributable>(
    attributable: &T,
    diagnostic_reporter: &mut DiagnosticReporter,
) {
    for (attribute, span) in &cs_attributes(&attributable.attributes(false)) {
        report_unexpected_attribute(attribute, span, diagnostic_reporter);
    }
}

impl Visitor for CsValidator<'_> {
    fn visit_module_start(&mut self, module_def: &Module) {
        for (attribute, span) in &cs_attributes(&module_def.attributes(false)) {
            match attribute {
                CsAttributeKind::Namespace { .. } => {}
                CsAttributeKind::Identifier { .. } => self.diagnostic_reporter.report_error(Error::new_with_notes(
                    ErrorKind::InvalidAttribute(cs_attributes::IDENTIFIER.to_owned(), "module".to_owned()),
                    Some(span),
                    vec![Note::new(
                        format!("To rename a module use {} instead", cs_attributes::NAMESPACE),
                        None,
                    )],
                )),
                CsAttributeKind::Internal => {}
                _ => validate_common_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_struct_start(&mut self, struct_def: &Struct) {
        for (attribute, span) in &cs_attributes(&struct_def.attributes(false)) {
            match attribute {
                CsAttributeKind::Readonly | CsAttributeKind::Internal => {}
                _ => validate_common_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_class_start(&mut self, class_def: &Class) {
        for (attribute, span) in &cs_attributes(&class_def.attributes(false)) {
            match attribute {
                CsAttributeKind::Internal => {}
                _ => validate_common_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_exception_start(&mut self, exception_def: &Exception) {
        for (attribute, span) in &cs_attributes(&exception_def.attributes(false)) {
            match attribute {
                CsAttributeKind::Internal => {}
                _ => validate_common_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_interface_start(&mut self, interface_def: &Interface) {
        for (attribute, span) in &cs_attributes(&interface_def.attributes(false)) {
            match attribute {
                CsAttributeKind::Internal => {}
                _ => validate_common_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_enum_start(&mut self, enum_def: &Enum) {
        for (attribute, span) in &cs_attributes(&enum_def.attributes(false)) {
            match attribute {
                CsAttributeKind::Internal => {}
                _ => validate_common_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_operation_start(&mut self, operation: &Operation) {
        for (attribute, span) in &cs_attributes(&operation.attributes(false)) {
            match attribute {
                CsAttributeKind::EncodedResult {} => {
                    validate_cs_encoded_result(operation, span, self.diagnostic_reporter)
                }
                _ => validate_common_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_custom_type(&mut self, custom_type: &CustomType) {
        // We require 'cs::type' on custom types to know how to encode/decode it.

        if !custom_type.has_attribute(false, match_cs_type) {
            self.diagnostic_reporter.report_error(Error::new(
                ErrorKind::MissingRequiredAttribute(cs_attributes::TYPE.to_owned()),
                Some(custom_type.span()),
            ));
        }

        for (attribute, span) in &cs_attributes(&custom_type.attributes(false)) {
            match attribute {
                CsAttributeKind::Type { .. } => {}
                _ => validate_common_attributes(attribute, span, self.diagnostic_reporter),
            }
        }
    }

    fn visit_type_alias(&mut self, type_alias: &TypeAlias) {
        for (attribute, ..) in &cs_attributes(&type_alias.attributes(false)) {
            match attribute {
                CsAttributeKind::Identifier { .. } => self.diagnostic_reporter.report_warning(
                    Warning::new(
                        WarningKind::InconsequentialUseOfAttribute(
                            cs_attributes::IDENTIFIER.to_owned(),
                            "typealias".to_owned(),
                        ),
                        type_alias.span(),
                    ),
                    type_alias,
                ),
                _ => validate_data_type_attributes(&type_alias.underlying, self.diagnostic_reporter),
            }
        }
    }

    fn visit_data_member(&mut self, data_member: &DataMember) {
        for (attribute, ..) in &cs_attributes(&data_member.attributes(false)) {
            match attribute {
                CsAttributeKind::Identifier { .. } => {}
                _ => validate_data_type_attributes(&data_member.data_type, self.diagnostic_reporter),
            }
        }
    }

    fn visit_parameter(&mut self, parameter: &Parameter) {
        for (attribute, ..) in &cs_attributes(&parameter.attributes(false)) {
            match attribute {
                CsAttributeKind::Identifier { .. } => {}
                _ => validate_data_type_attributes(&parameter.data_type, self.diagnostic_reporter),
            }
        }
    }
}
