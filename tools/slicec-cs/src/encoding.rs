// Copyright (c) ZeroC, Inc.

use crate::builders::{Builder, FunctionCallBuilder};
use crate::code_block::CodeBlock;
use crate::code_gen_util::{get_bit_sequence_size, TypeContext};
use crate::cs_attributes::CsType;
use crate::member_util::get_sorted_members;
use crate::slicec_ext::*;
use convert_case::Case;
use slicec::grammar::*;

pub fn encode_fields(fields: &[&Field], encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();

    let bit_sequence_size = get_bit_sequence_size(encoding, fields);
    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequenceWriter = encoder.GetBitSequenceWriter({bit_sequence_size});",
        );
    }

    for field in get_sorted_members(fields) {
        let namespace = field.namespace();
        let param = format!("this.{}", field.field_name());
        let encode_fn = match field.is_tagged() {
            true => encode_tagged_type,
            false => encode_type,
        };
        code.writeln(&encode_fn(field, TypeContext::Field, &namespace, &param, "encoder", encoding));
    }

    code
}

fn encode_type(
    member: &impl Member,
    type_context: TypeContext,
    namespace: &str,
    param: &str,
    encoder_param: &str,
    encoding: Encoding,
) -> CodeBlock {
    let type_ref = member.data_type();

    match &type_ref.concrete_typeref() {
        TypeRefs::CustomType(custom_type_ref) if encoding == Encoding::Slice1 => {
            let identifier = custom_type_ref.cs_identifier(Case::Pascal);
            let nullable = if type_ref.is_optional { "Nullable" } else { "" };
            let encoder_extensions_class =
                custom_type_ref.escape_scoped_identifier_with_suffix("SliceEncoderExtensions", namespace);
            format!("{encoder_extensions_class}.Encode{nullable}{identifier}(ref {encoder_param}, {param});")
        }
        _ if type_ref.is_class_type() => {
            assert!(encoding == Encoding::Slice1);
            if type_ref.is_optional {
                format!("{encoder_param}.EncodeNullableClass({param});")
            } else {
                format!("{encoder_param}.EncodeClass({param});")
            }
        }
        concrete_typeref => {
            let value = if type_ref.is_optional && type_ref.is_value_type() {
                format!("{param}.Value")
            } else if type_ref.is_optional && matches!(type_ref.concrete_type(), Types::CustomType(_)) {
                format!("{param} ?? default!")
            } else {
                param.to_owned()
            };
            let encode_type = match concrete_typeref {
                TypeRefs::Primitive(primitive_ref) => {
                    let type_suffix = primitive_ref.type_suffix();
                    format!("{encoder_param}.Encode{type_suffix}({value});")
                }
                TypeRefs::Struct(_) => format!("{value}.Encode(ref {encoder_param});"),
                TypeRefs::CustomType(custom_type_ref) => {
                    let encoder_extensions_class =
                        custom_type_ref.escape_scoped_identifier_with_suffix("SliceEncoderExtensions", namespace);
                    let identifier = custom_type_ref.cs_identifier(Case::Pascal);
                    format!("{encoder_extensions_class}.Encode{identifier}(ref {encoder_param}, {value});")
                }
                TypeRefs::Sequence(sequence_ref) => format!(
                    "{};",
                    encode_sequence(sequence_ref, namespace, param, type_context, encoder_param, encoding),
                ),
                TypeRefs::Dictionary(dictionary_ref) => {
                    format!(
                        "{};",
                        encode_dictionary(dictionary_ref, namespace, param, encoder_param, encoding),
                    )
                }
                TypeRefs::Enum(enum_ref) => {
                    let encoder_extensions_class =
                        enum_ref.escape_scoped_identifier_with_suffix("SliceEncoderExtensions", namespace);
                    let name = enum_ref.cs_identifier(Case::Pascal);
                    format!("{encoder_extensions_class}.Encode{name}(ref {encoder_param}, {value});")
                }
                TypeRefs::ResultType(result_type_ref) => {
                    format!(
                        "{};",
                        encode_result(result_type_ref, namespace, param, encoder_param, encoding),
                    )
                }
                _ => panic!("class types are handled in the outer match"),
            };

            if type_ref.is_optional {
                assert!(encoding == Encoding::Slice2);
                // A null T[]? or List<T>? is implicitly converted into a default aka null
                // ReadOnlyMemory<T> or ReadOnlySpan<T>. Furthermore, the span of a default
                // ReadOnlyMemory<T> is a default ReadOnlySpan<T>, which is distinct from
                // the span of an empty sequence. This is why the "value.Span != null" below
                // works correctly.
                format!(
                    "\
bitSequenceWriter.Write({param} != null);
if ({param} != null)
{{
    {encode_type}
}}
",
                    param = match concrete_typeref {
                        TypeRefs::Sequence(sequence_ref)
                            if sequence_ref.has_fixed_size_primitive_elements()
                                && !sequence_ref.has_attribute::<CsType>()
                                && type_context == TypeContext::OutgoingParam =>
                            format!("{param}.Span"),
                        _ => param.to_owned(),
                    },
                )
            } else {
                encode_type
            }
        }
    }
    .into()
}

fn encode_tagged_type(
    member: &impl Member,
    type_context: TypeContext,
    namespace: &str,
    param: &str,
    encoder_param: &str,
    encoding: Encoding,
) -> CodeBlock {
    let mut code = CodeBlock::default();
    let data_type = member.data_type();

    assert!(data_type.is_optional);
    assert!(member.is_tagged());

    let tag = member.tag().unwrap();

    let read_only_memory = matches!(
        data_type.concrete_type(),
        Types::Sequence(sequence_def) if sequence_def.has_fixed_size_primitive_elements()
            && type_context == TypeContext::OutgoingParam
            && !data_type.has_attribute::<CsType>()
    );

    let value = if data_type.is_value_type() {
        format!("{param}.Value")
    } else if matches!(data_type.concrete_type(), Types::CustomType(_)) {
        // We don't know if the mapped C# type is a value type or a reference type.
        format!("{param} ?? default!")
    } else {
        param.to_owned()
    };

    // For types with a known size, we provide a size parameter with the size of the tagged
    // param/member:
    let (size_parameter, count_value) = match data_type.concrete_type() {
        Types::Primitive(primitive_def) => match primitive_def {
            Primitive::VarInt32 | Primitive::VarInt62 => {
                (Some(format!("SliceEncoder.GetVarUInt62EncodedSize({value})")), None)
            }
            Primitive::VarUInt32 | Primitive::VarUInt62 => {
                (Some(format!("SliceEncoder.GetVarUInt62EncodedSize({value})")), None)
            }
            _ if encoding == Encoding::Slice1 => (None, None),
            _ => (primitive_def.fixed_wire_size().map(|s| s.to_string()), None),
        },
        Types::Struct(struct_def) => (struct_def.fixed_wire_size().map(|s| s.to_string()), None),
        Types::Enum(enum_def) => (enum_def.fixed_wire_size().map(|s| s.to_string()), None),
        Types::ResultType(result_type_def) => (result_type_def.fixed_wire_size().map(|s| s.to_string()), None),
        Types::Sequence(sequence_def) => {
            if let Some(element_size) = sequence_def.element_type.fixed_wire_size() {
                if element_size == 1 {
                    (None, None)
                } else if read_only_memory {
                    (
                        Some(format!(
                            "{encoder_param}.GetSizeLength({value}.Length) + {element_size} * {value}.Length",
                        )),
                        None,
                    )
                } else {
                    (
                        Some(format!(
                            "{encoder_param}.GetSizeLength(count_) + {element_size} * count_",
                        )),
                        Some(value.clone()),
                    )
                }
            } else {
                (None, None)
            }
        }

        Types::Dictionary(dictionary_def) => {
            if let (Some(key_size), Some(value_size)) = (
                dictionary_def.key_type.fixed_wire_size(),
                dictionary_def.value_type.fixed_wire_size(),
            ) {
                let size = key_size + value_size;
                (
                    Some(format!("{encoder_param}.GetSizeLength(count_) + ({size}) * count_",)),
                    Some(value.clone()),
                )
            } else {
                (None, None)
            }
        }
        _ => (None, None),
    };

    let unwrapped_name = member.parameter_name() + "_";
    let null_check = if read_only_memory {
        format!("{param}.Span != null")
    } else {
        let unwrapped_type = get_type_string(data_type, namespace, type_context, true);
        format!("{param} is {unwrapped_type} {unwrapped_name}")
    };

    let encode_tagged_call = FunctionCallBuilder::new(format!("{encoder_param}.EncodeTagged"))
        .add_argument(tag)
        .add_argument_if_present(
            (encoding == Encoding::Slice1 && data_type.tag_format() != Some(TagFormat::VSize))
                .then(|| format!("TagFormat.{}", data_type.tag_format().unwrap())),
        )
        .add_argument_if_present(size_parameter.map(|size| format!("size: {size}")))
        .add_argument_if(read_only_memory, value)
        .add_argument_if(!read_only_memory, unwrapped_name)
        .add_argument(encode_action(data_type, type_context, namespace, encoding, true))
        .build();

    writeln!(
        code,
        "\
if ({null_check})
{{
    {encode_tagged}
}}",
        encode_tagged = {
            let mut code = CodeBlock::default();
            if let Some(count) = count_value {
                code.writeln(&format!("int count_ = {count}.Count();"));
            }
            code.writeln(&encode_tagged_call);
            code
        },
    );

    code
}

fn encode_sequence(
    sequence_ref: &TypeRef<Sequence>,
    namespace: &str,
    value: &str,
    type_context: TypeContext,
    encoder_param: &str,
    encoding: Encoding,
) -> CodeBlock {
    if sequence_ref.has_fixed_size_primitive_elements() && !sequence_ref.has_attribute::<CsType>() {
        if type_context == TypeContext::OutgoingParam {
            format!("{encoder_param}.EncodeSpan({value}.Span)")
        } else {
            format!("{encoder_param}.EncodeSequence({value})")
        }
    } else {
        let element_type = &sequence_ref.element_type;
        format!(
            "\
{encoder_param}.EncodeSequence{with_bit_sequence}(
    {value},
    {encode_action})",
            with_bit_sequence = if encoding != Encoding::Slice1 && element_type.is_optional {
                "OfOptionals"
            } else {
                ""
            },
            encode_action = encode_action(element_type, TypeContext::Field, namespace, encoding, false).indent(),
        )
    }
    .into()
}

fn encode_dictionary(
    dictionary_def: &Dictionary,
    namespace: &str,
    param: &str,
    encoder_param: &str,
    encoding: Encoding,
) -> CodeBlock {
    let key_type = &dictionary_def.key_type;
    let value_type = &dictionary_def.value_type;
    format!(
        "\
{encoder_param}.{method}(
    {param},
    {encode_key},
    {encode_value})",
        method = if encoding != Encoding::Slice1 && value_type.is_optional {
            "EncodeDictionaryWithOptionalValueType"
        } else {
            "EncodeDictionary"
        },
        encode_key = encode_action(key_type, TypeContext::Field, namespace, encoding, false).indent(),
        encode_value = encode_action(value_type, TypeContext::Field, namespace, encoding, false).indent(),
    )
    .into()
}

fn encode_action(
    type_ref: &TypeRef,
    type_context: TypeContext,
    namespace: &str,
    encoding: Encoding,
    is_tagged: bool,
) -> CodeBlock {
    CodeBlock::from(format!(
        "(ref SliceEncoder encoder, {value_type} value) => {encode_action_body}",
        value_type = get_type_string(type_ref, namespace, type_context, is_tagged),
        encode_action_body = encode_action_body(type_ref, type_context, namespace, encoding, is_tagged),
    ))
}

fn encode_action_body(
    type_ref: &TypeRef,
    type_context: TypeContext,
    namespace: &str,
    encoding: Encoding,
    is_tagged: bool,
) -> CodeBlock {
    let value = match (
        type_ref.is_optional && !is_tagged,
        type_ref.is_value_type(),
        matches!(type_ref.concrete_type(), Types::CustomType(_)),
    ) {
        (true, false, false) => "value!",
        (true, true, false) => "value!.Value",
        (true, false, true) => "(value ?? default!)",
        _ => "value",
    };

    match &type_ref.concrete_typeref() {
        TypeRefs::Class(_) => {
            assert!(encoding == Encoding::Slice1);
            if type_ref.is_optional {
                "encoder.EncodeNullableClass(value)".into()
            } else {
                "encoder.EncodeClass(value)".into()
            }
        }
        TypeRefs::Primitive(primitive_ref) => format!("encoder.Encode{}({value})", primitive_ref.type_suffix()).into(),
        TypeRefs::Enum(enum_ref) => {
            let encoder_extensions_class =
                enum_ref.escape_scoped_identifier_with_suffix("SliceEncoderExtensions", namespace);
            let name = enum_ref.cs_identifier(Case::Pascal);
            format!("{encoder_extensions_class}.Encode{name}(ref encoder, {value})").into()
        }
        TypeRefs::ResultType(result_type_ref) => {
            encode_result(result_type_ref, namespace, "value", "encoder", encoding)
        }
        TypeRefs::Dictionary(dictionary_ref) => {
            encode_dictionary(dictionary_ref, namespace, value, "encoder", encoding)
        }
        TypeRefs::Sequence(sequence_ref) => {
            // We generate the sequence encoder inline, so this function must not be called when
            // the top-level object is not cached.
            encode_sequence(sequence_ref, namespace, value, type_context, "encoder", encoding)
        }
        TypeRefs::Struct(_) => format!("{value}.Encode(ref encoder)").into(),
        TypeRefs::CustomType(custom_type_ref) => {
            let encoder_extensions_class =
                custom_type_ref.escape_scoped_identifier_with_suffix("SliceEncoderExtensions", namespace);
            let identifier = custom_type_ref.cs_identifier(Case::Pascal);

            // We use the 'Nullable' encoding function here, even for tags, to be consistent with the decoding side.
            // And the decoding side uses it to ensure interop with Ice, because an Ice client could send a tagged proxy
            // that is 'set' to 'null', so we must use the 'Nullable' version.
            if type_ref.is_optional && encoding == Encoding::Slice1 {
                format!("{encoder_extensions_class}.EncodeNullable{identifier}(ref encoder, value)").into()
            } else {
                format!("{encoder_extensions_class}.Encode{identifier}(ref encoder, {value})").into()
            }
        }
    }
}

fn encode_result(
    result_type_def: &ResultType,
    namespace: &str,
    param: &str,
    encoder_param: &str,
    encoding: Encoding,
) -> CodeBlock {
    let success_type = &result_type_def.success_type;
    let failure_type = &result_type_def.failure_type;
    format!(
        "\
{encoder_param}.EncodeResult(
    {param},
    {encode_success},
    {encode_failure})",
        encode_success = encode_result_field(success_type, namespace, encoding).indent(),
        encode_failure = encode_result_field(failure_type, namespace, encoding).indent(),
    )
    .into()
}

pub fn encode_stream_parameter(type_ref: &TypeRef, namespace: &str, encoding: Encoding) -> CodeBlock {
    encode_type_with_bit_sequence_optimization(type_ref, TypeContext::OutgoingParam, namespace, encoding)
}

fn encode_result_field(type_ref: &TypeRef, namespace: &str, encoding: Encoding) -> CodeBlock {
    encode_type_with_bit_sequence_optimization(type_ref, TypeContext::Field, namespace, encoding)
}

/// This function returns a `encode_action` lambda function. This includes the code for handling optional types.
/// Instead of using a whole bit-sequence, it encodes the optionality on a single bool however.
fn encode_type_with_bit_sequence_optimization(
    type_ref: &TypeRef,
    type_context: TypeContext,
    namespace: &str,
    encoding: Encoding,
) -> CodeBlock {
    let value_type = get_type_string(type_ref, namespace, type_context, false);
    if type_ref.is_optional {
        CodeBlock::from(format!(
            "\
(ref SliceEncoder encoder, {value_type} value) =>
{{
    encoder.EncodeBool(value is not null);
    if (value is not null)
    {{
        {encode_action_body};
    }}
}}",
            encode_action_body = encode_action_body(type_ref, type_context, namespace, encoding, false).indent()
        ))
    } else {
        encode_action(type_ref, type_context, namespace, encoding, false)
    }
}

fn encode_operation_parameters(operation: &Operation, return_type: bool, encoder_param: &str) -> CodeBlock {
    let mut code = CodeBlock::default();
    let namespace = &operation.namespace();

    let parameters = if return_type {
        operation.non_streamed_return_members()
    } else {
        operation.non_streamed_parameters()
    };

    let bit_sequence_size = get_bit_sequence_size(operation.encoding, &parameters);
    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequenceWriter = encoder_.GetBitSequenceWriter({bit_sequence_size});",
        );
    }

    let single_return_type = return_type && parameters.len() == 1;

    for parameter in get_sorted_members(&parameters) {
        let parameter_name = if single_return_type {
            "returnValue".to_owned()
        } else {
            parameter.parameter_name()
        };

        let encode_fn = match parameter.is_tagged() {
            true => encode_tagged_type,
            false => encode_type,
        };
        code.writeln(&encode_fn(parameter, TypeContext::OutgoingParam, namespace, &parameter_name, encoder_param, operation.encoding));
    }

    if operation.encoding != Encoding::Slice1 {
        writeln!(code, "{encoder_param}.EncodeVarInt32(Slice2Definitions.TagEndMarker);");
    }

    code
}

pub fn encode_operation(operation: &Operation, is_dispatch: bool) -> CodeBlock {
    format!(
        "\
var pipe_ = new global::System.IO.Pipelines.Pipe(
    encodeOptions?.PipeOptions ?? SliceEncodeOptions.Default.PipeOptions);
var encoder_ = new SliceEncoder(pipe_.Writer, {encoding}, {class_format});

{size_placeholder_and_start_position}

{encode_returns}

{rewrite_size}

pipe_.Writer.Complete();
return pipe_.Reader;",
        size_placeholder_and_start_position = match operation.encoding {
            Encoding::Slice1 => "",
            _ =>
                "\
Span<byte> sizePlaceholder_ = encoder_.GetPlaceholderSpan(4);
int startPos_ = encoder_.EncodedByteCount;",
        },
        rewrite_size = match operation.encoding {
            Encoding::Slice1 => "",
            _ => "SliceEncoder.EncodeVarUInt62((ulong)(encoder_.EncodedByteCount - startPos_), sizePlaceholder_);",
        },
        encoding = operation.encoding.to_cs_encoding(),
        class_format = operation.get_class_format(is_dispatch),
        encode_returns = encode_operation_parameters(operation, is_dispatch, "encoder_"),
    )
    .into()
}

// TODO temporary bridging code while cleaning up the type_string functions.
fn get_type_string(type_ref: &TypeRef, namespace: &str, context: TypeContext, ignore_optional: bool) -> String {
    let type_string = match context {
        TypeContext::OutgoingParam => type_ref.outgoing_parameter_type_string(namespace),
        TypeContext::Field => type_ref.field_type_string(namespace),
        TypeContext::IncomingParam => unreachable!(),
    };

    match ignore_optional {
        true => remove_optional_modifier_from(type_string),
        false => type_string,
    }
}
