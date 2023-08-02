// Copyright (c) ZeroC, Inc.

use super::*;
use slicec::grammar::{Contained, Entities};

#[derive(Debug)]
pub struct CsReadonly {}

impl CsReadonly {
    pub fn parse_from(Unparsed { directive, args }: &Unparsed, span: &Span, diagnostics: &mut Diagnostics) -> Self {
        debug_assert_eq!(directive, Self::directive());

        check_that_no_arguments_were_provided(args, Self::directive(), span, diagnostics);

        CsReadonly {}
    }

    pub fn validate_on(&self, applied_on: Attributables, span: &Span, diagnostics: &mut Diagnostics) {
        match applied_on {
            Attributables::Struct(_) => {}
            Attributables::Field(field) => {
                if !matches!(field.parent().concrete_entity(), Entities::Struct(_)) {
                    let note = "'cs::readonly' can only be applied to structs, or fields inside structs";
                    report_unexpected_attribute(self, span, Some(note), diagnostics);
                }
            }
            _ => report_unexpected_attribute(self, span, None, diagnostics),
        }
    }
}

implement_attribute_kind_for!(CsReadonly, "cs::readonly", false);
