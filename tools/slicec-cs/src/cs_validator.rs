// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::error::ErrorHandler;
use slice::visitor::Visitor;

/// CsValidator visits all the elements in a slice file to check for errors and warnings specific to
/// the slicec-cs compiler. This is the final validation step, and the last phase of compilation
/// before code generation occurs.
#[derive(Debug)]
pub(crate) struct CsValidator<'a> {
    /// Reference to the compiler's error handler,
    error_handler: &'a mut ErrorHandler,
}

impl<'a> CsValidator<'a> {
    /// Creates a new cs-validator.
    pub(crate) fn new(error_handler: &'a mut ErrorHandler) -> Self {
        CsValidator { error_handler }
    }
}

impl<'a> Visitor for CsValidator<'a> {
    // TODO add validation logic here.
}
