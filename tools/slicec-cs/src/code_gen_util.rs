// Copyright (c) ZeroC, Inc.

use slicec::grammar::{Encoding, Member};

/// The context that a type is being used in while generating code. This is used primarily by the
/// `type_to_string` methods in each of the language mapping's code generators.
#[derive(Clone, Copy, Debug, Eq, Hash, PartialEq)]
pub enum TypeContext {
    /// Used when generating the types of fields in structs, classes, and exceptions, or when generating types that are
    /// parts of other types, such as the element type of a sequence, or the success & failure types of results.
    Field,
    /// Used when generating the types of operation parameters and return types in places where they're being decoded.
    IncomingParam,
    /// Used when generating the types of operation parameters and return types in places where they're being encoded.
    OutgoingParam,
}

pub fn get_bit_sequence_size<T: Member>(encoding: Encoding, members: &[&T]) -> usize {
    if encoding == Encoding::Slice1 {
        return 0;
    }

    members
        .iter()
        .filter(|member| !member.is_tagged() && member.data_type().is_optional)
        .count()
}
