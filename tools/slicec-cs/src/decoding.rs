// Copyright (c) ZeroC, Inc.

use crate::builders::{Builder, FunctionCallBuilder};
use crate::code_gen_util::get_bit_sequence_size;
use crate::cs_attributes::CsType;
use crate::cs_util::*;
use crate::member_util::get_sorted_members;
use crate::slicec_ext::*;
use convert_case::Case;
use slicec::code_block::CodeBlock;
use slicec::grammar::*;

/// Compute how many bits are needed to decode the provided members, and if more than 0 bits are needed,
/// this generates code that creates a new `BitSequenceReader` with the necessary capacity.
fn initialize_bit_sequence_reader_for<T: Member>(members: &[&T], code: &mut CodeBlock, encoding: Encoding) {
    let bit_sequence_size = get_bit_sequence_size(encoding, members);
    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequenceReader = decoder.GetBitSequenceReader({bit_sequence_size});",
        );
    }
}

pub fn decode_fields(fields: &[&Field], encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();
    initialize_bit_sequence_reader_for(fields, &mut code, encoding);

    let action = |field_name, field_value| writeln!(code, "this.{field_name} = {field_value};");

    decode_fields_core(fields, encoding, action);
    code
}

pub fn decode_enum_fields(fields: &[&Field], enum_class: &str, encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();
    initialize_bit_sequence_reader_for(fields, &mut code, encoding);

    let mut new_instance_builder = FunctionCallBuilder::new(format!("new {enum_class}"));
    new_instance_builder.arguments_on_newline(fields.len() > 1);
    let action = |field_name, field_value| {
        new_instance_builder.add_argument(&format!("{field_name}: {field_value}"));
    };

    decode_fields_core(fields, encoding, action);
    writeln!(code, "var result = {}", new_instance_builder.build());
    code
}

/// Generates code for each of the provided fields by calling the provided `action` for each field.
fn decode_fields_core(fields: &[&Field], encoding: Encoding, mut action: impl FnMut(String, CodeBlock)) {
    for field in get_sorted_members(fields) {
        let namespace = field.namespace();

        let field_name = field.field_name();
        let field_value = match field.is_tagged() {
            true => decode_tagged(field, &namespace, true, encoding),
            false => decode_member(field, &namespace, encoding),
        };

        action(field_name, field_value);
    }
}

pub fn default_activator(encoding: Encoding) -> &'static str {
    if encoding == Encoding::Slice1 {
        "_defaultActivator"
    } else {
        "null"
    }
}

fn decode_member(member: &impl Member, namespace: &str, encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();
    let data_type = member.data_type();
    let type_string = data_type.field_type_string(namespace, true);

    if data_type.is_optional {
        match data_type.concrete_type() {
            Types::CustomType(custom_type_ref) if encoding == Encoding::Slice1 => {
                write!(
                    code,
                    "{decoder_extensions_class}.DecodeNullable{name}(ref decoder);",
                    decoder_extensions_class =
                        custom_type_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                    name = custom_type_ref.cs_identifier(Case::Pascal),
                );
                return code;
            }
            _ if data_type.is_class_type() => {
                // does not use bit sequence
                writeln!(code, "decoder.DecodeNullableClass<{type_string}>();");
                return code;
            }
            _ => {
                write!(code, "bitSequenceReader.Read() ? ");
                // keep going
            }
        }
    }

    match &data_type.concrete_typeref() {
        TypeRefs::Class(_) => {
            assert!(!data_type.is_optional);
            write!(code, "decoder.DecodeClass<{type_string}>()");
        }
        TypeRefs::Primitive(primitive_ref) => {
            if primitive_ref.is_class_type() {
                write!(code, "decoder.DecodeClass<SliceClass>()");
            } else {
                write!(code, "decoder.Decode{}()", primitive_ref.type_suffix());
            }
        }
        TypeRefs::Struct(_) => write!(code, "new {type_string}(ref decoder)"),
        TypeRefs::Dictionary(dictionary_ref) => code.write(&decode_dictionary(dictionary_ref, namespace, encoding)),
        TypeRefs::Sequence(sequence_ref) => code.write(&decode_sequence(sequence_ref, namespace, encoding)),
        TypeRefs::Enum(enum_ref) => {
            write!(
                code,
                "{decoder_extensions_class}.Decode{name}(ref decoder)",
                decoder_extensions_class =
                    enum_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                name = enum_ref.cs_identifier(Case::Pascal),
            );
        }
        TypeRefs::ResultType(result_type_ref) => code.write(&decode_result(result_type_ref, namespace, encoding)),
        TypeRefs::CustomType(custom_type_ref) => {
            write!(
                code,
                "{decoder_extensions_class}.Decode{name}(ref decoder)",
                decoder_extensions_class =
                    custom_type_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                name = custom_type_ref.cs_identifier(Case::Pascal),
            );
        }
    }

    if data_type.is_optional {
        code.write(" : null");
    }

    code
}

fn decode_tagged(member: &impl Member, namespace: &str, constructed_type: bool, encoding: Encoding) -> CodeBlock {
    let data_type = member.data_type();

    assert!(data_type.is_optional);
    assert!(member.is_tagged());

    let decode = FunctionCallBuilder::new("decoder.DecodeTagged")
        .add_argument(member.tag().unwrap())
        .add_argument_if_present(
            (encoding == Encoding::Slice1).then(|| format!("TagFormat.{}", data_type.tag_format().unwrap())),
        )
        .add_argument(decode_func(data_type, namespace, encoding))
        .add_argument_if_present((encoding == Encoding::Slice1).then(|| format!("useTagEndMarker: {constructed_type}")))
        .use_semicolon(false)
        .build();

    decode
}

fn decode_dictionary(dictionary_ref: &TypeRef<Dictionary>, namespace: &str, encoding: Encoding) -> CodeBlock {
    let key_type = &dictionary_ref.key_type;
    let value_type = &dictionary_ref.value_type;

    // decode key
    let decode_key = decode_func(key_type, namespace, encoding);

    // decode value
    let mut decode_value = decode_func(value_type, namespace, encoding);
    if matches!(value_type.concrete_type(), Types::Sequence(_) | Types::Dictionary(_)) {
        write!(decode_value, " as {}", value_type.field_type_string(namespace, true));
    }

    let dictionary_type = dictionary_ref.incoming_parameter_type_string(namespace, true);
    let decode_key = decode_key.indent();
    let decode_value = decode_value.indent();

    // Use WithOptionalValueType method if encoding is not Slice1 and the value type is optional
    if encoding != Encoding::Slice1 && value_type.is_optional {
        format!(
            "\
decoder.DecodeDictionaryWithOptionalValueType(
    size => new {dictionary_type}(size),
    {decode_key},
    {decode_value})",
        )
    } else {
        format!(
            "\
decoder.DecodeDictionary(
    size => new {dictionary_type}(size),
    {decode_key},
    {decode_value})",
        )
    }
    .into()
}

fn decode_result(result_type_ref: &TypeRef<ResultType>, namespace: &str, encoding: Encoding) -> CodeBlock {
    assert!(encoding != Encoding::Slice1);
    let success_type = &result_type_ref.success_type;
    let failure_type = &result_type_ref.failure_type;

    let decode_success = decode_result_field(success_type, namespace, encoding);
    let decode_failure = decode_result_field(failure_type, namespace, encoding);

    format!(
        "\
decoder.DecodeResult(
    {decode_success},
    {decode_failure})"
    )
    .into()
}

fn decode_sequence(sequence_ref: &TypeRef<Sequence>, namespace: &str, encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();
    let element_type = &sequence_ref.element_type;

    let has_cs_type_attribute = sequence_ref.has_attribute::<CsType>();

    if !has_cs_type_attribute && matches!(element_type.concrete_type(), Types::Sequence(_)) {
        // For nested sequences we want to cast Foo[][] returned by DecodeSequence to IList<Foo>[]
        // used in the request and response decode methods.
        write!(code, "({}[])", element_type.field_type_string(namespace, false));
    };

    if has_cs_type_attribute {
        let sequence_type = sequence_ref.incoming_parameter_type_string(namespace, true);

        let arg: Option<String> = match element_type.concrete_type() {
            Types::Primitive(primitive) if primitive.fixed_wire_size().is_some() && !element_type.is_optional => {
                // We always read an array even when mapped to a collection, as it's expected to be
                // faster than decoding the collection elements one by one.
                Some(format!(
                    "decoder.DecodeSequence<{}>({})",
                    element_type.incoming_parameter_type_string(namespace, true),
                    if matches!(primitive, Primitive::Bool) {
                        "checkElement: SliceDecoder.CheckBoolValue"
                    } else {
                        ""
                    }
                ))
            }
            Types::Enum(enum_def)
                if enum_def.underlying.is_some()
                    && enum_def.fixed_wire_size().is_some()
                    && !element_type.is_optional =>
            {
                // We always read an array even when mapped to a collection, as it's expected to be
                // faster than decoding the collection elements one by one.
                if enum_def.is_unchecked {
                    Some(format!(
                        "decoder.DecodeSequence<{}>()",
                        element_type.incoming_parameter_type_string(namespace, true),
                    ))
                } else {
                    Some(format!(
                        "\
decoder.DecodeSequence(
    ({enum_type_name} e) => _ = {underlying_extensions_class}.As{name}(({underlying_type})e))",
                        enum_type_name = element_type.incoming_parameter_type_string(namespace, false),
                        underlying_extensions_class = enum_def.escape_scoped_identifier_with_suffix(
                            &format!(
                                "{}Extensions",
                                enum_def.get_underlying_cs_type().to_cs_case(Case::Pascal)
                            ),
                            namespace,
                        ),
                        name = enum_def.cs_identifier(Case::Pascal),
                        underlying_type = enum_def.get_underlying_cs_type(),
                    ))
                }
            }
            _ => {
                if encoding != Encoding::Slice1 && element_type.is_optional {
                    write!(
                        code,
                        "\
decoder.DecodeSequenceOfOptionals(
    sequenceFactory: (size) => new {sequence_type}(size),
    {decode_func})",
                        decode_func = decode_func(element_type, namespace, encoding).indent(),
                    );
                } else {
                    write!(
                        code,
                        "\
decoder.DecodeSequence(
    sequenceFactory: (size) => new {sequence_type}(size),
    {decode_func})",
                        decode_func = decode_func(element_type, namespace, encoding).indent(),
                    );
                }
                None
            }
        };

        if let Some(arg) = arg {
            write!(
                code,
                "\
new {sequence_type}(
    {})",
                CodeBlock::from(arg).indent(),
            );
        }
    } else if encoding != Encoding::Slice1 && element_type.is_optional {
        write!(
            code,
            "\
decoder.DecodeSequenceOfOptionals(
    {})",
            decode_func(element_type, namespace, encoding).indent(),
        )
    } else {
        match element_type.concrete_type() {
            Types::Primitive(primitive) if primitive.fixed_wire_size().is_some() => {
                write!(
                    code,
                    "decoder.DecodeSequence<{}>({})",
                    element_type.incoming_parameter_type_string(namespace, true),
                    if matches!(primitive, Primitive::Bool) {
                        "checkElement: SliceDecoder.CheckBoolValue"
                    } else {
                        ""
                    }
                )
            }
            Types::Enum(enum_def) if enum_def.underlying.is_some() && enum_def.fixed_wire_size().is_some() => {
                if enum_def.is_unchecked {
                    write!(
                        code,
                        "decoder.DecodeSequence<{}>()",
                        element_type.incoming_parameter_type_string(namespace, true),
                    )
                } else {
                    write!(
                        code,
                        "\
decoder.DecodeSequence(
    ({enum_type} e) => _ = {underlying_extensions_class}.As{name}(({underlying_type})e))",
                        enum_type = element_type.incoming_parameter_type_string(namespace, false),
                        underlying_extensions_class = enum_def.escape_scoped_identifier_with_suffix(
                            &format!(
                                "{}Extensions",
                                enum_def.get_underlying_cs_type().to_cs_case(Case::Pascal)
                            ),
                            namespace,
                        ),
                        name = enum_def.cs_identifier(Case::Pascal),
                        underlying_type = enum_def.get_underlying_cs_type(),
                    );
                }
            }
            _ => {
                write!(
                    code,
                    "\
decoder.DecodeSequence(
    {})",
                    decode_func(element_type, namespace, encoding).indent(),
                )
            }
        }
    }

    code
}

fn decode_stream_parameter(type_ref: &TypeRef, namespace: &str, encoding: Encoding) -> CodeBlock {
    let decode_func_body = decode_func_body(type_ref, namespace, encoding);
    if type_ref.is_optional {
        CodeBlock::from(format!(
            "(ref SliceDecoder decoder) => decoder.DecodeBool() ? {decode_func_body} : null"
        ))
    } else {
        decode_func(type_ref, namespace, encoding)
    }
}

fn decode_result_field(type_ref: &TypeRef, namespace: &str, encoding: Encoding) -> CodeBlock {
    let mut decode_func = match type_ref.is_optional {
        true => decode_func_body(type_ref, namespace, encoding),
        false => decode_func(type_ref, namespace, encoding),
    };

    // TODO: it's lame to have to do this here. We should provide a better API.
    if matches!(type_ref.concrete_type(), Types::Sequence(_) | Types::Dictionary(_)) {
        write!(decode_func, " as {}", type_ref.field_type_string(namespace, false));
    }

    if type_ref.is_optional {
        decode_func = CodeBlock::from(format!(
            "\
(ref SliceDecoder decoder) => decoder.DecodeBool() ?
    {decode_func}
    : null",
        ));
    }

    decode_func.indent()
}

pub fn decode_func(type_ref: &TypeRef, namespace: &str, encoding: Encoding) -> CodeBlock {
    let decode_func_body = decode_func_body(type_ref, namespace, encoding);
    CodeBlock::from(format!("(ref SliceDecoder decoder) => {decode_func_body}"))
}

fn decode_func_body(type_ref: &TypeRef, namespace: &str, encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();
    let type_name = type_ref.incoming_parameter_type_string(namespace, true);

    // When we decode the type, we decode it as a non-optional.
    // If the type is supposed to be optional, we cast it after decoding.
    if type_ref.is_optional {
        write!(code, "({type_name}?)");
    }

    match &type_ref.concrete_typeref() {
        _ if type_ref.is_class_type() => {
            // is_class_type is either Typeref::Class or Primitive::AnyClass
            assert!(encoding == Encoding::Slice1);
            if type_ref.is_optional {
                write!(code, "decoder.DecodeNullableClass<{type_name}>()")
            } else {
                write!(code, "decoder.DecodeClass<{type_name}>()")
            }
        }
        // Primitive::AnyClass is handled above by is_class_type branch
        TypeRefs::Primitive(primitive_ref) => write!(code, "decoder.Decode{}()", primitive_ref.type_suffix()),
        TypeRefs::Sequence(sequence_ref) => write!(code, "{}", decode_sequence(sequence_ref, namespace, encoding)),
        TypeRefs::Dictionary(dictionary_ref) => {
            write!(code, "{}", decode_dictionary(dictionary_ref, namespace, encoding))
        }
        TypeRefs::Enum(enum_ref) => {
            write!(
                code,
                "{decoder_extensions_class}.Decode{name}(ref decoder)",
                decoder_extensions_class =
                    enum_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                name = enum_ref.cs_identifier(Case::Pascal),
            )
        }
        TypeRefs::ResultType(result_type_ref) => {
            write!(code, "{}", decode_result(result_type_ref, namespace, encoding))
        }
        TypeRefs::Struct(_) => write!(code, "new {type_name}(ref decoder)"),
        TypeRefs::CustomType(custom_type_ref) => {
            write!(
                code,
                "{decoder_extensions_class}.Decode{nullable}{name}(ref decoder)",
                decoder_extensions_class =
                    custom_type_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                name = custom_type_ref.cs_identifier(Case::Pascal),
                nullable = if encoding == Encoding::Slice1 && type_ref.is_optional {
                    "Nullable"
                } else {
                    ""
                },
            )
        }
        TypeRefs::Class(_) => panic!("unexpected, see is_class_type above"),
    }
    code
}

/// Returns a lambda function that takes a `SliceDecoder` and decodes the provided list of parameters from it.
/// This function assumes the parameters are non-streamed, and that at least one such parameter was provided.
pub fn decode_non_streamed_parameters_func(non_streamed_parameters: &[&Parameter], encoding: Encoding) -> CodeBlock {
    // Ensure that the parameters are all non-streamed.
    assert!(non_streamed_parameters.iter().all(|p| !p.is_streamed));

    match non_streamed_parameters {
        [] => panic!("passed empty parameter list to `decode_non_streamed_parameters_func"),

        // If there's only one parameter, it isn't tagged, and doesn't require a bit-sequence to decode,
        // We return a simplified lambda function.
        [param] if !param.is_tagged() && (encoding == Encoding::Slice1 || !param.data_type().is_optional) => {
            decode_func(param.data_type(), &param.namespace(), encoding)
        }

        // Otherwise we return a full multi-line lambda function for decoding the parameters.
        _ => {
            let mut code = CodeBlock::default();

            initialize_bit_sequence_reader_for(non_streamed_parameters, &mut code, encoding);

            for parameter in get_sorted_members(non_streamed_parameters) {
                let param_type = parameter.data_type();
                let namespace = parameter.namespace();

                // For optional value types we have to use the full type as the compiler cannot
                // disambiguate between null and the actual value type.
                let param_type_string = match param_type.is_optional && param_type.is_value_type() {
                    true => param_type.incoming_parameter_type_string(&namespace, false),
                    false => "var".to_owned(),
                };

                let param_name = &parameter.parameter_name_with_prefix();

                let decode = match parameter.is_tagged() {
                    true => decode_tagged(parameter, &namespace, false, encoding),
                    false => decode_member(parameter, &namespace, encoding),
                };

                writeln!(code, "{param_type_string} {param_name} = {decode};")
            }
            writeln!(code, "return {};", non_streamed_parameters.to_argument_tuple());

            let body_content = code.indent();
            format!(
                "\
(ref SliceDecoder decoder) =>
{{
    {body_content}
}}",
            )
            .into()
        }
    }
}

pub fn decode_operation_stream(
    stream_member: &Parameter,
    namespace: &str,
    encoding: Encoding,
    dispatch: bool,
) -> CodeBlock {
    let cs_encoding = encoding.to_cs_encoding();
    let param_type = stream_member.data_type();
    let param_type_str = param_type.incoming_parameter_type_string(namespace, false);
    let fixed_wire_size = param_type.fixed_wire_size();

    match param_type.concrete_type() {
        Types::Primitive(Primitive::UInt8) if !param_type.is_optional => {
            panic!("Must not be called for non-optional UInt8 parameters as there is no decoding");
        }
        _ => FunctionCallBuilder::new(format!("payloadContinuation.ToAsyncEnumerable<{param_type_str}>"))
            .arguments_on_newline(true)
            .add_argument(cs_encoding)
            .add_argument(decode_stream_parameter(param_type, namespace, encoding).indent())
            .add_argument_if_present(fixed_wire_size)
            .add_argument_if(!dispatch && fixed_wire_size.is_none(), "sender")
            .add_argument_if(
                fixed_wire_size.is_none(),
                "sliceFeature: request.Features.Get<ISliceFeature>()",
            )
            .build(),
    }
}
