// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::error::ErrorReporter;
use slice::grammar::*;
use slice::visitor::Visitor;

/// CsValidator visits all the elements in a slice file to check for errors and warnings specific to
/// the slicec-cs compiler. This is the final validation step, and the last phase of compilation
/// before code generation occurs.
#[derive(Debug)]
pub(crate) struct CsValidator<'a> {
    pub error_reporter: &'a mut ErrorReporter,
}

fn cs_attributes(attributes: &[Attribute]) -> Vec<Attribute> {
    attributes
        .iter()
        .cloned()
        .filter(|attribute| {
            attribute.prefix.is_some() && attribute.prefix.as_ref().unwrap() == "cs"
        })
        .collect::<Vec<_>>()
}

fn report_unexpected_attribute(attribute: &Attribute, error_reporter: &mut ErrorReporter) {
    error_reporter.report_error(
        format!("unexpected attribute cs::{}", attribute.directive),
        Some(&attribute.location),
    );
}

fn validate_cs_attribute(attribute: &Attribute, error_reporter: &mut ErrorReporter) {
    match attribute.arguments.len() {
        1 => (), // Expected 1 argument
        0 => error_reporter.report_error(
            r#"missing required argument, expected 'cs::attribute("<attribute-value>")'"#.to_owned(),
            Some(&attribute.location),
        ),
        _ => error_reporter.report_error(
            r#"too many arguments expected 'cs::attribute("<attribute-value>")'"#.to_owned(),
            Some(&attribute.location),
        ),
    }
}

fn validate_cs_internal(attribute: &Attribute, error_reporter: &mut ErrorReporter) {
    if !attribute.arguments.is_empty() {
        error_reporter.report_error(
            "too many arguments expected 'cs::internal'".to_owned(),
            Some(&attribute.location),
        );
    }
}

fn validate_cs_encoded_result(attribute: &Attribute,  error_reporter: &mut ErrorReporter) {
    if !attribute.arguments.is_empty() {
        error_reporter.report_error(
            "too many arguments expected 'cs::encodedResult'".to_owned(),
            Some(&attribute.location),
        );
    }
}

fn validate_cs_generic(attribute: &Attribute, error_reporter: &mut ErrorReporter) {
    match attribute.arguments.len() {
        1 => (), // Expected 1 argument
        0 => error_reporter.report_error(
            r#"missing required argument, expected 'cs::generic("<generic-type>")'"#.to_owned(),
            Some(&attribute.location),
        ),
        _ => error_reporter.report_error(
            r#"too many arguments expected 'cs::generic("<generic-type>")'"#.to_owned(),
            Some(&attribute.location),
        ),
    }
}

fn validate_cs_type(attribute: &Attribute, error_reporter: &mut ErrorReporter) {
    match attribute.arguments.len() {
        1 => (), // Expected 1 argument
        0 => error_reporter.report_error(
            r#"missing required argument, expected 'cs::type("<type>")'"#.to_owned(),
            Some(&attribute.location),
        ),
        _ => error_reporter.report_error(
            r#"too many arguments, expected 'cs::type("<type>")'"#.to_owned(),
            Some(&attribute.location),
        ),
    }
}

fn validate_collection_attributes<T: Attributable>(attributable: &T, error_reporter: &mut ErrorReporter) {
    for attribute in &cs_attributes(attributable.attributes()) {
        match attribute.directive.as_ref() {
            "generic" => validate_cs_generic(attribute, error_reporter),
            _ => report_unexpected_attribute(attribute, error_reporter),
        }
    }
}

fn validate_common_attributes(attribute: &Attribute, error_reporter: &mut ErrorReporter) {
    match attribute.directive.as_ref() {
        "attribute" => validate_cs_attribute(attribute, error_reporter),
        "internal" => validate_cs_internal(attribute, error_reporter),
        _ => report_unexpected_attribute(attribute, error_reporter),
    }
}

fn validate_data_type_attributes(data_type: &TypeRef, error_reporter: &mut ErrorReporter) {
    match data_type.concrete_type() {
        Types::Sequence(_) | Types::Dictionary(_) => validate_collection_attributes(data_type, error_reporter),
        _ => report_typeref_unexpected_attributes(data_type, error_reporter),
    }
}

fn report_typeref_unexpected_attributes<T: Attributable>(attributable: &T, error_reporter: &mut ErrorReporter) {
    for attribute in &cs_attributes(attributable.attributes()) {
        report_unexpected_attribute(attribute, error_reporter);
    }
}

impl Visitor for CsValidator<'_> {
    fn visit_module_start(&mut self, module_def: &Module) {
        for attribute in &cs_attributes(module_def.attributes()) {
            match attribute.directive.as_ref() {
                "namespace" => {
                    match attribute.arguments.len() {
                        1 => (), // Expected 1 argument
                        0 => self.error_reporter.report_error(
                            r#"missing required argument, expected 'cs::namespace("<namespace>")'"#
                                .to_owned(),
                            Some(&attribute.location),
                        ),
                        _ => self.error_reporter.report_error(
                            r#"too many arguments expected 'cs::namespace("<namespace>")'"#
                                .to_owned(),
                            Some(&attribute.location),
                        ),
                    }
                    if !module_def.is_top_level() {
                        self.error_reporter.report_error(
                            "The 'cs::namespace' attribute is only valid for top-level modules"
                                .to_owned(),
                            Some(&attribute.location),
                        );
                    }
                }
                _ => validate_common_attributes(attribute, self.error_reporter),
            }
        }
    }

    fn visit_struct_start(&mut self, struct_def: &Struct) {
        for attribute in &cs_attributes(struct_def.attributes()) {
            match attribute.directive.as_ref() {
                "readonly" => {
                    if !attribute.arguments.is_empty() {
                        self.error_reporter.report_error(
                            "too many arguments expected 'cs::readonly'".to_owned(),
                            Some(&attribute.location),
                        );
                    }
                }
                "type" => {
                    if !struct_def.is_compact {
                        self.error_reporter.report_error(
                            r#"The 'cs::type("<type>")' attribute is only valid for compact structs"#.to_owned(),
                            Some(&attribute.location))
                    }
                    validate_cs_type(attribute, self.error_reporter);
                }
                _ => validate_common_attributes(attribute, self.error_reporter),
            }
        }
    }

    fn visit_class_start(&mut self, class_def: &Class) {
        for attribute in &cs_attributes(class_def.attributes()) {
            validate_common_attributes(attribute, self.error_reporter);
        }
    }

    fn visit_exception_start(&mut self, exception_def: &Exception) {
        for attribute in &cs_attributes(exception_def.attributes()) {
            match attribute.directive.as_ref() {
                "attribute" => validate_cs_attribute(attribute, self.error_reporter),
                _ => report_unexpected_attribute(attribute, self.error_reporter),
            }
        }
    }

    fn visit_interface_start(&mut self, interface_def: &Interface) {
        for attribute in &cs_attributes(interface_def.attributes()) {
            match attribute.directive.as_ref() {
                "encodedResult" => validate_cs_encoded_result(attribute, self.error_reporter),
                _ => validate_common_attributes(attribute, self.error_reporter),
            }
        }
    }

    fn visit_enum_start(&mut self, enum_def: &Enum) {
        for attribute in &cs_attributes(enum_def.attributes()) {
            validate_common_attributes(attribute, self.error_reporter);
        }
    }

    fn visit_operation_start(&mut self, operation: &Operation) {
        for attribute in &cs_attributes(operation.attributes()) {
            match attribute.directive.as_ref() {
                "encodedResult" => validate_cs_encoded_result(attribute, self.error_reporter),
                _ => validate_common_attributes(attribute, self.error_reporter),
            }
        }
    }

    fn visit_trait(&mut self, trait_def: &Trait) {
        for attribute in &cs_attributes(trait_def.attributes()) {
            validate_common_attributes(attribute, self.error_reporter);
        }
    }

    fn visit_custom_type(&mut self, custom_type: &CustomType) {
        // We require 'cs::type' on custom types to know how to encode/decode it.
        if !custom_type.has_attribute("cs::type", false) {
            self.error_reporter.report_error(
                "missing required attribute: 'cs::type'".to_owned(),
                Some(&custom_type.location),
            );
        }

        for attribute in &cs_attributes(custom_type.attributes()) {
            match attribute.directive.as_ref() {
                "attribute" => validate_cs_attribute(attribute, self.error_reporter),
                "type" => validate_cs_type(attribute, self.error_reporter),
                _ => report_unexpected_attribute(attribute, self.error_reporter),
            }
        }
    }

    fn visit_type_alias(&mut self, type_alias: &TypeAlias) {
        validate_data_type_attributes(&type_alias.underlying, self.error_reporter);
    }

    fn visit_data_member(&mut self, data_member: &DataMember) {
        validate_data_type_attributes(&data_member.data_type, self.error_reporter);
    }

    fn visit_parameter(&mut self, parameter: &Parameter) {
        validate_data_type_attributes(&parameter.data_type, self.error_reporter);
    }

    fn visit_return_member(&mut self, parameter: &Parameter) {
        validate_data_type_attributes(&parameter.data_type, self.error_reporter);
    }
}
