// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Tools;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;

namespace IceRpc.Tests;

#nullable enable

[Parallelizable(ParallelScope.All)]
public class DiagnosticParserTests
{
    public static IEnumerable<TestCaseData> ParseSource
    {
        get
        {
            yield return new TestCaseData(
                "assets/err01.json",
                new Diagnostic
                {
                    Severity = "error",
                    Message = "redefinition of 'op'",
                    SourceSpan = new SourceSpan
                    {
                        Start = new Location
                        {
                            Row = 19,
                            Column = 5,
                        },
                        End = new Location
                        {
                            Row = 19,
                            Column = 7,
                        },
                        File = "tests\\IceRpc.Tests\\Slice\\EnumTests.slice"
                    },
                    ErrorCode = "E012",
                    Notes = new Note[]
                    {
                        new Note
                        {
                            Message = "'op' was previously defined here",
                            SourceSpan = new SourceSpan
                            {
                                Start = new Location
                                {
                                    Row = 18,
                                    Column = 5,
                                },
                                End = new Location
                                {
                                    Row = 18,
                                    Column = 7,
                                },
                                File = "tests\\IceRpc.Tests\\Slice\\EnumTests.slice"
                            }
                        }
                    }
                });

            yield return new TestCaseData(
                "assets/err02.json",
                new Diagnostic
                {
                    Severity = "error",
                    Message = "redefinition of 'MyFixedLengthEnum'",
                    SourceSpan = new SourceSpan
                    {
                        Start = new Location
                        {
                            Row = 17,
                            Column = 11,
                        },
                        End = new Location
                        {
                            Row = 17,
                            Column = 28,
                        },
                        File = "tests\\IceRpc.Tests\\Slice\\EnumTests.slice"
                    },
                    ErrorCode = "E012",
                    Notes = new Note[]
                    {
                        new Note
                        {
                            Message = "'MyFixedLengthEnum' was previously defined here",
                            SourceSpan = new SourceSpan
                            {
                                Start = new Location
                                {
                                    Row = 11,
                                    Column = 6,
                                },
                                End = new Location
                                {
                                    Row = 11,
                                    Column = 23,
                                },
                                File = "tests\\IceRpc.Tests\\Slice\\EnumTests.slice"
                            }
                        }
                    }
                });
        }
    }
    [Test, TestCaseSource(nameof(ParseSource))]
    public void Parse_json_diagnostics(string file, Diagnostic expected)
    {
        string data = File.ReadAllText(file);

        Diagnostic? diagnostic = DiagnosticParser.Parse(data);

        Assert.That(diagnostic, Is.Not.Null);
        Assert.That(diagnostic.Severity, Is.EqualTo(expected.Severity));
        Assert.That(diagnostic.Message, Is.EqualTo(expected.Message));
        Assert.That(diagnostic.SourceSpan?.Start, Is.EqualTo(expected.SourceSpan?.Start));
        Assert.That(diagnostic.SourceSpan?.End, Is.EqualTo(expected.SourceSpan?.End));
        Assert.That(diagnostic.SourceSpan?.File, Is.EqualTo(expected.SourceSpan?.File));
        Assert.That(diagnostic.ErrorCode, Is.EqualTo(expected.ErrorCode));
        Assert.That(diagnostic.Notes.Length, Is.EqualTo(expected.Notes.Length));

        for (int i = 0; i < expected.Notes.Length; i++)
        {
            Note note = diagnostic.Notes[i];
            Note expectedNote = expected.Notes[i];

            Assert.That(note.Message, Is.EqualTo(expectedNote.Message));
            Assert.That(note.SourceSpan?.Start, Is.EqualTo(expectedNote.SourceSpan?.Start));
            Assert.That(note.SourceSpan?.End, Is.EqualTo(expectedNote.SourceSpan?.End));
            Assert.That(note.SourceSpan?.File, Is.EqualTo(expectedNote.SourceSpan?.File));
        }
    }
}
