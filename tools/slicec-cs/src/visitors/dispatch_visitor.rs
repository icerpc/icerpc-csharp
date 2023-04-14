// Copyright (c) ZeroC, Inc.

use crate::builders::{AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType};
use crate::cs_util::*;
use crate::decoding::*;
use crate::encoded_result::encoded_result_struct;
use crate::encoding::*;
use crate::generated_code::GeneratedCode;
use crate::slicec_ext::*;
use slice::code_block::CodeBlock;

use slice::grammar::*;
use slice::utils::code_gen_util::*;
use slice::visitor::Visitor;

pub struct DispatchVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl Visitor for DispatchVisitor<'_> {
    fn visit_interface_start(&mut self, interface_def: &Interface) {
        let namespace = interface_def.namespace();
        let bases = interface_def.base_interfaces();
        let service_name = interface_def.service_name();
        let slice_interface = interface_def.module_scoped_identifier();
        let access = interface_def.access_modifier();
        let mut interface_builder = ContainerBuilder::new(&format!("{access} partial interface"), &service_name);

        let remarks = format!(
            r#"
The Slice compiler generated this server-side interface from Slice interface <c>{slice_interface}</c>.
Your service implementation must implement this interface and derive from <see cref="Service" />.
"#
        );

        interface_builder
            .add_comments(interface_def.formatted_doc_comment_with_remarks(remarks))
            .add_type_id_attribute(interface_def)
            .add_container_attributes(interface_def);

        interface_builder.add_bases(
            &bases
                .iter()
                .map(|b| b.scoped_service_name(&namespace))
                .collect::<Vec<_>>(),
        );

        interface_builder
            .add_block(request_class(interface_def))
            .add_block(response_class(interface_def));

        if interface_def.supported_encodings().supports(&Encoding::Slice1) {
            interface_builder.add_block(
                format!(
                    "\
private static readonly IActivator _defaultActivator =
    SliceDecoder.GetActivator(typeof({service_name}).Assembly);"
                )
                .into(),
            );
        }

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
            .insert_scoped(interface_def, interface_builder.build());
    }
}

fn request_class(interface_def: &Interface) -> CodeBlock {
    let bases = interface_def.base_interfaces();
    let operations = interface_def
        .operations()
        .iter()
        .filter(|o| !o.parameters.is_empty())
        .cloned()
        .collect::<Vec<_>>();

    if operations.is_empty() {
        return "".into();
    }

    let mut class_builder = ContainerBuilder::new(
        if bases.is_empty() {
            "public static class"
        } else {
            "public static new class"
        },
        "Request",
    );

    class_builder
        .add_comment("summary", "Provides static methods that decode request payloads.")
        .add_comment(
            "remarks",
            format!(
                "The Slice compiler generated this static class from Slice interface <c>{}</c>.",
                &interface_def.module_scoped_identifier()
            ),
        );

    for operation in operations {
        let parameters = operation.parameters();

        let namespace = &operation.namespace();

        let function_type = if operation.streamed_parameter().is_some() {
            FunctionType::BlockBody
        } else {
            FunctionType::ExpressionBody
        };

        let mut builder = FunctionBuilder::new(
            if function_type == FunctionType::BlockBody {
                "public static async"
            } else {
                "public static"
            },
            &format!(
                "global::System.Threading.Tasks.ValueTask<{}>",
                &parameters.to_tuple_type(namespace, TypeContext::Decode, false),
            ),
            &operation.escape_identifier_with_suffix("Async"),
            function_type,
        );

        builder.add_comment(
            "summary",
            format!(
                r#"Decodes the request payload of operation <c>{operation_identifier}</c>."#,
                operation_identifier = operation.identifier(),
            ),
        );

        builder.add_parameter(
            "IceRpc.IncomingRequest",
            "request",
            None,
            Some("The incoming request.".to_owned()),
        );
        builder.add_parameter(
            "global::System.Threading.CancellationToken",
            "cancellationToken",
            None,
            Some("A cancellation token that receives the cancellation requests.".to_owned()),
        );
        builder.set_body(request_decode_body(operation));

        class_builder.add_block(builder.build());
    }

    class_builder.build()
}

fn response_class(interface_def: &Interface) -> CodeBlock {
    let bases = interface_def.base_interfaces();

    let operations = interface_def
        .operations()
        .iter()
        .filter(|o| o.has_non_streamed_return_members())
        .cloned()
        .collect::<Vec<_>>();

    if operations.is_empty() {
        return "".into();
    }

    let mut class_builder = ContainerBuilder::new(
        if bases.is_empty() {
            "public static class"
        } else {
            "public static new class"
        },
        "Response",
    );

    class_builder
        .add_comment(
            "summary",
            "Provides static methods that encode return values into response payloads.",
        )
        .add_comment(
            "remarks",
            format!(
                "The Slice compiler generated this static class from Slice interface <c>{}</c>.",
                &interface_def.module_scoped_identifier()
            ),
        );

    for operation in operations {
        let non_streamed_returns = operation.non_streamed_return_members();

        let namespace = &operation.namespace();
        let operation_name = &operation.escape_identifier();

        let mut builder = FunctionBuilder::new(
            "public static",
            "global::System.IO.Pipelines.PipeReader",
            operation_name,
            FunctionType::BlockBody,
        );

        builder.add_comment(
            "summary",
            format!(
                "Encodes the return value of operation <c>{}</c> into a response payload.",
                operation.identifier(),
            ),
        );

        match non_streamed_returns.as_slice() {
            [param] => {
                builder.add_parameter(
                    &param.cs_type_string(namespace, TypeContext::Encode, false),
                    "returnValue",
                    None,
                    Some("The operation return value.".to_owned()),
                );
            }
            _ => {
                for param in &non_streamed_returns {
                    builder.add_parameter(
                        &param.cs_type_string(namespace, TypeContext::Encode, false),
                        &param.parameter_name(),
                        None,
                        param.formatted_parameter_doc_comment(),
                    );
                }
            }
        }

        builder.add_parameter(
            "SliceEncodeOptions?",
            "encodeOptions",
            Some("null"),
            Some("The Slice encode options.".to_owned()),
        );

        builder.add_comment("returns", "A new response payload.");

        builder.set_body(encode_operation(operation, true, "return"));

        class_builder.add_block(builder.build());
    }

    class_builder.build()
}

fn request_decode_body(operation: &Operation) -> CodeBlock {
    let mut code = CodeBlock::default();

    let namespace = &operation.namespace();

    if let Some(stream_member) = operation.streamed_parameter() {
        let non_streamed_parameters = operation.non_streamed_parameters();
        if non_streamed_parameters.is_empty() {
            writeln!(
                code,
                "await request.DecodeEmptyArgsAsync({encoding}, cancellationToken).ConfigureAwait(false);",
                encoding = operation.encoding.to_cs_encoding(),
            );

            let stream_type = stream_member.data_type();
            match stream_type.concrete_type() {
                Types::Primitive(Primitive::UInt8) if !stream_type.is_optional => {
                    writeln!(code, "return request.DetachPayload();");
                }
                _ => {
                    writeln!(
                        code,
                        "\
var payloadContinuation = request.DetachPayload();
return {}",
                        decode_operation_stream(stream_member, namespace, operation.encoding, true)
                    )
                }
            }
        } else {
            writeln!(
                code,
                "\
var {args} = await request.DecodeArgsAsync(
    {encoding},
    {decode_func},
    defaultActivator: null,
    cancellationToken).ConfigureAwait(false);",
                args = non_streamed_parameters.to_argument_tuple("sliceP_"),
                encoding = operation.encoding.to_cs_encoding(),
                decode_func = request_decode_func(operation).indent(),
            );
            let stream_type = stream_member.data_type();
            match stream_type.concrete_type() {
                Types::Primitive(Primitive::UInt8) if !stream_type.is_optional => {
                    writeln!(
                        code,
                        "var {} = request.DetachPayload();",
                        stream_member.parameter_name_with_prefix("sliceP_"),
                    )
                }
                _ => writeln!(
                    code,
                    "\
var payloadContinuation = request.DetachPayload();
var {stream_parameter_name} = {decode_operation_stream}
",
                    stream_parameter_name = stream_member.parameter_name_with_prefix("sliceP_"),
                    decode_operation_stream =
                        decode_operation_stream(stream_member, namespace, operation.encoding, true),
                ),
            }
            writeln!(code, "return {};", operation.parameters().to_argument_tuple("sliceP_"));
        }
    } else {
        writeln!(
            code,
            "\
request.DecodeArgsAsync(
    {encoding},
    {decode_func},
    defaultActivator: {default_activator},
    cancellationToken)
",
            encoding = operation.encoding.to_cs_encoding(),
            decode_func = request_decode_func(operation).indent(),
            default_activator = default_activator(operation.encoding),
        );
    }
    code
}

fn request_decode_func(operation: &Operation) -> CodeBlock {
    let namespace = &operation.namespace();

    let parameters = operation.non_streamed_parameters();
    assert!(!parameters.is_empty());

    let use_default_decode_func = parameters.len() == 1
        && get_bit_sequence_size(operation.encoding, &parameters) == 0
        && parameters.first().unwrap().tag.is_none();

    if use_default_decode_func {
        let param = parameters.first().unwrap();
        decode_func(param.data_type(), namespace, operation.encoding)
    } else {
        format!(
            "(ref SliceDecoder decoder) =>
{{
    {}
}}",
            decode_operation(operation, true).indent(),
        )
        .into()
    }
}

fn operation_declaration(operation: &Operation) -> CodeBlock {
    FunctionBuilder::new(
        "public",
        &operation.return_task(true),
        &operation.escape_identifier_with_suffix("Async"),
        FunctionType::Declaration,
    )
    .add_comments(operation.formatted_doc_comment())
    .add_operation_parameters(operation, TypeContext::Decode)
    .add_container_attributes(operation)
    .build()
}

fn operation_dispatch(operation: &Operation) -> CodeBlock {
    let operation_name = &operation.escape_identifier();
    let internal_name = format!("SliceD{}Async", &operation_name);

    format!(
        r#"
[SliceOperation("{name}")]
[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
protected static async global::System.Threading.Tasks.ValueTask<IceRpc.OutgoingResponse> {internal_name}(
    {service_name} target,
    IceRpc.IncomingRequest request,
    global::System.Threading.CancellationToken cancellationToken)
{{
    {dispatch_body}
}}
"#,
        name = operation.identifier(),
        service_name = operation.parent().unwrap().service_name(),
        dispatch_body = operation_dispatch_body(operation).indent(),
    )
    .into()
}

fn operation_dispatch_body(operation: &Operation) -> CodeBlock {
    let async_operation_name = &operation.escape_identifier_with_suffix("Async");
    let parameters = operation.parameters();
    let return_parameters = operation.return_members();

    let mut check_and_decode = CodeBlock::default();

    if !operation.is_idempotent {
        check_and_decode.writeln("request.CheckNonIdempotent();");
    }

    if operation.compress_return() {
        check_and_decode.writeln(
            "\
request.Features = IceRpc.Features.FeatureCollectionExtensions.With(
    request.Features,
    IceRpc.Features.CompressFeature.Compress);
            ",
        )
    }

    let encoding = operation.encoding.to_cs_encoding();

    match parameters.as_slice() {
        [] => {
            // Verify the payload is indeed empty (it can contain tagged params that we have to
            // skip).
            writeln!(
                check_and_decode,
                "\
await request.DecodeEmptyArgsAsync({encoding}, cancellationToken).ConfigureAwait(false);",
            );
        }
        [parameter] => {
            writeln!(
                check_and_decode,
                "var {var_name} = await Request.{async_operation_name}(request, cancellationToken).ConfigureAwait(false);",
                var_name = parameter.parameter_name_with_prefix("sliceP_"),
            )
        }
        _ => {
            // > 1 parameter
            writeln!(
                check_and_decode,
                "var args = await Request.{async_operation_name}(request, cancellationToken).ConfigureAwait(false);",
            )
        }
    };

    let mut dispatch_and_return = CodeBlock::default();

    if operation.has_encoded_result() {
        let mut args = vec![];

        match parameters.as_slice() {
            [p] => {
                args.push(p.parameter_name_with_prefix("sliceP_"));
            }
            _ => {
                for p in parameters {
                    args.push("args.".to_owned() + &p.field_name(FieldType::NonMangled));
                }
            }
        }

        args.push("request.Features".to_owned());
        args.push("cancellationToken".to_owned());

        writeln!(
            dispatch_and_return,
            "var returnValue = await target.{async_operation_name}({args}).ConfigureAwait(false);",
            args = args.join(", "),
        );
        if operation.streamed_return_member().is_some() {
            writeln!(
                dispatch_and_return,
                "\
return new IceRpc.OutgoingResponse(request)
{{
    Payload = returnValue.EncodedResult.Payload,
    PayloadContinuation = {payload_continuation}
}};",
                payload_continuation = payload_continuation(operation, encoding).indent(),
            );
        } else {
            writeln!(
                dispatch_and_return,
                "return new IceRpc.OutgoingResponse(request) {{ Payload = returnValue.Payload }};",
            );
        }
    } else {
        let mut args = match parameters.as_slice() {
            [parameter] => vec![parameter.parameter_name_with_prefix("sliceP_")],
            _ => parameters
                .iter()
                .map(|parameter| format!("args.{}", &parameter.field_name(FieldType::NonMangled)))
                .collect(),
        };
        args.push("request.Features".to_owned());
        args.push("cancellationToken".to_owned());

        writeln!(
            dispatch_and_return,
            "{return_value}await target.{async_operation_name}({args}).ConfigureAwait(false);",
            return_value = if !return_parameters.is_empty() {
                "var returnValue = "
            } else {
                ""
            },
            args = args.join(", "),
        );

        if operation.return_type.is_empty() {
            writeln!(dispatch_and_return, "return new IceRpc.OutgoingResponse(request);")
        } else {
            writeln!(
                dispatch_and_return,
                "\
return new IceRpc.OutgoingResponse(request)
{{
    Payload = {payload},
    PayloadContinuation = {payload_continuation}
}};",
                payload = dispatch_return_payload(operation, encoding),
                payload_continuation = payload_continuation(operation, encoding).indent(),
            );
        }
    }

    if let Throws::None = &operation.throws {
        format!(
            "
{check_and_decode}
{dispatch_and_return}"
        )
    } else {
        let exception_type = match &operation.throws {
            Throws::Specific(exception) => exception.escape_scoped_identifier(&operation.namespace()),
            Throws::AnyException => "SliceException".to_owned(),
            Throws::None => unreachable!(),
        };

        format!(
            "
{check_and_decode}
try
{{
    {dispatch_and_return}
}}
catch ({exception_type} sliceException) when (!sliceException.ConvertToUnhandled)
{{
    return request.CreateSliceExceptionResponse(sliceException, {encoding});
}}",
            dispatch_and_return = dispatch_and_return.indent(),
        )
    }
    .into()
}

// only called for non-void operations
fn dispatch_return_payload(operation: &Operation, encoding: &str) -> CodeBlock {
    let non_streamed_return_values = operation.non_streamed_return_members();

    let mut returns = vec![];

    returns.push(match operation.return_members().len() {
        1 => "returnValue".to_owned(),
        _ => non_streamed_return_values
            .iter()
            .map(|r| format!("returnValue.{}", &r.field_name(FieldType::NonMangled)))
            .collect::<Vec<_>>()
            .join(", "),
    });

    match non_streamed_return_values.len() {
        0 => format!("{encoding}.CreateSizeZeroPayload()"),
        _ => format!(
            "Response.{operation_name}({args}, request.Features.Get<ISliceFeature>()?.EncodeOptions)",
            operation_name = operation.escape_identifier(),
            args = returns.join(", "),
        ),
    }
    .into()
}

fn payload_continuation(operation: &Operation, encoding: &str) -> CodeBlock {
    let namespace = &operation.namespace();
    let return_values = operation.return_members();
    match operation.streamed_return_member() {
        None => "null".into(),
        Some(stream_return) => {
            let stream_type = stream_return.data_type();

            let stream_arg = if return_values.len() == 1 {
                "returnValue".to_owned()
            } else {
                format!("returnValue.{}", &stream_return.field_name(FieldType::NonMangled))
            };

            match stream_type.concrete_type() {
                Types::Primitive(Primitive::UInt8) if !stream_type.is_optional => stream_arg.into(),
                _ => format!(
                    "\
{stream_arg}.ToPipeReader(
    {encode_stream_parameter},
    {use_segments},
    {encoding},
    {encode_options})",
                    encode_stream_parameter =
                        encode_stream_parameter(stream_type, TypeContext::Encode, namespace, operation.encoding)
                            .indent(),
                    use_segments = stream_type.fixed_wire_size().is_none(),
                    encode_options = "request.Features.Get<ISliceFeature>()?.EncodeOptions",
                )
                .into(),
            }
        }
    }
}
