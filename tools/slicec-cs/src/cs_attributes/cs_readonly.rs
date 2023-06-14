// Copyright (c) ZeroC, Inc.

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
        if !matches!(applied_on, Attributables::Struct(_)) {
            report_unexpected_attribute(self, span, None, reporter);
        }
    }
}

implement_attribute_kind_for!(CsReadonly, "cs::readonly", false);
