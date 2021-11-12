use slice::code_gen_util::*;
use slice::grammar::*;
use slice::visitor::Visitor;

use crate::builders::{
    AttributeBuilder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType,
};
use crate::code_block::CodeBlock;
use crate::comments::doc_comment_message;
use crate::cs_util::*;
use crate::decoding::*;
use crate::encoded_result::encoded_result_struct;
use crate::encoding::*;
use crate::generated_code::GeneratedCode;
use crate::slicec_ext::*;

pub struct DispatchVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl<'a> Visitor for DispatchVisitor<'_> {
    fn visit_interface_start(&mut self, interface_def: &Interface) {
        let bases = interface_def.base_interfaces();
        let interface_name = interface_def.interface_name();
        let access = interface_def.access_modifier();
        let mut interface_builder =
            ContainerBuilder::new(&format!("{} partial interface", access), &interface_name);

        let summary_comment = format!(
            r#"Interface used to implement services for Slice interface {}. <seealso cref="{}"/>.
{}"#,
            interface_def.identifier(),
            interface_def.proxy_name(),
            doc_comment_message(interface_def)
        );

        interface_builder
            .add_comment("summary", &summary_comment)
            .add_type_id_attribute(interface_def)
            .add_container_attributes(interface_def);

        interface_builder.add_bases(&bases.iter().map(|b| b.interface_name()).collect::<Vec<_>>());

        interface_builder
            .add_block(request_class(interface_def))
            .add_block(response_class(interface_def));

        interface_builder.add_block(format!("\
private static readonly DefaultIceDecoderFactories _defaultIceDecoderFactories = new(typeof({}).Assembly);
", interface_name).into());

        for operation in interface_def.operations() {
            if operation.has_encoded_result() {
                interface_builder.add_block(encoded_result_struct(operation));
            }
            interface_builder.add_block(operation_declaration(operation));
        }

        for operation in interface_def.operations() {
            interface_builder.add_block(operation_dispatch(operation));
        }

        self.generated_code
            .insert_scoped(interface_def, interface_builder.build().into());
    }
}

fn request_class(interface_def: &Interface) -> CodeBlock {
    let bases = interface_def.base_interfaces();
    let operations = interface_def
        .operations()
        .iter()
        .filter(|o| o.has_nonstreamed_parameters())
        .cloned()
        .collect::<Vec<_>>();

    if operations.is_empty() {
        return "".into();
    }

    let access = interface_def.access_modifier();
    let mut class_builder = ContainerBuilder::new(
        &if bases.is_empty() {
            format!("{} static class", access)
        } else {
            format!("{} static new class", access)
        },
        "Request",
    );

    class_builder.add_comment(
        "summary",
        "Provides static methods that read the arguments of requests.",
    );

    for operation in operations {
        let parameters = operation.parameters();

        let operation_name = operation.escape_identifier();

        let decoder_factory = if operation.sends_classes() {
            "request.GetIceDecoderFactory(_defaultIceDecoderFactories.Ice11DecoderFactory)"
        } else {
            "request.GetIceDecoderFactory(_defaultIceDecoderFactories)"
        };

        let namespace = &operation.namespace();

        let code = format!(
            "\
///<summary>Decodes the argument{s} of operation {operation_name}.</summary>
{access} static {return_type} {operation_name}(IceRpc.IncomingRequest request) =>
    request.ToArgs(
        {decoder_factory},
        {decode_func});",
            access = access,
            s = if parameters.len() == 1 { "" } else { "s" },
            return_type = parameters.to_tuple_type(namespace, TypeContext::Incoming),
            operation_name = operation_name,
            decoder_factory = decoder_factory,
            decode_func = request_decode_func(operation).indent().indent(),
        );

        class_builder.add_block(code.into());
    }

    class_builder.build().into()
}

fn response_class(interface_def: &Interface) -> CodeBlock {
    let bases = interface_def.base_interfaces();

    let operations = interface_def
        .operations()
        .iter()
        .filter(|o| o.has_nonstreamed_return_members())
        .cloned()
        .collect::<Vec<_>>();

    if operations.is_empty() {
        return "".into();
    }

    let access = interface_def.access_modifier();
    let mut class_builder = ContainerBuilder::new(
        &if bases.is_empty() {
            format!("{} static class", access)
        } else {
            format!("{} static new class", access)
        },
        "Response",
    );

    class_builder.add_comment(
        "summary",
        "Provides static methods that write the return values of responses.",
    );

    for operation in operations {
        let non_streamed_returns = operation.nonstreamed_return_members();

        let namespace = &operation.namespace();
        let operation_name = &operation.escape_identifier();
        let returns_classes = operation.returns_classes();
        let return_type = &non_streamed_returns.to_tuple_type(namespace, TypeContext::Outgoing);

        let mut builder = FunctionBuilder::new(
            &format!("{} static", access),
            "global::System.ReadOnlyMemory<global::System.ReadOnlyMemory<byte>>",
            operation_name,
            FunctionType::ExpressionBody,
        );

        builder
            .add_comment(
                "summary",
                &format!(
                    "Creates a response payload for operation {}.",
                    &operation_name
                ),
            )
            .add_comment("returns", "A new response payload.");

        if !returns_classes {
            builder.add_parameter(
                "IceEncoding",
                "encoding",
                None,
                Some("The encoding of the payload"),
            );
        }

        if non_streamed_returns.len() == 1 {
            builder.add_parameter(
                return_type,
                "returnValue",
                None,
                Some("The return value to write into the new response payload."),
            );
        } else {
            builder.add_parameter(
                return_type,
                "returnValueTuple",
                None,
                Some("The return values to write into the new response payload."),
            );
        };

        let body = format!(
            "\
{encoding}.{encoding_operation}(
    {return_arg},
    {encode_action})",
            encoding = match returns_classes {
                true => "Ice11Encoding",
                _ => "encoding",
            },
            encoding_operation = match non_streamed_returns.len() {
                1 => "CreatePayloadFromSingleReturnValue",
                _ => "CreatePayloadFromReturnValueTuple",
            },
            return_arg = match non_streamed_returns.len() {
                1 => "returnValue",
                _ => "returnValueTuple",
            },
            encode_action = response_encode_action(operation)
        );

        builder.set_body(body.into());

        class_builder.add_block(builder.build());
    }

    class_builder.build().into()
}

fn request_decode_func(operation: &Operation) -> CodeBlock {
    let namespace = &operation.namespace();

    let parameters = operation.parameters();

    let use_default_decode_func = parameters.len() == 1
        && get_bit_sequence_size(&parameters) == 0
        && parameters.first().unwrap().tag.is_none();

    if use_default_decode_func {
        let param = parameters.first().unwrap();
        // request_decode_func should not be called when the operation has a single parameter that is streamed.
        assert!(!param.is_streamed);
        decode_func(param.data_type(), namespace)
    } else {
        format!(
            "decoder =>
{{
    {}
}}",
            decode_operation(operation, true).indent()
        )
        .into()
    }
}

pub fn response_encode_action(operation: &Operation) -> CodeBlock {
    let namespace = &operation.namespace();

    // We only want the non-streamed returns
    let returns = operation.nonstreamed_return_members();

    // When the operation returns a T? where T is an interface or a class, there is a built-in
    // encoder, so defaultEncodeAction is true.
    let use_default_encode_action = returns.len() == 1
        && get_bit_sequence_size(&returns) == 0
        && returns.first().unwrap().tag.is_none();

    if use_default_encode_action {
        encode_action(returns.first().unwrap().data_type(), namespace, true, true)
    } else {
        let encoder_class = if operation.returns_classes() {
            "Ice11Encoder"
        } else {
            "IceEncoder"
        };

        format!(
            "({encoder} encoder, {_in}{tuple_type} value) => {{ {encode_action} }}",
            encoder = encoder_class,
            _in = if returns.len() == 1 { "" } else { "in " },
            tuple_type = returns.to_tuple_type(namespace, TypeContext::Outgoing),
            encode_action = encode_operation(operation, true),
        )
        .into()
    }
}

fn operation_declaration(operation: &Operation) -> CodeBlock {
    // TODO: operation obsolete deprecation
    FunctionBuilder::new(
        &operation.parent().unwrap().access_modifier(),
        &operation.return_task(true),
        &(operation.escape_identifier_with_suffix("Async")),
        FunctionType::Declaration,
    )
    .add_comment("summary", &doc_comment_message(operation))
    .add_operation_parameters(operation, TypeContext::Incoming)
    .build()
}

fn operation_dispatch(operation: &Operation) -> CodeBlock {
    let operation_name = &operation.escape_identifier();
    let internal_name = format!("IceD{}Async", &operation_name);

    format!(
        r#"
[IceRpc.Slice.Operation("{name}")]
protected static async global::System.Threading.Tasks.ValueTask<(IceEncoding, global::System.ReadOnlyMemory<global::System.ReadOnlyMemory<byte>>, IceRpc.IStreamParamSender?)> {internal_name}(
    {interface_name} target,
    IceRpc.IncomingRequest request,
    IceRpc.Dispatch dispatch,
    global::System.Threading.CancellationToken cancel)
{{
    {dispatch_body}
}}
"#,
        name = operation.identifier(),
        internal_name = internal_name,
        interface_name = operation.parent().unwrap().interface_name(),
        dispatch_body = operation_dispatch_body(operation).indent()
    )
    .into()
}

fn operation_dispatch_body(operation: &Operation) -> CodeBlock {
    let namespace = &operation.namespace();
    let operation_name = &operation.escape_identifier();
    let parameters = operation.parameters();
    let stream_parameter = operation.streamed_parameter();
    let return_parameters = operation.return_members();

    let mut code = CodeBlock::new();

    if stream_parameter.is_none() {
        code.writeln("request.StreamReadingComplete();");
    }

    if !operation.is_idempotent {
        code.writeln("request.CheckNonIdempotent();");
    }

    if operation.compress_return() {
        // At this point, Dispatch is just created and the application had no opportunity to set any
        // response feature.
        code.writeln("dispatch.ResponseFeatures = IceRpc.FeatureCollectionExtensions.CompressPayload(dispatch.ResponseFeatures);");
    }

    // Even when the parameters are empty, we verify the payload is indeed empty (can contain
    // tagged params
    // that we skip).
    if parameters.is_empty() {
        code.writeln(
            "request.CheckEmptyArgs(request.GetIceDecoderFactory(_defaultIceDecoderFactories));",
        );
    }

    // TODO: cleanup stream logic
    match parameters.as_slice() {
        [] => {}
        [_] if stream_parameter.is_some() => {
            let stream_parameter = stream_parameter.unwrap();
            let stream_type = stream_parameter.data_type();
            let name = stream_parameter.parameter_name_with_prefix("iceP_");
            let stream_assignment = match stream_type.concrete_type() {
                Types::Primitive(primitive) if matches!(primitive, Primitive::Byte) => {
                    "IceRpc.Slice.StreamParamReceiver.ToByteStream(request)".to_owned()
                }
                _ => {
                    format!(
                        "\
IceRpc.Slice.StreamParamReceiver.ToAsyncEnumerable<{stream_type}>(
    request,
    request.GetIceDecoderFactory(_defaultIceDecoderFactories),
    {decode_func})
    ",
                        stream_type = stream_type.to_type_string(namespace, TypeContext::Outgoing),
                        decode_func = decode_func(stream_type, namespace)
                    )
                }
            };
            writeln!(code, "var {} = {};", name, stream_assignment);
        }
        [parameter] => {
            writeln!(
                code,
                "var {var_name} = Request.{operation_name}(request);",
                var_name = parameter.parameter_name_with_prefix("iceP_"),
                operation_name = operation_name,
            )
        }
        _ => {
            // > 1 parameter
            writeln!(
                code,
                "var args = Request.{operation_name}(request);",
                operation_name = operation_name,
            )
        }
    };

    if operation.has_encoded_result() {
        // TODO: support for stream param with encoded result?

        let mut args = vec![];

        match parameters.as_slice() {
            [p] => {
                args.push(p.parameter_name_with_prefix("iceP_"));
            }
            _ => {
                for p in parameters {
                    args.push("args.".to_owned() + &p.field_name(FieldType::NonMangled));
                }
            }
        }

        args.push("dispatch".to_owned());
        args.push("cancel".to_owned());

        writeln!(
            code,
            "var returnValue = await target.{name}Async({args}).ConfigureAwait(false);",
            name = operation_name,
            args = args.join(", ")
        );

        let encoding = if operation.returns_classes() {
            "IceRpc.Encoding.Ice11"
        } else {
            "request.GetIceEncoding()"
        };

        writeln!(
            code,
            "return ({encoding}, returnValue.Payload, null);",
            encoding = encoding
        );
    } else {
        let mut args = match parameters.as_slice() {
            [parameter] => vec![parameter.parameter_name_with_prefix("iceP_")],
            _ => parameters
                .iter()
                .map(|parameter| format!("args.{}", &parameter.field_name(FieldType::NonMangled)))
                .collect(),
        };
        args.push("dispatch".to_owned());
        args.push("cancel".to_owned());

        writeln!(
            code,
            "{return_value}await target.{operation_name}Async({args}).ConfigureAwait(false);",
            return_value = if !return_parameters.is_empty() {
                "var returnValue = "
            } else {
                ""
            },
            operation_name = operation_name,
            args = args.join(", ")
        );

        let encoding = if operation.returns_classes() {
            "IceRpc.Encoding.Ice11"
        } else {
            code.writeln("var payloadEncoding = request.GetIceEncoding();");
            "payloadEncoding"
        };

        writeln!(
            code,
            "return ({encoding}, {payload}, {stream});",
            encoding = encoding,
            payload = dispatch_return_payload(operation, encoding),
            stream = stream_param_sender(operation, encoding)
        );
    }

    code
}

fn dispatch_return_payload(operation: &Operation, encoding: &str) -> CodeBlock {
    let non_streamed_return_values = operation.nonstreamed_return_members();

    let mut returns = vec![];

    if !operation.returns_classes() {
        returns.push(encoding.to_owned());
    }

    returns.push(match operation.return_members().len() {
        1 => "returnValue".to_owned(),
        _ => format!(
            "({})",
            non_streamed_return_values
                .iter()
                .map(|r| format!("returnValue.{}", &r.field_name(FieldType::NonMangled)))
                .collect::<Vec<_>>()
                .join(", ")
        ),
    });

    match non_streamed_return_values.len() {
        0 => format!("{}.CreateEmptyPayload()", encoding),
        _ => format!(
            "Response.{operation_name}({args})",
            operation_name = operation.escape_identifier(),
            args = returns.join(", ")
        ),
    }
    .into()
}

fn stream_param_sender(operation: &Operation, encoding: &str) -> CodeBlock {
    let namespace = &operation.namespace();
    let return_values = operation.return_members();

    match operation.streamed_return_member() {
        None => "null".into(),
        Some(stream_return) => {
            let stream_type = stream_return.data_type();

            let stream_arg = if return_values.len() == 1 {
                "returnValue".to_owned()
            } else {
                format!(
                    "returnValue.{}",
                    &stream_return.field_name(FieldType::NonMangled)
                )
            };

            match stream_type.concrete_type() {
                Types::Primitive(primitive) if matches!(primitive, Primitive::Byte) => {
                    format!("new IceRpc.Slice.ByteStreamParamSender({})", stream_arg).into()
                }
                _ => {
                    format!("\
new IceRpc.Slice.AsyncEnumerableStreamParamSender<{stream_type}>({stream_arg}, {encoding}, {encode_action})",
                             stream_type = stream_type.to_type_string(namespace, TypeContext::Outgoing),
                             stream_arg = stream_arg,
                             encoding = encoding,
                             encode_action = encode_action(
                                 stream_type,
                                 namespace,
                                 false,
                                 false),
                    ).into()
                }
            }
        }
    }
}
