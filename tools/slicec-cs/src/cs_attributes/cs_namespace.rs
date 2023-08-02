// Copyright (c) ZeroC, Inc.

use super::*;

#[derive(Debug)]
pub struct CsNamespace {
    pub namespace: String,
}

impl CsNamespace {
    pub fn parse_from(Unparsed { directive, args }: &Unparsed, span: &Span, diagnostics: &mut Diagnostics) -> Self {
        debug_assert_eq!(directive, Self::directive());

        check_that_exactly_one_argument_was_provided(args, Self::directive(), span, diagnostics);

        let namespace = args.first().cloned().unwrap_or_default();
        CsNamespace { namespace }
    }

    pub fn validate_on(&self, applied_on: Attributables, span: &Span, diagnostics: &mut Diagnostics) {
        if !matches!(applied_on, Attributables::Module(_)) {
            report_unexpected_attribute(self, span, None, diagnostics);
        }
    }
}

implement_attribute_kind_for!(CsNamespace, "cs::namespace", false);
