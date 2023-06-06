// Copyright (c) ZeroC, Inc.

use super::{EntityExt, InterfaceExt, MemberExt, ParameterExt, ParameterSliceExt};
use crate::cs_attributes::match_cs_encoded_result;
use crate::cs_util::FieldType;
use slicec::grammar::{AttributeFunctions, Contained, Operation};
use slicec::utils::code_gen_util::TypeContext;

pub trait OperationExt {
    /// Returns true if the operation has the `cs::encodedResult` attribute; otherwise, false.
    fn has_encoded_result(&self) -> bool;

    /// The name of the generated encoded result type.
    fn encoded_result_struct(&self) -> String;

    /// Returns the format that classes should be encoded with.
    fn get_class_format(&self, is_dispatch: bool) -> &str;

    /// The operation return task.
    fn return_task(&self, is_dispatch: bool) -> String;
}

impl OperationExt for Operation {
    fn has_encoded_result(&self) -> bool {
        self.has_attribute(match_cs_encoded_result)
    }

    fn encoded_result_struct(&self) -> String {
        format!(
            "{}.{}EncodedResult",
            self.parent().service_name(),
            self.escape_identifier(),
        )
    }

    fn get_class_format(&self, is_dispatch: bool) -> &str {
        let use_sliced_format = match is_dispatch {
            true => self.slice_classes_in_return(),
            false => self.slice_classes_in_arguments(),
        };
        match use_sliced_format {
            true => "ClassFormat.Sliced",
            false => "default", // `ClassFormat.Compact` is the default value
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
                format!("global::System.Threading.Tasks.ValueTask<{return_type}>")
            } else {
                format!("global::System.Threading.Tasks.Task<{return_type}>")
            }
        }
    }
}

fn operation_return_type(operation: &Operation, is_dispatch: bool, context: TypeContext) -> String {
    let ns = operation.parent().namespace();
    if is_dispatch && operation.has_encoded_result() {
        if let Some(stream_member) = operation.streamed_return_member() {
            format!(
                "({} EncodedResult, {} {})",
                operation.encoded_result_struct(),
                stream_member.cs_type_string(&ns, context, false),
                stream_member.field_name(FieldType::NonMangled),
            )
        } else {
            operation.encoded_result_struct()
        }
    } else {
        match operation.return_members().as_slice() {
            [] => "void".to_owned(),
            [member] => member.cs_type_string(&ns, context, false),
            members => members.to_tuple_type(&ns, context, false),
        }
    }
}
