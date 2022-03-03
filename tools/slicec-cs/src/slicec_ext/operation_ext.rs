// Copyright (c) ZeroC, Inc. All rights reserved.

use super::{EntityExt, ParameterExt, ParameterSliceExt};
use slice::code_gen_util::TypeContext;
use slice::grammar::{Attributable, Contained, Operation};

pub trait OperationExt {
    /// Returns true if the operation has the `cs:encoded-result` attribute. False otherwise.
    fn has_encoded_result(&self) -> bool;

    /// The name of the generated encoded result type.
    fn encoded_result_struct(&self) -> String;

    /// The Slice format type of the operation
    fn format_type(&self) -> String;

    /// The operation return task.
    fn return_task(&self, is_dispatch: bool) -> String;
}

impl OperationExt for Operation {
    fn has_encoded_result(&self) -> bool {
        self.has_attribute("cs:encoded-result", true)
    }

    fn encoded_result_struct(&self) -> String {
        format!(
            "{}.{}EncodedResult",
            self.parent().unwrap().interface_name(),
            self.escape_identifier()
        )
    }

    fn format_type(&self) -> String {
        match self.get_attribute("format", true) {
            Some(format) if format.len() == 1 => match format.first().unwrap().as_str() {
                "sliced" => "IceRpc.Slice.FormatType.Sliced".to_owned(),
                "compact" => "default".to_owned(), // compact is the default value
                _ => panic!("unexpected format type"),
            },
            _ => "default".to_owned(),
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


#[cfg(test)]
mod tests {
    use super::*;
    use slice::parser::parse_string;

    macro_rules! setup_operation_return_task_tests {
        ($($name:ident: $value:expr,)*) => {
        $(
            #[test]
            fn $name() {
                let (slice_return_type, is_dispatch, expected) = $value;

                let slice = format!("\
                module Test;
                interface TestInterface
                {{
                    op(){};
                }}
                ", if slice_return_type.is_empty() {
                    "".to_owned()
                } else {
                    format!(" -> {}", slice_return_type)
                });

                let ast = parse_string(&slice).unwrap();
                let operation_ptr = ast.find_typed_entity::<Operation>("::Test::TestInterface::op").unwrap();
                let operation = operation_ptr.borrow();

                let return_task = operation.return_task(is_dispatch);

                assert_eq!(
                    expected,
                    &return_task
                );
            }
        )*
        }
    }

    setup_operation_return_task_tests! {
        dispatch_return_tuple_task: ("(myString: string, anInt: int)",
                                     true,
                                     "global::System.Threading.Tasks.ValueTask<(string MyString, int AnInt)>"),

        proxy_return_tuple_task: ("(myString: string, anInt: int)",
                                  false,
                                  "global::System.Threading.Tasks.Task<(string MyString, int AnInt)>"),

        dispatch_void_return_task: ("", true, "global::System.Threading.Tasks.ValueTask"),
        proxy_void_return_task: ("", false, "global::System.Threading.Tasks.Task"),
    }
}
