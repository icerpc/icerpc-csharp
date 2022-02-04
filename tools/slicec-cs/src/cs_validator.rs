// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::*;
use slice::visitor::Visitor;

/// CsValidator visits all the elements in a slice file to check for errors and warnings specific to
/// the slicec-cs compiler. This is the final validation step, and the last phase of compilation
/// before code generation occurs.
#[derive(Debug)]
pub(crate) struct CsValidator;

fn cs_attributes(attributes: &[Attribute]) -> Vec<Attribute> {
    attributes
        .iter()
        .cloned()
        .filter(|attribute| {
            attribute.prefix.is_some() && attribute.prefix.as_ref().unwrap() == "cs"
        })
        .collect::<Vec<_>>()
}

fn report_unexpected_attribute(attribute: &Attribute) {
    slice::report_error(
        format!("unexpected attribute cs:{}", attribute.directive),
        Some(&attribute.location),
    );
}

fn validate_cs_attribute(attribute: &Attribute) {
    match attribute.arguments.len() {
        1 => (), // Expected 1 argument
        0 => slice::report_error(
            r#"missing required argument, expected 'cs:attribute("<attribute-value>")'"#.to_owned(),
            Some(&attribute.location),
        ),
        _ => slice::report_error(
            r#"too many arguments expected 'cs:attribute("<attribute-value>")'"#.to_owned(),
            Some(&attribute.location),
        ),
    }
}

fn validate_cs_internal(attribute: &Attribute) {
    if !attribute.arguments.is_empty() {
        slice::report_error(
            "too many arguments expected 'cs:internal'".to_owned(),
            Some(&attribute.location),
        );
    }
}

fn validate_cs_encoded_result(attribute: &Attribute) {
    if !attribute.arguments.is_empty() {
        slice::report_error(
            "too many arguments expected 'cs:encoded-result'".to_owned(),
            Some(&attribute.location),
        );
    }
}

fn validate_cs_generic(attribute: &Attribute) {
    match attribute.arguments.len() {
        1 => (), // Expected 1 argument
        0 => slice::report_error(
            r#"missing required argument, expected 'cs:generic("<generic-type>")'"#.to_owned(),
            Some(&attribute.location),
        ),
        _ => slice::report_error(
            r#"too many arguments expected 'cs:generic("<generic-type>")'"#.to_owned(),
            Some(&attribute.location),
        ),
    }
}

fn validate_collection_attributes<T: Attributable>(attributable: &T) {
    for attribute in &cs_attributes(attributable.attributes()) {
        match attribute.directive.as_ref() {
            "generic" => validate_cs_generic(attribute),
            _ => report_unexpected_attribute(attribute),
        }
    }
}

fn validate_common_attributes(attribute: &Attribute) {
    match attribute.directive.as_ref() {
        "attribute" => validate_cs_attribute(attribute),
        "internal" => validate_cs_internal(attribute),
        _ => report_unexpected_attribute(attribute),
    }
}

fn validate_data_type_attributes(data_type: &TypeRef) {
    match data_type.concrete_type() {
        Types::Sequence(_) | Types::Dictionary(_) => validate_collection_attributes(data_type),
        _ => report_typeref_unexpected_attributes(data_type),
    }
}

fn report_typeref_unexpected_attributes<T: Attributable>(attributable: &T) {
    for attribute in &cs_attributes(attributable.attributes()) {
        report_unexpected_attribute(attribute);
    }
}

impl Visitor for CsValidator {
    fn visit_module_start(&mut self, module_def: &Module) {
        for attribute in &cs_attributes(module_def.attributes()) {
            match attribute.directive.as_ref() {
                "namespace" => {
                    match attribute.arguments.len() {
                        1 => (), // Expected 1 argument
                        0 => slice::report_error(
                            r#"missing required argument, expected 'cs:namespace("<namespace>")'"#
                                .to_owned(),
                            Some(&attribute.location),
                        ),
                        _ => slice::report_error(
                            r#"too many arguments expected 'cs:namespace("<namespace>")'"#
                                .to_owned(),
                            Some(&attribute.location),
                        ),
                    }
                    if !module_def.is_top_level() {
                        slice::report_error(
                            "The 'cs:namespace' attribute is only valid for top-level modules"
                                .to_owned(),
                            Some(&attribute.location),
                        );
                    }
                }
                _ => validate_common_attributes(attribute),
            }
        }
    }

    fn visit_struct_start(&mut self, struct_def: &Struct) {
        for attribute in &cs_attributes(struct_def.attributes()) {
            match attribute.directive.as_ref() {
                "readonly" => {
                    if !attribute.arguments.is_empty() {
                        slice::report_error(
                            "too many arguments expected 'cs:readonly'".to_owned(),
                            Some(&attribute.location),
                        );
                    }
                }
                "type" => {
                    match attribute.arguments.len() {
                        1 => (), // Expected 1 argument
                        0 => slice::report_error(
                            r#"missing required argument, expected 'cs:type("<type>")'"#.to_owned(),
                            Some(&attribute.location),
                        ),
                        _ => slice::report_error(
                            r#"too many arguments, expected 'cs:type("<type>")'"#.to_owned(),
                            Some(&attribute.location),
                        ),
                    }
                }
                _ => validate_common_attributes(attribute),
            }
        }
    }

    fn visit_class_start(&mut self, class_def: &Class) {
        for attribute in &cs_attributes(class_def.attributes()) {
            validate_common_attributes(attribute);
        }
    }

    fn visit_exception_start(&mut self, exception_def: &Exception) {
        for attribute in &cs_attributes(exception_def.attributes()) {
            match attribute.directive.as_ref() {
                "attribute" => validate_cs_attribute(attribute),
                _ => report_unexpected_attribute(attribute),
            }
        }
    }

    fn visit_interface_start(&mut self, interface_def: &Interface) {
        for attribute in &cs_attributes(interface_def.attributes()) {
            match attribute.directive.as_ref() {
                "encoded-result" => validate_cs_encoded_result(attribute),
                _ => validate_common_attributes(attribute),
            }
        }
    }

    fn visit_enum_start(&mut self, enum_def: &Enum) {
        for attribute in &cs_attributes(enum_def.attributes()) {
            validate_common_attributes(attribute);
        }
    }

    fn visit_operation_start(&mut self, operation: &Operation) {
        for attribute in &cs_attributes(operation.attributes()) {
            match attribute.directive.as_ref() {
                "encoded-result" => validate_cs_encoded_result(attribute),
                _ => validate_common_attributes(attribute),
            }
        }
    }

    fn visit_trait(&mut self, trait_def: &Trait) {
        for attribute in &cs_attributes(trait_def.attributes()) {
            validate_common_attributes(attribute);
        }
    }

    fn visit_type_alias(&mut self, type_alias: &TypeAlias) {
        validate_data_type_attributes(&type_alias.underlying);
    }

    fn visit_data_member(&mut self, data_member: &DataMember) {
        validate_data_type_attributes(&data_member.data_type);
    }

    fn visit_parameter(&mut self, parameter: &Parameter) {
        validate_data_type_attributes(&parameter.data_type);
    }

    fn visit_return_member(&mut self, parameter: &Parameter) {
        validate_data_type_attributes(&parameter.data_type);
    }
}
