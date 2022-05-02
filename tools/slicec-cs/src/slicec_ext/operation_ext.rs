// Copyright (c) ZeroC, Inc. All rights reserved.

use super::{EntityExt, ParameterExt, ParameterSliceExt};
use slice::code_gen_util::TypeContext;
use slice::grammar::{Attributable, ClassFormat, Contained, Operation};

pub trait OperationExt {
    /// Returns true if the operation has the `cs::encodedResult` attribute. False otherwise.
    fn has_encoded_result(&self) -> bool;

    /// The name of the generated encoded result type.
    fn encoded_result_struct(&self) -> String;

    /// The Slice format type of the operation
    fn format_type(&self) -> &str;

    /// The operation return task.
    fn return_task(&self, is_dispatch: bool) -> String;
}

impl OperationExt for Operation {
    fn has_encoded_result(&self) -> bool {
        self.has_attribute("cs::encodedResult", true)
    }

    fn encoded_result_struct(&self) -> String {
        format!(
            "{}.{}EncodedResult",
            self.parent().unwrap().interface_name(),
            self.escape_identifier()
        )
    }

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
                is_dispatch,
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

fn operation_return_type(operation: &Operation, is_dispatch: bool, context: TypeContext) -> String {
    let return_members = operation.return_members();

    if !return_members.is_empty() && is_dispatch && operation.has_encoded_result() {
        return operation.encoded_result_struct();
    }

    let ns = operation.parent().unwrap().namespace();
    match return_members.as_slice() {
        [] => "void".to_owned(),
        [member] => member.to_type_string(&ns, context, false),
        _ => return_members.to_tuple_type(&ns, context, false),
    }
}
