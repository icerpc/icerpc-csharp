// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::visitor::Visitor;

/// CsValidator visits all the elements in a slice file to check for errors and warnings specific to
/// the slicec-cs compiler. This is the final validation step, and the last phase of compilation
/// before code generation occurs.
#[derive(Debug)]
pub(crate) struct CsValidator;

impl Visitor for CsValidator {
    // TODO add validation logic here.
}
