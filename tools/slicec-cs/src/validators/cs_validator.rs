// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::cs_attributes;
use slice::diagnostics::{DiagnosticReporter, Error, ErrorKind, Note, Warning, WarningKind};
use slice::grammar::*;
use slice::parse_result::{ParsedData, ParserResult};
use slice::visitor::Visitor;

pub(crate) fn validate_cs_attributes(mut parsed_data: ParsedData) -> ParserResult {
    let mut visitor = CsValidator {
        diagnostic_reporter: &mut parsed_data.diagnostic_reporter,
    };
    for slice_file in parsed_data.files.values() {
        slice_file.visit_with(&mut visitor);
    }
    parsed_data.into()
}

/// CsValidator visits all the elements in a slice file to check for errors and warnings specific to
/// the slicec-cs compiler. This is the final validation step, and the last phase of compilation
/// before code generation occurs.
#[derive(Debug)]
struct CsValidator<'a> {
    pub diagnostic_reporter: &'a mut DiagnosticReporter,
}

fn cs_attributes(attributes: &[Attribute]) -> Vec<Attribute> {
    attributes
        .iter()
        .cloned()
        .filter(|attribute| attribute.prefix.is_some() && attribute.prefix.as_ref().unwrap() == "cs")
        .collect::<Vec<_>>()
}

fn report_unexpected_attribute(attribute: &Attribute, diagnostic_reporter: &mut DiagnosticReporter) {
    diagnostic_reporter.report_error(Error::new(
        ErrorKind::UnexpectedAttribute(format!("cs::{}", attribute.directive)),
        Some(attribute.span()),
    ));
}

fn validate_cs_attribute(attribute: &Attribute, diagnostic_reporter: &mut DiagnosticReporter) {
    match attribute.arguments.len() {
        1 => (), // Expected 1 argument
        0 => diagnostic_reporter.report_error(Error::new(
            ErrorKind::MissingRequiredArgument(cs_attributes::ATTRIBUTE.to_owned() + r#"("<attribute-value>")"#),
            Some(attribute.span()),
        )),
        _ => diagnostic_reporter.report_error(Error::new(
            ErrorKind::TooManyArguments(cs_attributes::ATTRIBUTE.to_owned() + r#"("<attribute-value>")"#),
            Some(attribute.span()),
        )),
    }
}

fn validate_cs_identifier(attribute: &Attribute, diagnostic_reporter: &mut DiagnosticReporter) {
    // Validate that the user supplied a single argument
    match attribute.arguments.len() {
        1 => (), // Expected 1 argument
        0 => diagnostic_reporter.report_error(Error::new(
            ErrorKind::MissingRequiredArgument(cs_attributes::IDENTIFIER.to_owned() + r#"("<identifier>")"#),
            Some(attribute.span()),
        )),
        _ => diagnostic_reporter.report_error(Error::new(
            ErrorKind::TooManyArguments(cs_attributes::IDENTIFIER.to_owned() + r#"("<identifier>")"#),
            Some(attribute.span()),
        )),
    }
}

fn validate_cs_internal(attribute: &Attribute, diagnostic_reporter: &mut DiagnosticReporter) {
    if !attribute.arguments.is_empty() {
        diagnostic_reporter.report_error(Error::new(
            ErrorKind::TooManyArguments(cs_attributes::INTERNAL.to_owned()),
            Some(attribute.span()),
        ));
    }
}

fn validate_cs_encoded_result(attribute: &Attribute, diagnostic_reporter: &mut DiagnosticReporter) {
    if !attribute.arguments.is_empty() {
        diagnostic_reporter.report_error(Error::new(
            ErrorKind::TooManyArguments(cs_attributes::ENCODED_RESULT.to_owned()),
            Some(attribute.span()),
        ));
    }
}

fn validate_cs_generic(attribute: &Attribute, diagnostic_reporter: &mut DiagnosticReporter) {
    match attribute.arguments.len() {
        1 => (), // Expected 1 argument
        0 => diagnostic_reporter.report_error(Error::new(
            ErrorKind::MissingRequiredArgument(cs_attributes::GENERIC.to_owned() + r#"("<generic-type>")"#),
            Some(attribute.span()),
        )),
        _ => diagnostic_reporter.report_error(Error::new(
            ErrorKind::TooManyArguments(cs_attributes::GENERIC.to_owned() + r#"("<generic-type>")"#),
            Some(attribute.span()),
        )),
    }
}

fn validate_cs_type(attribute: &Attribute, diagnostic_reporter: &mut DiagnosticReporter) {
    match attribute.arguments.len() {
        1 => (), // Expected 1 argument
        0 => diagnostic_reporter.report_error(Error::new(
            ErrorKind::MissingRequiredArgument(cs_attributes::TYPE.to_owned() + r#"("<type>")"#),
            Some(attribute.span()),
        )),
        _ => diagnostic_reporter.report_error(Error::new(
            ErrorKind::TooManyArguments(cs_attributes::TYPE.to_owned() + r#"("<type>")"#),
            Some(attribute.span()),
        )),
    }
}

fn validate_collection_attributes<T: Attributable>(attributable: &T, diagnostic_reporter: &mut DiagnosticReporter) {
    for attribute in &cs_attributes(attributable.attributes()) {
        match attribute.directive.as_str() {
            "generic" => validate_cs_generic(attribute, diagnostic_reporter),
            _ => report_unexpected_attribute(attribute, diagnostic_reporter),
        }
    }
}

fn validate_common_attributes(attribute: &Attribute, diagnostic_reporter: &mut DiagnosticReporter) {
    match attribute.directive.as_str() {
        "attribute" => validate_cs_attribute(attribute, diagnostic_reporter),
        "identifier" => validate_cs_identifier(attribute, diagnostic_reporter),
        _ => report_unexpected_attribute(attribute, diagnostic_reporter),
    }
}

fn validate_data_type_attributes(data_type: &TypeRef, diagnostic_reporter: &mut DiagnosticReporter) {
    match data_type.concrete_type() {
        Types::Sequence(_) | Types::Dictionary(_) => validate_collection_attributes(data_type, diagnostic_reporter),
        _ => report_typeref_unexpected_attributes(data_type, diagnostic_reporter),
    }
}

fn report_typeref_unexpected_attributes<T: Attributable>(
    attributable: &T,
    diagnostic_reporter: &mut DiagnosticReporter,
) {
    for attribute in &cs_attributes(attributable.attributes()) {
        report_unexpected_attribute(attribute, diagnostic_reporter);
    }
}

impl Visitor for CsValidator<'_> {
    fn visit_module_start(&mut self, module_def: &Module) {
        for attribute in &cs_attributes(module_def.attributes()) {
            match attribute.directive.as_str() {
                "namespace" => {
                    match attribute.arguments.len() {
                        1 => (), // Expected 1 argument
                        0 => self.diagnostic_reporter.report_error(Error::new(
                            ErrorKind::MissingRequiredArgument(
                                cs_attributes::NAMESPACE.to_owned() + r#"("<namespace>")"#,
                            ),
                            Some(attribute.span()),
                        )),
                        _ => self.diagnostic_reporter.report_error(Error::new(
                            ErrorKind::TooManyArguments(cs_attributes::NAMESPACE.to_owned() + r#"("<namespace>")"#),
                            Some(attribute.span()),
                        )),
                    }
                }
                "identifier" => self.diagnostic_reporter.report_error(Error::new_with_notes(
                    ErrorKind::InvalidAttribute(cs_attributes::IDENTIFIER.to_owned(), "module".to_owned()),
                    Some(attribute.span()),
                    vec![Note::new(
                        format!("To rename a module use {} instead", cs_attributes::NAMESPACE),
                        None,
                    )],
                )),
                "internal" => validate_cs_internal(attribute, self.diagnostic_reporter),
                _ => validate_common_attributes(attribute, self.diagnostic_reporter),
            }
        }
    }

    fn visit_struct_start(&mut self, struct_def: &Struct) {
        for attribute in &cs_attributes(struct_def.attributes()) {
            match attribute.directive.as_str() {
                "readonly" => {
                    if !attribute.arguments.is_empty() {
                        self.diagnostic_reporter.report_error(Error::new(
                            ErrorKind::TooManyArguments(cs_attributes::READONLY.to_owned()),
                            Some(attribute.span()),
                        ));
                    }
                }
                "internal" => validate_cs_internal(attribute, self.diagnostic_reporter),
                _ => validate_common_attributes(attribute, self.diagnostic_reporter),
            }
        }
    }

    fn visit_class_start(&mut self, class_def: &Class) {
        for attribute in &cs_attributes(class_def.attributes()) {
            match attribute.directive.as_str() {
                "internal" => validate_cs_internal(attribute, self.diagnostic_reporter),
                _ => validate_common_attributes(attribute, self.diagnostic_reporter),
            }
        }
    }

    fn visit_exception_start(&mut self, exception_def: &Exception) {
        for attribute in &cs_attributes(exception_def.attributes()) {
            match attribute.directive.as_str() {
                "internal" => validate_cs_internal(attribute, self.diagnostic_reporter),
                _ => validate_common_attributes(attribute, self.diagnostic_reporter),
            }
        }
    }

    fn visit_interface_start(&mut self, interface_def: &Interface) {
        for attribute in &cs_attributes(interface_def.attributes()) {
            match attribute.directive.as_str() {
                "encodedResult" => validate_cs_encoded_result(attribute, self.diagnostic_reporter),
                "internal" => validate_cs_internal(attribute, self.diagnostic_reporter),
                _ => validate_common_attributes(attribute, self.diagnostic_reporter),
            }
        }
    }

    fn visit_enum_start(&mut self, enum_def: &Enum) {
        for attribute in &cs_attributes(enum_def.attributes()) {
            match attribute.directive.as_str() {
                "internal" => validate_cs_internal(attribute, self.diagnostic_reporter),
                _ => validate_common_attributes(attribute, self.diagnostic_reporter),
            }
        }
    }

    fn visit_operation_start(&mut self, operation: &Operation) {
        for attribute in &cs_attributes(operation.attributes()) {
            match attribute.directive.as_str() {
                "encodedResult" => validate_cs_encoded_result(attribute, self.diagnostic_reporter),
                _ => validate_common_attributes(attribute, self.diagnostic_reporter),
            }
        }
    }

    fn visit_trait(&mut self, trait_def: &Trait) {
        for attribute in &cs_attributes(trait_def.attributes()) {
            match attribute.directive.as_str() {
                "internal" => validate_cs_internal(attribute, self.diagnostic_reporter),
                _ => validate_common_attributes(attribute, self.diagnostic_reporter),
            }
        }
    }

    fn visit_custom_type(&mut self, custom_type: &CustomType) {
        // We require 'cs::type' on custom types to know how to encode/decode it.
        if !custom_type.has_attribute(cs_attributes::TYPE, false) {
            self.diagnostic_reporter.report_error(Error::new(
                ErrorKind::MissingRequiredAttribute(cs_attributes::TYPE.to_owned()),
                Some(custom_type.span()),
            ));
        }

        for attribute in &cs_attributes(custom_type.attributes()) {
            match attribute.directive.as_str() {
                "type" => validate_cs_type(attribute, self.diagnostic_reporter),
                _ => validate_common_attributes(attribute, self.diagnostic_reporter),
            }
        }
    }

    fn visit_type_alias(&mut self, type_alias: &TypeAlias) {
        for attribute in &cs_attributes(type_alias.attributes()) {
            match attribute.directive.as_str() {
                "identifier" => self.diagnostic_reporter.report_warning(
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
        for attribute in &cs_attributes(data_member.attributes()) {
            match attribute.directive.as_str() {
                "identifier" => validate_cs_identifier(attribute, self.diagnostic_reporter),
                _ => validate_data_type_attributes(&data_member.data_type, self.diagnostic_reporter),
            }
        }
    }

    fn visit_parameter(&mut self, parameter: &Parameter) {
        for attribute in &cs_attributes(parameter.attributes()) {
            match attribute.directive.as_str() {
                "identifier" => validate_cs_identifier(attribute, self.diagnostic_reporter),
                _ => validate_data_type_attributes(&parameter.data_type, self.diagnostic_reporter),
            }
        }
    }
}
