using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;

namespace Chrono.Net.AppGen.Editor;

/// <summary>Format Code: Roslyn for C#, indentation for XML-based files (AXAML, csproj, config) and JSON.</summary>
public static class CodeFormatter
{
    private static AdhocWorkspace? _workspace;

    /// <summary>Returns the formatted text, or an error message describing why the file could not be formatted.</summary>
    public static (string? Text, string? Error) Format(string path, string text)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".cs" => (FormatCSharp(text), null),
                ".axaml" or ".xaml" or ".csproj" or ".props" or ".targets" or ".config" or ".xml" or ".slnx" or ".nuspec" => (FormatXml(text), null),
                ".json" => (FormatJson(text), null),
                _ => (null, $"No formatter for {ext} files."),
            };
        }
        catch (Exception ex) when (ex is XmlException or JsonException or InvalidOperationException)
        {
            return (null, $"Could not format: {ex.Message}");
        }
    }

    private static string FormatCSharp(string text)
    {
        var tree = CSharpSyntaxTree.ParseText(text);
        _workspace ??= new AdhocWorkspace();
        var options = _workspace.Options
            .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, false)
            .WithChangedOption(FormattingOptions.IndentationSize, LanguageNames.CSharp, 4)
            .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, "\r\n");
        return Formatter.Format(tree.GetRoot(), _workspace, options).ToFullString();
    }

    private static string FormatXml(string text)
    {
        var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
        // Drop whitespace-only text nodes so the writer can re-indent.
        foreach (var t in doc.DescendantNodes().OfType<XText>().Where(t => string.IsNullOrWhiteSpace(t.Value)).ToList()) t.Remove();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = doc.Declaration is null,
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace,
        };
        using var sw = new Utf8StringWriter();
        using (var w = XmlWriter.Create(sw, settings)) doc.Save(w);
        return sw.ToString() + "\r\n";
    }

    private static string FormatJson(string text)
    {
        var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return (node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "") + "\r\n";
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override System.Text.Encoding Encoding => new System.Text.UTF8Encoding(false);
    }
}
