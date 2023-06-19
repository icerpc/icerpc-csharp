// Copyright (c) ZeroC, Inc.

use slicec::grammar::{Contained, Entities};

use super::*;

#[derive(Debug)]
pub struct CsReadonly {}

impl CsReadonly {
    pub fn parse_from(Unparsed { directive, args }: &Unparsed, span: &Span, reporter: &mut DiagnosticReporter) -> Self {
        debug_assert_eq!(directive, Self::directive());

        check_that_no_arguments_were_provided(args, Self::directive(), span, reporter);

        CsReadonly {}
    }

    pub fn validate_on(&self, applied_on: Attributables, span: &Span, reporter: &mut DiagnosticReporter) {
        match applied_on {
            Attributables::Struct(_) => {},
            Attributables::Field(field) => {
                if !matches!(field.parent().concrete_entity(), Entities::Struct(_)) {
                    let note = "'cs::readonly' can only be applied to structs, or fields inside structs";
                    report_unexpected_attribute(self, span, Some(note), reporter);
                }
            }
            _ => report_unexpected_attribute(self, span, None, reporter),
        }
    }
}

implement_attribute_kind_for!(CsReadonly, "cs::readonly", false);
