// Copyright (c) ZeroC, Inc.

use super::*;

#[derive(Debug)]
pub struct CsCustom {
    pub type_string: String,
}

impl CsCustom {
    pub fn parse_from(Unparsed { directive, args }: &Unparsed, span: &Span, reporter: &mut DiagnosticReporter) -> Self {
        debug_assert_eq!(directive, Self::directive());

        check_that_exactly_one_argument_was_provided(args, Self::directive(), span, reporter);

        let type_string = args.first().cloned().unwrap_or_default();
        CsCustom { type_string }
    }

    pub fn validate_on(&self, applied_on: Attributables, span: &Span, reporter: &mut DiagnosticReporter) {
        if !matches!(applied_on, Attributables::CustomType(_)) {
            report_unexpected_attribute(self, span, None, reporter);
        }
    }
}

implement_attribute_kind_for!(CsCustom, "cs::custom", false);
