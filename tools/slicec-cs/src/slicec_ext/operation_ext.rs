// Copyright (c) ZeroC, Inc.

use super::{EntityExt, MemberExt, ParameterExt, ParameterSliceExt};
use crate::code_gen_util::TypeContext;
use crate::cs_attributes::CsEncodedReturn;
use slicec::grammar::{AttributeFunctions, Contained, Operation};

pub trait OperationExt {
    /// Returns the format that classes should be encoded with.
    fn get_class_format(&self, is_dispatch: bool) -> &str;

    /// The operation return task.
    fn return_task(&self, is_dispatch: bool) -> String;
}

impl OperationExt for Operation {
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
                    TypeContext::OutgoingParam
                } else {
                    TypeContext::IncomingParam
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
    let namespace = operation.parent().namespace();
    if is_dispatch && operation.has_attribute::<CsEncodedReturn>() {
        if let Some(stream_member) = operation.streamed_return_member() {
            format!(
                "(global::System.IO.Pipelines.PipeReader Payload, {} {})",
                stream_member.cs_type_string(&namespace, context),
                stream_member.field_name(),
            )
        } else {
            "global::System.IO.Pipelines.PipeReader".to_owned()
        }
    } else {
        match operation.return_members().as_slice() {
            [] => "void".to_owned(),
            [member] => member.cs_type_string(&namespace, context),
            members => members.to_tuple_type(&namespace, context),
        }
    }
}
