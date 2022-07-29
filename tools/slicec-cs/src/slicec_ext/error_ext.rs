// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::errors::ErrorKind;
use std::fmt;

pub enum ErrorKindExtended {
    Attribute(String),
    Compiler(ErrorKind),
}

impl ErrorKindExtended {
    pub fn new_attribute(message: impl Into<String>) -> ErrorKindExtended {
        ErrorKindExtended::Attribute(message.into())
    }
}

impl From<ErrorKindExtended> for ErrorKind {
    fn from(error_kind: ErrorKindExtended) -> ErrorKind {
        match error_kind {
            ErrorKindExtended::Attribute(error) => ErrorKind::Syntax(error),
            ErrorKindExtended::Compiler(error_kind) => error_kind,
        }
    }
}

impl fmt::Display for ErrorKindExtended {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        match self {
            ErrorKindExtended::Compiler(kind) => write!(f, "{}", kind),
            ErrorKindExtended::Attribute(error) => write!(f, "{}", error),
        }
    }
}
