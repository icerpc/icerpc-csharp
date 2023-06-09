// Copyright (c) ZeroC, Inc.

use crate::cs_attributes::match_cs_namespace;
use crate::cs_util::{escape_keyword, CsCase};
use slicec::grammar::{AttributeFunctions, Module};

use convert_case::Case;

pub trait ModuleExt {
    fn as_namespace(&self) -> String;
}

impl ModuleExt for Module {
    fn as_namespace(&self) -> String {
        self.find_attribute(match_cs_namespace).unwrap_or_else(|| {
            // If this module doesn't have `cs::namespace` applied to it, compute its namespace.
            let segments = self.nested_module_identifier().split("::");
            let cased_segments = segments.map(|s| escape_keyword(&s.to_cs_case(Case::Pascal)));
            cased_segments.collect::<Vec<_>>().join(".")
        })
    }
}
