# 🔍 Keyword Usage Extractor for C# Solutions

This tool scans a C# solution and extracts detailed metadata about all classes where a given keyword appears. It's designed to help developers and architects quickly locate, analyze, and document REST API contracts, domain classes, or any other code element of interest.

---

## ✨ Features

- 🔎 **Finds classes containing a given keyword**
- 🧠 Captures full context: namespace, attributes, interfaces, and more
- 📁 Groups results by folder
- 🔗 Optional GitHub source links
- 🗂️ Table of Contents and anchors for navigation
- ✅ Markdown and HTML output formats
- 🎨 HTML includes syntax highlighting and live search
- 📊 LOC and class count summary

---

## 🚀 Usage

```bash
dotnet run -- <path/to/solution.sln> <keyword> [--with-references] [--github=https://github.com/org/repo/blob/main/]
