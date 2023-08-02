// Copyright (c) ZeroC, Inc.

use super::*;
use slicec::grammar::Types;

#[derive(Debug)]
pub struct CsType {
    pub type_string: String,
}

impl CsType {
    pub fn parse_from(Unparsed { directive, args }: &Unparsed, span: &Span, diagnostics: &mut Diagnostics) -> Self {
        debug_assert_eq!(directive, Self::directive());

        check_that_exactly_one_argument_was_provided(args, Self::directive(), span, diagnostics);

        let type_string = args.first().cloned().unwrap_or_default();
        CsType { type_string }
    }

    pub fn validate_on(&self, applied_on: Attributables, span: &Span, diagnostics: &mut Diagnostics) {
        match applied_on {
            Attributables::CustomType(_) => {}
            Attributables::TypeRef(type_ref)
                if matches!(type_ref.concrete_type(), Types::Sequence(_) | Types::Dictionary(_)) => {}
            _ => {
                let note = "the cs::type attribute can only be applied to sequences, dictionaries, and custom types";
                report_unexpected_attribute(self, span, Some(note), diagnostics);
            }
        }
    }
}

implement_attribute_kind_for!(CsType, "cs::type", false);
