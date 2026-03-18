// Copyright (c) ZeroC, Inc.

use super::{EntityExt, MemberExt, ParameterExt, ParameterSliceExt};
use crate::code_gen_util::TypeContext;
use crate::cs_attributes::CsEncodedReturn;
use slicec::grammar::{AttributeFunctions, Operation};

pub trait OperationExt {
    fn invocation_return_task(&self, task_type: &str) -> String;
    fn dispatch_return_task(&self) -> String;
}

impl OperationExt for Operation {
    fn invocation_return_task(&self, task_type: &str) -> String {
        let namespace = &self.namespace();
        let return_type = match self.return_members().as_slice() {
            [] => "".to_owned(),
            members => format!("<{}>", members.to_tuple_type(namespace, TypeContext::IncomingParam)),
        };
        format!("global::System.Threading.Tasks.{task_type}{return_type}")
    }

    fn dispatch_return_task(&self) -> String {
        let return_members = self.return_members();
        if return_members.is_empty() {
            "global::System.Threading.Tasks.ValueTask".to_owned()
        } else {
            let namespace = self.namespace();
            let return_type = if self.has_attribute::<CsEncodedReturn>() {
                if let Some(stream_member) = self.streamed_return_member() {
                    format!(
                        "(global::System.IO.Pipelines.PipeReader Payload, {} {})",
                        stream_member.cs_type_string(&namespace, TypeContext::OutgoingParam),
                        stream_member.field_name(),
                    )
                } else {
                    "global::System.IO.Pipelines.PipeReader".to_owned()
                }
            } else {
                return_members.to_tuple_type(&namespace, TypeContext::OutgoingParam)
            };
            format!("global::System.Threading.Tasks.ValueTask<{return_type}>")
        }
    }
}
