// Copyright (c) ZeroC, Inc. All rights reserved.

use std::fmt;

#[derive(Clone, Debug)]
pub struct CodeBlock {
    pub content: String,
}

impl CodeBlock {
    pub fn new() -> CodeBlock {
        CodeBlock {
            content: String::new(),
        }
    }

    pub fn write<T: fmt::Display + ?Sized>(&mut self, s: &T) {
        let string = s.to_string();
        if !string.trim_matches(char::is_whitespace).is_empty() {
            self.content.push_str(&string);
        }
    }

    pub fn writeln<T: fmt::Display + ?Sized>(&mut self, s: &T) {
        self.write(&format!("{}\n", s));
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

    pub fn indent(&mut self) -> &mut Self {
        self.content = self.content.replace("\n", "\n    ");
        self
    }

    pub fn add_block<T: fmt::Display + ?Sized>(&mut self, s: &T) {
        self.write(&format!("\n{}\n", s));
    }

    pub fn is_empty(&self) -> bool {
        self.content.trim_matches(char::is_whitespace).is_empty()
    }

    pub fn into_string(self) -> String {
        // Do not return content here as we want the the format function to be applied first
        self.to_string()
    }
}

/// Formats a CodeBlock for display. Whitespace characters are removed from the beginning, the end,
/// and from lines that only contain whitespaces.
impl fmt::Display for CodeBlock {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(
            f,
            "{}",
            self.content
                .lines()
                .map(
                    |line| match line.trim_matches(char::is_whitespace).is_empty() {
                        true => "",
                        _ => line.trim_end_matches(char::is_whitespace),
                    },
                )
                .collect::<Vec<_>>()
                .join("\n")
                .trim_matches(char::is_whitespace)
        )
    }
}

/// Converts an iterator of std::string::String into a CodeBlock. Each string is separated by a
/// single newline.
impl std::iter::FromIterator<std::string::String> for CodeBlock {
    fn from_iter<T: IntoIterator<Item = std::string::String>>(iter: T) -> Self {
        let mut code = CodeBlock::new();
        for i in iter {
            code.writeln(&i);
        }
        code
    }
}

/// Converts an iterator of CodeBlocks into a single CodeBlock. Each of the individual CodeBlocks is
/// is stringified and spaced with an empty line between them.
impl std::iter::FromIterator<CodeBlock> for CodeBlock {
    fn from_iter<T: IntoIterator<Item = CodeBlock>>(iter: T) -> Self {
        let mut code = CodeBlock::new();
        for i in iter {
            code.add_block(&i);
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
        CodeBlock {
            content: s.to_owned(),
        }
    }
}
