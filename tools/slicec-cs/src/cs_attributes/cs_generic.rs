// Copyright (c) ZeroC, Inc.

use super::*;
use slicec::grammar::Types;

#[derive(Debug)]
pub struct CsGeneric {
    pub type_string: String,
}

impl CsGeneric {
    pub fn parse_from(Unparsed { directive, args }: &Unparsed, span: &Span, reporter: &mut DiagnosticReporter) -> Self {
        debug_assert_eq!(directive, Self::directive());

        check_that_exactly_one_argument_was_provided(args, Self::directive(), span, reporter);

        let type_string = args.first().cloned().unwrap_or_default();
        CsGeneric { type_string }
    }

    pub fn validate_on(&self, applied_on: Attributables, span: &Span, reporter: &mut DiagnosticReporter) {
        if let Attributables::TypeRef(type_ref) = applied_on {
            if matches!(type_ref.concrete_type(), Types::Sequence(_) | Types::Dictionary(_)) {
                return;
            }
        }

        let note = "the cs::generic attribute can only be applied to sequences and dictionaries";
        report_unexpected_attribute(self, span, Some(note), reporter);
    }
}

implement_attribute_kind_for!(CsGeneric, "cs::generic", false);
