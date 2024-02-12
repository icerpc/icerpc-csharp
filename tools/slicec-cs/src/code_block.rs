// Copyright (c) ZeroC, Inc.

use std::fmt;

#[derive(Clone, Debug, Default)]
pub struct CodeBlock {
    pub content: String,
}

impl CodeBlock {
    pub fn write<T: fmt::Display + ?Sized>(&mut self, s: &T) {
        let string = s.to_string();
        if !string.trim().is_empty() {
            self.content.push_str(&string);
        }
    }

    pub fn writeln<T: fmt::Display + ?Sized>(&mut self, s: &T) {
        self.write(&format!("{s}\n"));
    }

    /// Used to write code blocks using the write! and writeln! macros
    /// without results. Note that the write_fmt defined in fmt::Write and io::Write
    /// have a Result<()> return type.
    pub fn write_fmt(&mut self, args: fmt::Arguments<'_>) {
        if let Some(s) = args.as_str() {
            self.write(s);
        } else {
            self.write(&args.to_string());
        }
    }

    pub fn indent(mut self) -> Self {
        self.content = self.content.replace('\n', "\n    ");
        self
    }

    pub fn add_block(&mut self, block: impl Into<CodeBlock>) {
        let block: CodeBlock = block.into();
        self.write(&format!("\n{block}\n"));
    }

    pub fn is_empty(&self) -> bool {
        self.content.trim().is_empty()
    }
}

/// Formats a CodeBlock for display. Whitespace characters are removed from the beginning, the end,
/// and from lines that only contain whitespace.
impl fmt::Display for CodeBlock {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        let mut last_line_was_empty = false;
        write!(
            f,
            "{}",
            self.content
                .lines()
                .map(
                    // Trim whitespace only lines and remove trailing whitespace from non-empty
                    // lines
                    |line| match line.trim_matches(char::is_whitespace).is_empty() {
                        true => "",
                        _ => line.trim_end_matches(char::is_whitespace),
                    },
                )
                .filter(|line| {
                    // Remove empty lines if the previous line was empty
                    let is_empty = line.is_empty();
                    match last_line_was_empty && is_empty {
                        true => false,
                        _ => {
                            last_line_was_empty = is_empty;
                            true
                        }
                    }
                })
                .collect::<Vec<_>>()
                .join("\n")
                .trim_matches(char::is_whitespace),
        )
    }
}

/// Converts an iterator of CodeBlocks into a single CodeBlock. Each of the individual CodeBlocks
/// are stringified and spaced with an empty line between them.
impl FromIterator<CodeBlock> for CodeBlock {
    fn from_iter<T: IntoIterator<Item = CodeBlock>>(iter: T) -> Self {
        let mut code = CodeBlock::default();
        for block in iter {
            code.add_block(block);
        }
        code
    }
}

/// Allows for converting a String into a Codeblock.
/// eg. let code_block: CodeBlock = format!("{}", "Hello, World!").into();
impl From<String> for CodeBlock {
    fn from(s: String) -> Self {
        CodeBlock { content: s }
    }
}

/// Allows for converting a &str into a Codeblock.
/// eg. let code_block: CodeBlock = "Hello, World!".into();
impl From<&str> for CodeBlock {
    fn from(s: &str) -> Self {
        CodeBlock { content: s.to_owned() }
    }
}
