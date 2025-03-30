using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

record KeywordUsageResult(
    string file,
    int line,
    string match,
    string className,
    string containingClass,
    List<string> attributes,
    List<string> interfaces,
    List<string> usings,
    string @namespace,
    List<string>? references,
    string kind
);

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: KeywordUsageExtractor <solution.sln> <keyword> [--with-references] [--github=https://github.com/user/repo/blob/main/]");
            return;
        }

        var solutionPath = Path.GetFullPath(args[0]);
        var solutionRoot = Path.GetDirectoryName(solutionPath)!;
        var keyword = args[1];
        var includeReferences = args.Contains("--with-references");
        var githubArg = args.FirstOrDefault(a => a.StartsWith("--github="));
        var githubBaseUrl = githubArg != null ? githubArg.Replace("--github=", "") : null;

        MSBuildLocator.RegisterDefaults();

        using var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionPath);

        var groupedResults = new Dictionary<string, List<KeywordUsageResult>>();
        var markdown = new StringBuilder();
        var html = new StringBuilder();
        var toc = new StringBuilder();

        var allDocuments = solution.Projects.SelectMany(p => p.Documents).ToList();

        int totalClasses = 0;
        int totalLines = 0;

        foreach (var doc in allDocuments)
        {
            var tree = await doc.GetSyntaxTreeAsync();
            var root = await tree.GetRootAsync();
            var text = await doc.GetTextAsync();

            var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Select(u => u.ToString())
                .ToList();

            var classNodes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var classNode in classNodes)
            {
                var classText = classNode.ToFullString();
                if (classText.Contains(keyword))
                {
                    var location = classNode.GetLocation().GetLineSpan();
                    var lineNumber = location.StartLinePosition.Line;
                    var classLineCount = classText.Split('\n').Length;
                    totalClasses++;
                    totalLines += classLineCount;

                    var namespaceNode = classNode.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
                    var namespaceName = namespaceNode != null ? namespaceNode.Name.ToString() : null;

                    var attributes = classNode.AttributeLists.SelectMany(a => a.Attributes).Select(a => a.ToString()).ToList();

                    var interfaces = classNode.BaseList?.Types
                        .Where(t => t.Type is IdentifierNameSyntax || t.Type is GenericNameSyntax)
                        .Select(t => t.Type.ToString())
                        .ToList() ?? new List<string>();

                    var className = classNode.Identifier.Text;

                    List<string>? references = null;
                    if (includeReferences)
                    {
                        references = new List<string>();
                        foreach (var otherDoc in allDocuments.Where(d => d != doc))
                        {
                            var otherText = await otherDoc.GetTextAsync();
                            if (otherText.ToString().Contains(className))
                            {
                                var refPath = Path.GetFullPath(otherDoc.FilePath).Replace(solutionRoot, "").Replace("\\", "/");
                                references.Add($"/{refPath.TrimStart('/')}");
                            }
                        }
                    }

                    var fullPath = Path.GetFullPath(doc.FilePath);
                    var relativePath = fullPath.Replace(solutionRoot, "").Replace("\\", "/");
                    var displayPath = $"/{relativePath.TrimStart('/')}";
                    var folder = Path.GetDirectoryName(displayPath) ?? "Root";

                    var result = new KeywordUsageResult(
                        displayPath,
                        lineNumber + 1,
                        keyword,
                        className,
                        classText,
                        attributes,
                        interfaces,
                        usings,
                        namespaceName,
                        references,
                        "class_containing_keyword"
                    );

                    if (!groupedResults.ContainsKey(folder))
                        groupedResults[folder] = new List<KeywordUsageResult>();

                    groupedResults[folder].Add(result);
                }
            }
        }

        var outputJson = JsonSerializer.Serialize(groupedResults, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync("keyword_usage_output.json", outputJson);

        // Markdown TOC and Summary
        markdown.AppendLine($"# Keyword Usage Summary for '{keyword}'\n");
        markdown.AppendLine($"**Total Classes:** {totalClasses}  ");
        markdown.AppendLine($"**Total Lines of Code (LOC):** {totalLines}\n");
        toc.AppendLine("## Table of Contents\n");

        html.AppendLine("<html><head><meta charset='UTF-8'><title>Keyword Usage Report</title>");
        html.AppendLine("<link rel='stylesheet' href='https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.7.0/styles/github-dark.min.css'>");
        html.AppendLine("<script src='https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.7.0/highlight.min.js'></script>");
        html.AppendLine("<script>hljs.highlightAll();</script>");
        html.AppendLine("<style>body{font-family:sans-serif;padding:1em;} summary{cursor:pointer;} pre{padding:10px;border-radius:6px;} #searchBox{margin-bottom:1em;padding:0.5em;width:100%;max-width:400px;}</style>");
        html.AppendLine("</head><body>");
        html.AppendLine("<h1>Keyword Usage Summary</h1>");
        html.AppendLine($"<p><strong>Total Classes:</strong> {totalClasses}<br/><strong>Total LOC:</strong> {totalLines}</p>");
        html.AppendLine("<input id='searchBox' type='text' placeholder='Search class names...' oninput='filterEntries()'/>");
        html.AppendLine("<h2>Table of Contents</h2><ul id='tocList'>");

        foreach (var group in groupedResults.OrderBy(g => g.Key))
        {
            var folderId = group.Key.Replace(" ", "-").Replace("/", "-").Replace("\\", "-");
            toc.AppendLine($"- [{group.Key}](#{folderId})");
            html.AppendLine($"<li><a href='#{folderId}'>{group.Key}</a><ul>");

            foreach (var entry in group.Value)
            {
                var classAnchor = $"{entry.className.ToLowerInvariant()}-{entry.line}";
                toc.AppendLine($"  - [{entry.className}](#{classAnchor})");
                html.AppendLine($"<li class='entry'><a href='#{classAnchor}'>{entry.className}</a></li>");
            }

            html.AppendLine("</ul></li>");
        }

        html.AppendLine("</ul>");
        html.AppendLine("<script>function filterEntries() { const q = document.getElementById('searchBox').value.toLowerCase(); document.querySelectorAll('.entry').forEach(e => { e.style.display = e.textContent.toLowerCase().includes(q) ? '' : 'none'; }); }</script>");

        markdown.AppendLine(toc.ToString());

        foreach (var group in groupedResults.OrderBy(g => g.Key))
        {
            var folderId = group.Key.Replace(" ", "-").Replace("/", "-").Replace("\\", "-");
            markdown.AppendLine($"\n## Folder: {group.Key} <a name=\"{folderId}\"></a>\n");
            html.AppendLine($"<h2 id='{folderId}'>Folder: {group.Key}</h2>");

            foreach (var entry in group.Value)
            {
                var classAnchor = $"{entry.className.ToLowerInvariant()}-{entry.line}";
                markdown.AppendLine($"<a name=\"{classAnchor}\"></a>");
                var githubLink = githubBaseUrl != null ? $" [(GitHub)]({githubBaseUrl}{entry.file.TrimStart('/')}#L{entry.line})" : string.Empty;
                markdown.AppendLine($"<details><summary><strong>{entry.className}</strong> in `{entry.file}` (line {entry.line}){githubLink}</summary>\n");
                markdown.AppendLine();
                markdown.AppendLine($"- **Namespace:** `{entry.@namespace}`");
                markdown.AppendLine($"- **Attributes:** `{string.Join(", ", entry.attributes)}`");
                markdown.AppendLine($"- **Interfaces:** `{string.Join(", ", entry.interfaces)}`");
                if (includeReferences && entry.references != null)
                {
                    markdown.AppendLine($"- **References:** `{string.Join(", ", entry.references)}`");
                }
                markdown.AppendLine("</details>\n");

                html.AppendLine($"<a name='{classAnchor}'></a><details><summary><strong>{entry.className}</strong> in <code>{entry.file}</code> (line {entry.line})</summary>");
                html.AppendLine($"<ul><li><strong>Namespace:</strong> {entry.@namespace}</li>");
                html.AppendLine($"<li><strong>Attributes:</strong> {string.Join(", ", entry.attributes)}</li>");
                html.AppendLine($"<li><strong>Interfaces:</strong> {string.Join(", ", entry.interfaces)}</li>");
                if (includeReferences && entry.references != null)
                {
                    html.AppendLine($"<li><strong>References:</strong> {string.Join(", ", entry.references)}</li>");
                }
                html.AppendLine("</ul>");
                html.AppendLine("<pre><code class='language-csharp'>");
                html.AppendLine(System.Net.WebUtility.HtmlEncode(entry.containingClass));
                html.AppendLine("</code></pre></details>");
            }
        }

        html.AppendLine("</body></html>");

        await File.WriteAllTextAsync("keyword_usage_summary.md", markdown.ToString());
        await File.WriteAllTextAsync("keyword_usage_summary.html", html.ToString());

        Console.WriteLine("Extraction complete. Output written to keyword_usage_output.json, keyword_usage_summary.md, and keyword_usage_summary.html");
    }
}
