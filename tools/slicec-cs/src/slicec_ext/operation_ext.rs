// Copyright (c) ZeroC, Inc. All rights reserved.

use super::{EntityExt, ParameterExt, ParameterSliceExt};

use slice::grammar::{ClassFormat, Contained, Operation};
use slice::utils::code_gen_util::TypeContext;

pub trait OperationExt {
    /// The Slice format type of the operation
    fn format_type(&self) -> &str;

    /// The operation return task.
    fn return_task(&self, is_dispatch: bool) -> String;
}

impl OperationExt for Operation {
    fn format_type(&self) -> &str {
        match self.class_format() {
            ClassFormat::Sliced => "IceRpc.Slice.ClassFormat.Sliced",
            ClassFormat::Compact => "default", // compact is the default value
        }
    }

    fn return_task(&self, is_dispatch: bool) -> String {
        let return_members = self.return_members();
        if return_members.is_empty() {
            if is_dispatch {
                "global::System.Threading.Tasks.ValueTask".to_owned()
            } else {
                "global::System.Threading.Tasks.Task".to_owned()
            }
        } else {
            let return_type = operation_return_type(
                self,
                if is_dispatch {
                    TypeContext::Encode
                } else {
                    TypeContext::Decode
                },
            );
            if is_dispatch {
                format!("global::System.Threading.Tasks.ValueTask<{}>", return_type)
            } else {
                format!("global::System.Threading.Tasks.Task<{}>", return_type)
            }
        }
    }
}

fn operation_return_type(operation: &Operation, context: TypeContext) -> String {
    let return_members = operation.return_members();

    let ns = operation.parent().unwrap().namespace();
    match return_members.as_slice() {
        [] => "void".to_owned(),
        [member] => member.cs_type_string(&ns, context, false),
        _ => return_members.to_tuple_type(&ns, context, false),
    }
}
