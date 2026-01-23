// Copyright (c) ZeroC, Inc.

using System.Text;

namespace ZeroC.CodeBuilder;

/// <summary>Represents a block of generated code that can be manipulated and formatted.</summary>
public sealed class CodeBlock
{
    /// <summary>Gets a value indicating whether the code block is empty.</summary>
    public bool IsEmpty => _content.Length == 0;

    /// <summary>Combines multiple code blocks into a single code block, separated by newlines.</summary>
    /// <param name="blocks">The blocks to combine.</param>
    /// <returns>A new <see cref="CodeBlock"/> containing all the combined blocks.</returns>
    public static CodeBlock FromBlocks(IEnumerable<CodeBlock> blocks)
    {
        var result = new CodeBlock();
        foreach (CodeBlock block in blocks)
        {
            result.AddBlock(block);
        }
        return result;
    }

    private readonly StringBuilder _content = new();

    /// <summary>Converts a string to a <see cref="CodeBlock"/>.</summary>
    /// <param name="content">The string content.</param>
    public static implicit operator CodeBlock(string content) => new(content);

    /// <summary>Initializes a new instance of the <see cref="CodeBlock"/> class.</summary>
    public CodeBlock()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CodeBlock"/> class with the specified content.</summary>
    /// <param name="content">The initial content of the code block.</param>
    public CodeBlock(string content) => _content.Append(content);

    /// <summary>Adds another code block to this one, separated by newlines.</summary>
    /// <param name="block">The block to add.</param>
    public void AddBlock(CodeBlock block) => Write($"\n{block}\n");

    /// <summary>Creates a new code block with the content indented by 4 spaces.</summary>
    /// <returns>A new <see cref="CodeBlock"/> with indented content.</returns>
    /// <remarks>Only newlines within the content are indented. The beginning of the string is not indented,
    /// allowing the caller to control the initial indentation level.</remarks>
    public CodeBlock Indent()
    {
        string indented = _content.ToString().Replace("\n", "\n    ");
        return new CodeBlock(indented);
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        bool lastLineWasEmpty = false;
        var lines = _content.ToString()
            .Split('\n')
            .Select(line =>
            {
                // Trim whitespace-only lines and remove trailing whitespace from non-empty lines
                if (string.IsNullOrWhiteSpace(line))
                {
                    return string.Empty;
                }
                return line.TrimEnd();
            })
            .Where(line =>
            {
                // Remove empty lines if the previous line was empty
                bool isEmpty = string.IsNullOrEmpty(line);
                if (lastLineWasEmpty && isEmpty)
                {
                    return false;
                }
                lastLineWasEmpty = isEmpty;
                return true;
            })
            .ToList();

        return string.Join("\n", lines).Trim();
    }

    /// <summary>Writes the specified value to the code block if it is not empty or whitespace.</summary>
    /// <param name="value">The value to write.</param>
    public void Write(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _content.Append(value);
        }
    }

    /// <summary>Writes the specified value followed by a newline to the code block.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteLine(string value) => Write($"{value}\n");
}
