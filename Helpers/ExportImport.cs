using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

namespace QuickNotes.Helpers;

/// <summary>
/// Converts FlowDocument XAML to/from Markdown, Plain Text, and HTML.
/// </summary>
public static partial class ExportImport
{
    // ── Export ──────────────────────────────────────────────────────

    /// <summary>Plain text (no formatting).</summary>
    public static string ToPlainText(string xaml)
    {
        if (string.IsNullOrEmpty(xaml)) return "";
        if (!xaml.StartsWith('<')) return xaml; // Not XAML
        try
        {
            var doc = (FlowDocument)XamlReader.Parse(xaml);
            return new TextRange(doc.ContentStart, doc.ContentEnd).Text.TrimEnd('\r', '\n');
        }
        catch
        {
            return xaml;
        }
    }

    /// <summary>Markdown export.</summary>
    public static string ToMarkdown(string xaml)
    {
        if (string.IsNullOrEmpty(xaml)) return "";
        if (!xaml.StartsWith('<')) return xaml;

        try
        {
            var doc = (FlowDocument)XamlReader.Parse(xaml);
            var sb = new StringBuilder();
            foreach (var block in doc.Blocks)
                WriteBlockMarkdown(block, sb);
            var result = sb.ToString().TrimEnd('\n', '\r');
            // Collapse multiple blank lines to one
            return BlankLinesRegex().Replace(result, "\n\n");
        }
        catch
        {
            return xaml;
        }
    }

    /// <summary>HTML export.</summary>
    public static string ToHtml(string xaml, string title = "")
    {
        if (string.IsNullOrEmpty(xaml))
            return "<!DOCTYPE html>\n<html><body></body></html>";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\">");
        if (!string.IsNullOrEmpty(title))
            sb.AppendLine($"<title>{System.Net.WebUtility.HtmlEncode(title)}</title>");
        sb.AppendLine("""<style>body{font-family:Calibri,sans-serif;max-width:800px;margin:20px auto;padding:0 16px;line-height:1.5}ul,ol{padding-left:24px}blockquote{border-left:3px solid #ccc;margin:0;padding:0 12px;color:#555}</style>""");
        sb.AppendLine("</head><body>");

        if (!xaml.StartsWith('<'))
        {
            sb.AppendLine($"<p>{System.Net.WebUtility.HtmlEncode(xaml)}</p>");
        }
        else
        {
            try
            {
                var doc = (FlowDocument)XamlReader.Parse(xaml);
                foreach (var block in doc.Blocks)
                    WriteBlockHtml(block, sb);
            }
            catch
            {
                sb.AppendLine($"<p>{System.Net.WebUtility.HtmlEncode(xaml)}</p>");
            }
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    // ── Import ──────────────────────────────────────────────────────

    /// <summary>Import plain text → FlowDocument XAML.</summary>
    public static string FromPlainText(string text, string title = "")
    {
        var doc = CreateDoc(title);
        var para = new Paragraph();
        para.Inlines.Add(new Run(text));
        doc.Blocks.Add(para);
        return XamlWriter.Save(doc);
    }

    /// <summary>Import Markdown → FlowDocument XAML.</summary>
    public static string FromMarkdown(string md, string title = "")
    {
        var doc = CreateDoc(title);
        var lines = md.Replace("\r\n", "\n").Split('\n');

        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];

            // Blank line → paragraph separator
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // Heading: ## text
            var headingMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (headingMatch.Success)
            {
                var level = headingMatch.Groups[1].Value.Length;
                var text = headingMatch.Groups[2].Value.Trim();
                var para = new Paragraph();
                para.FontSize = level switch
                {
                    1 => 24, 2 => 20, 3 => 17, _ => 15
                };
                para.FontWeight = FontWeights.Bold;
                para.Inlines.Add(ParseInlineMarkdown(text));
                doc.Blocks.Add(para);
                i++;
                continue;
            }

            // Horizontal rule
            if (Regex.IsMatch(line, @"^-{3,}$"))
            {
                doc.Blocks.Add(new Paragraph(new Run(new string('\u2500', 40))));
                i++;
                continue;
            }

            // Checkbox list: - [ ] or - [x]
            var cbMatch = Regex.Match(line, @"^[\-\*]\s+\[([ x])\]\s+(.+)$");
            if (cbMatch.Success)
            {
                var list = new List { MarkerStyle = TextMarkerStyle.None };
                var cbText = cbMatch.Groups[1].Value == "x" ? "\u2713 " : "\u25fb ";
                var li = new ListItem();
                var p = new Paragraph();
                p.Inlines.Add(new Run(cbText + cbMatch.Groups[2].Value));
                li.Blocks.Add(p);
                list.ListItems.Add(li);
                doc.Blocks.Add(list);

                // Collect consecutive checkbox items
                i++;
                while (i < lines.Length)
                {
                    var nextCb = Regex.Match(lines[i], @"^[\-\*]\s+\[([ x])\]\s+(.+)$");
                    if (!nextCb.Success) break;
                    var nextLi = new ListItem();
                    var nextP = new Paragraph();
                    nextP.Inlines.Add(new Run(
                        (nextCb.Groups[1].Value == "x" ? "\u2713 " : "\u25fb ") +
                        nextCb.Groups[2].Value));
                    nextLi.Blocks.Add(nextP);
                    list.ListItems.Add(nextLi);
                    i++;
                }
                continue;
            }

            // Bullet list: - or *
            var bulletMatch = Regex.Match(line, @"^[\-\*]\s+(.+)$");
            if (bulletMatch.Success)
            {
                var list = new List { MarkerStyle = TextMarkerStyle.Disc };
                var li = new ListItem();
                var p = new Paragraph();
                p.Inlines.Add(ParseInlineMarkdown(bulletMatch.Groups[1].Value));
                li.Blocks.Add(p);
                list.ListItems.Add(li);
                doc.Blocks.Add(list);

                i++;
                while (i < lines.Length)
                {
                    var nextBullet = Regex.Match(lines[i], @"^[\-\*]\s+(.+)$");
                    if (!nextBullet.Success) break;
                    var nextLi = new ListItem();
                    var nextP = new Paragraph();
                    nextP.Inlines.Add(ParseInlineMarkdown(nextBullet.Groups[1].Value));
                    nextLi.Blocks.Add(nextP);
                    list.ListItems.Add(nextLi);
                    i++;
                }
                continue;
            }

            // Numbered list: 1. text
            var numMatch = Regex.Match(line, @"^(\d+)\.\s+(.+)$");
            if (numMatch.Success)
            {
                var list = new List { MarkerStyle = TextMarkerStyle.Decimal };
                var li = new ListItem();
                var p = new Paragraph();
                p.Inlines.Add(ParseInlineMarkdown(numMatch.Groups[2].Value));
                li.Blocks.Add(p);
                list.ListItems.Add(li);
                doc.Blocks.Add(list);

                i++;
                while (i < lines.Length)
                {
                    var nextNum = Regex.Match(lines[i], @"^(\d+)\.\s+(.+)$");
                    if (!nextNum.Success) break;
                    var nextLi = new ListItem();
                    var nextP = new Paragraph();
                    nextP.Inlines.Add(ParseInlineMarkdown(nextNum.Groups[2].Value));
                    nextLi.Blocks.Add(nextP);
                    list.ListItems.Add(nextLi);
                    i++;
                }
                continue;
            }

            // Regular paragraph
            {
                var para = new Paragraph();

                // Blockquote
                var qMatch = Regex.Match(line, @"^>\s+(.+)$");
                if (qMatch.Success)
                {
                    para.Inlines.Add(new Run("│ ") { Foreground = Brushes.Gray });
                    para.Inlines.Add(ParseInlineMarkdown(qMatch.Groups[1].Value));
                }
                else
                {
                    para.Inlines.Add(ParseInlineMarkdown(line));
                }

                // Collect continuation lines (until blank line or heading)
                i++;
                while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) &&
                       !lines[i].StartsWith('#') && !Regex.IsMatch(lines[i], @"^[\-\*\d\>]"))
                {
                    para.Inlines.Add(new LineBreak());
                    para.Inlines.Add(ParseInlineMarkdown(lines[i]));
                    i++;
                }
                doc.Blocks.Add(para);
                continue;
            }
        }

        return XamlWriter.Save(doc);
    }

    /// <summary>Import HTML → FlowDocument XAML (basic).</summary>
    public static string FromHtml(string html, string title = "")
    {
        // Strip tags for a basic text import, preserving line breaks
        var text = HtmlToPlainText(html);
        return FromPlainText(text, title);
    }

    // ── Internal: Markdown export ──────────────────────────────────

    private static void WriteBlockMarkdown(Block block, StringBuilder sb)
    {
        switch (block)
        {
            case Paragraph para:
                WriteParagraphMarkdown(para, sb);
                sb.AppendLine();
                sb.AppendLine();
                break;

            case List list:
                int itemIdx = 0;
                foreach (var li in list.ListItems)
                {
                    var prefix = list.MarkerStyle switch
                    {
                        TextMarkerStyle.Disc or TextMarkerStyle.Circle or TextMarkerStyle.Square
                            => "- ",
                        TextMarkerStyle.Decimal => $"{++itemIdx}. ",
                        TextMarkerStyle.None => "", // checkbox
                        _ => "- ",
                    };
                    sb.Append(prefix);
                    foreach (var liBlock in li.Blocks)
                    {
                        if (liBlock is Paragraph liPara)
                            WriteInlinesMarkdown(liPara.Inlines, sb);
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();
                break;

            case Section section:
                foreach (var child in section.Blocks)
                    WriteBlockMarkdown(child, sb);
                break;

            case BlockUIContainer bui:
                // Skip UI elements
                break;
        }
    }

    private static void WriteParagraphMarkdown(Paragraph para, StringBuilder sb)
    {
        // Detect heading by font size (consistent with SetHeading logic)
        bool isHeading = para.FontSize >= 15 && para.FontWeight == FontWeights.Bold;
        int headingLevel = 0;
        if (isHeading)
        {
            headingLevel = para.FontSize switch
            {
                >= 26 => 1,
                >= 20 => 2,
                >= 16 => 3,
                _ => 0
            };
            // Don't mark heading if it's just bold text
        }

        // Check if heading detection makes sense (first run is bold + large)
        bool actuallyHeading = headingLevel > 0;
        if (actuallyHeading)
        {
            sb.Append(new string('#', headingLevel));
            sb.Append(' ');
        }
        else if (para.TextDecorations != null && para.TextDecorations == TextDecorations.Strikethrough)
        {
            // Could be a checklist completion marker (or just strikethrough)
        }

        WriteInlinesMarkdown(para.Inlines, sb);

        // Line break handling
        if (!actuallyHeading)
        {
            // Check if there's only an InlineUIContainer (like an image) → no text wrapper
        }
    }

    private static void WriteInlinesMarkdown(InlineCollection inlines, StringBuilder sb)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    WriteRunMarkdown(run, sb);
                    break;

                case Bold bold:
                    sb.Append("**");
                    WriteInlinesMarkdown(bold.Inlines, sb);
                    sb.Append("**");
                    break;

                case Italic italic:
                    sb.Append("*");
                    WriteInlinesMarkdown(italic.Inlines, sb);
                    sb.Append("*");
                    break;

                case Underline underline:
                    // MD doesn't have underline; just output text
                    WriteInlinesMarkdown(underline.Inlines, sb);
                    break;

                case Hyperlink link:
                    var linkText = GetInlinesText(link.Inlines);
                    var url = link.NavigateUri?.ToString() ?? linkText;
                    sb.Append($"[{linkText}]({url})");
                    break;

                case Span span:
                    bool isStrike = span.TextDecorations != null &&
                        span.TextDecorations == TextDecorations.Strikethrough;
                    if (isStrike) sb.Append("~~");
                    WriteInlinesMarkdown(span.Inlines, sb);
                    if (isStrike) sb.Append("~~");
                    break;

                case LineBreak:
                    sb.AppendLine();
                    break;

                case InlineUIContainer uic:
                    WriteUIContainerMarkdown(uic, sb);
                    break;
            }
        }
    }

    private static void WriteRunMarkdown(Run run, StringBuilder sb)
    {
        var text = run.Text;
        if (string.IsNullOrEmpty(text)) return;

        bool runBold = run.FontWeight >= FontWeights.Bold;
        bool runItalic = run.FontStyle == FontStyles.Italic;
        bool runStrike = run.TextDecorations != null &&
            run.TextDecorations == TextDecorations.Strikethrough;

        // Handle checkbox unicode
        if (text == "\u25fb " || text == "\u25fb")
        {
            sb.Append("- [ ] ");
            return;
        }
        if (text == "\u2713 " || text == "\u2713")
        {
            sb.Append("- [x] ");
            return;
        }

        if (runStrike) sb.Append("~~");
        if (runBold && runItalic) sb.Append("***");
        else if (runBold) sb.Append("**");
        else if (runItalic) sb.Append("*");

        sb.Append(text);

        if (runBold && runItalic) sb.Append("***");
        else if (runBold) sb.Append("**");
        else if (runItalic) sb.Append("*");
        if (runStrike) sb.Append("~~");
    }

    private static void WriteUIContainerMarkdown(InlineUIContainer uic, StringBuilder sb)
    {
        if (uic.Child is Image img && img.Source is System.Windows.Media.Imaging.BitmapImage bmp)
        {
            var src = bmp.UriSource?.ToString() ?? "";
            sb.Append($"![image]({src})");
        }
        else if (uic.Child is CheckBox cb)
        {
            sb.Append(cb.IsChecked == true ? "- [x] " : "- [ ] ");
        }
    }

    // ── Internal: HTML export ──────────────────────────────────────

    private static void WriteBlockHtml(Block block, StringBuilder sb)
    {
        switch (block)
        {
            case Paragraph para:
                bool isHeading = para.FontSize >= 15 && para.FontWeight == FontWeights.Bold;
                int hLevel = isHeading ? para.FontSize switch
                {
                    >= 26 => 1, >= 20 => 2, >= 16 => 3, _ => 0
                } : 0;

                if (hLevel > 0)
                {
                    sb.Append($"<h{hLevel}>");
                    WriteInlinesHtml(para.Inlines, sb);
                    sb.AppendLine($"</h{hLevel}>");
                }
                else
                {
                    sb.Append("<p>");
                    WriteInlinesHtml(para.Inlines, sb);
                    sb.AppendLine("</p>");
                }
                break;

            case List list:
                var tag = list.MarkerStyle == TextMarkerStyle.Decimal ? "ol" : "ul";
                sb.AppendLine($"<{tag}>");
                foreach (var li in list.ListItems)
                {
                    sb.Append("  <li>");
                    foreach (var liBlock in li.Blocks)
                    {
                        if (liBlock is Paragraph liPara)
                            WriteInlinesHtml(liPara.Inlines, sb);
                    }
                    sb.AppendLine("</li>");
                }
                sb.AppendLine($"</{tag}>");
                break;

            case Section section:
                foreach (var child in section.Blocks)
                    WriteBlockHtml(child, sb);
                break;

            case BlockUIContainer bui:
                if (bui.Child is Image img && img.Source is System.Windows.Media.Imaging.BitmapImage bmp)
                {
                    sb.AppendLine($"<img src=\"{System.Net.WebUtility.HtmlEncode(bmp.UriSource?.ToString() ?? "")}\" alt=\"image\" />");
                }
                break;
        }
    }

    private static void WriteInlinesHtml(InlineCollection inlines, StringBuilder sb)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    var text = System.Net.WebUtility.HtmlEncode(run.Text);
                    if (string.IsNullOrEmpty(text)) break;

                    bool rBold = run.FontWeight >= FontWeights.Bold;
                    bool rItalic = run.FontStyle == FontStyles.Italic;
                    bool rUnder = run.TextDecorations != null && run.TextDecorations == TextDecorations.Underline;
                    bool rStrike = run.TextDecorations != null && run.TextDecorations == TextDecorations.Strikethrough;

                    if (rStrike) text = $"<s>{text}</s>";
                    if (rUnder) text = $"<u>{text}</u>";
                    if (rItalic) text = $"<em>{text}</em>";
                    if (rBold) text = $"<strong>{text}</strong>";
                    sb.Append(text);
                    break;

                case Bold bold:
                    sb.Append("<strong>");
                    WriteInlinesHtml(bold.Inlines, sb);
                    sb.Append("</strong>");
                    break;

                case Italic italic:
                    sb.Append("<em>");
                    WriteInlinesHtml(italic.Inlines, sb);
                    sb.Append("</em>");
                    break;

                case Underline underline:
                    sb.Append("<u>");
                    WriteInlinesHtml(underline.Inlines, sb);
                    sb.Append("</u>");
                    break;

                case Hyperlink link:
                    var href = System.Net.WebUtility.HtmlEncode(link.NavigateUri?.ToString() ?? "");
                    sb.Append($"<a href=\"{href}\">");
                    WriteInlinesHtml(link.Inlines, sb);
                    sb.Append("</a>");
                    break;

                case Span span:
                    bool sStrike = span.TextDecorations != null && span.TextDecorations == TextDecorations.Strikethrough;
                    if (sStrike) sb.Append("<s>");
                    WriteInlinesHtml(span.Inlines, sb);
                    if (sStrike) sb.Append("</s>");
                    break;

                case LineBreak:
                    sb.Append("<br>\n");
                    break;

                case InlineUIContainer uic:
                    if (uic.Child is Image img && img.Source is System.Windows.Media.Imaging.BitmapImage bmp)
                    {
                        var src = System.Net.WebUtility.HtmlEncode(bmp.UriSource?.ToString() ?? "");
                        sb.Append($"<img src=\"{src}\" alt=\"image\" />");
                    }
                    break;
            }
        }
    }

    // ── Internal: Markdown import ──────────────────────────────────

    /// <summary>Parse inline Markdown (bold, italic, code, links) into Inline collection.</summary>
    private static Inline ParseInlineMarkdown(string text)
    {
        // Simple recursive parser for common patterns
        var container = new Span();

        // Process ***bold+italic***, **bold**, *italic*, ~~strikethrough~~, `code`, [links](url)
        var remaining = text;
        int pos = 0;

        while (pos < remaining.Length)
        {
            // Priority: `code` first (don't parse markers inside)
            var codeMatch = InlineCodeRegex().Match(remaining, pos);
            if (codeMatch.Success && codeMatch.Index == pos)
            {
                var code = codeMatch.Groups[1].Value;
                container.Inlines.Add(new Run(code));
                pos = codeMatch.Index + codeMatch.Length;
                continue;
            }

            // ***bold+italic***
            var biMatch = BoldItalicRegex().Match(remaining, pos);
            if (biMatch.Success && biMatch.Index == pos)
            {
                var inner = biMatch.Groups[1].Value;
                var span = new Span();
                var innerRun = new Run(inner)
                {
                    FontWeight = FontWeights.Bold,
                    FontStyle = FontStyles.Italic,
                };
                span.Inlines.Add(innerRun);
                container.Inlines.Add(span);
                pos = biMatch.Index + biMatch.Length;
                continue;
            }

            // **bold**
            var boldMatch = BoldRegex().Match(remaining, pos);
            if (boldMatch.Success && boldMatch.Index == pos)
            {
                var inner = boldMatch.Groups[1].Value;
                var run = new Run(inner) { FontWeight = FontWeights.Bold };
                container.Inlines.Add(run);
                pos = boldMatch.Index + boldMatch.Length;
                continue;
            }

            // *italic*
            var italicMatch = ItalicRegex().Match(remaining, pos);
            if (italicMatch.Success && italicMatch.Index == pos)
            {
                var inner = italicMatch.Groups[1].Value;
                var run = new Run(inner) { FontStyle = FontStyles.Italic };
                container.Inlines.Add(run);
                pos = italicMatch.Index + italicMatch.Length;
                continue;
            }

            // ~~strikethrough~~
            var strikeMatch = StrikethroughRegex().Match(remaining, pos);
            if (strikeMatch.Success && strikeMatch.Index == pos)
            {
                var inner = strikeMatch.Groups[1].Value;
                var run = new Run(inner) { TextDecorations = TextDecorations.Strikethrough };
                container.Inlines.Add(run);
                pos = strikeMatch.Index + strikeMatch.Length;
                continue;
            }

            // [link text](url)
            var linkMatch = LinkRegex().Match(remaining, pos);
            if (linkMatch.Success && linkMatch.Index == pos)
            {
                var linkText = linkMatch.Groups[1].Value;
                var url = linkMatch.Groups[2].Value;
                var link = new Hyperlink(new Run(linkText))
                {
                    NavigateUri = new Uri(url, UriKind.RelativeOrAbsolute)
                };
                container.Inlines.Add(link);
                pos = linkMatch.Index + linkMatch.Length;
                continue;
            }

            // Plain character
            var ch = remaining[pos];
            // Check if this char starts a non-matching marker and escape it
            if (ch == '\\' && pos + 1 < remaining.Length)
            {
                container.Inlines.Add(new Run(remaining[pos + 1].ToString()));
                pos += 2;
            }
            else
            {
                // Collect plain text until next potential marker
                int end = pos + 1;
                while (end < remaining.Length && !@"*~[`\".Contains(remaining[end]))
                    end++;
                container.Inlines.Add(new Run(remaining[pos..end]));
                pos = end;
            }
        }

        if (container.Inlines.Count == 0)
            container.Inlines.Add(new Run(text));
        return container.Inlines.Count == 1 ? container.Inlines.FirstInline : container;
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static FlowDocument CreateDoc(string title)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Calibri"),
            FontSize = 13,
        };
        return doc;
    }

    private static string GetInlinesText(InlineCollection inlines)
    {
        var sb = new StringBuilder();
        foreach (var inline in inlines)
        {
            if (inline is Run run)
                sb.Append(run.Text);
            else if (inline is Span span)
                sb.Append(GetInlinesText(span.Inlines));
            else if (inline is Bold bold)
                sb.Append(GetInlinesText(bold.Inlines));
            else if (inline is Italic italic)
                sb.Append(GetInlinesText(italic.Inlines));
            else if (inline is Hyperlink link)
                sb.Append(GetInlinesText(link.Inlines));
        }
        return sb.ToString();
    }

    private static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        // Remove scripts, styles
        var stripped = ScriptStyleRegex().Replace(html, "");
        // Replace <br>, <p>, </p>, <div>, </div>, <tr>, </tr> with newlines
        stripped = BlockTagRegex().Replace(stripped, "\n");
        // Strip remaining tags
        stripped = HtmlTagRegex().Replace(stripped, "");
        // Decode entities
        stripped = System.Net.WebUtility.HtmlDecode(stripped);
        // Collapse whitespace
        stripped = BlankLinesRegex().Replace(stripped, "\n");
        return stripped.Trim();
    }

    // ── Regex ──────────────────────────────────────────────────────

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\*\*\*(.+?)\*\*\*")]
    private static partial Regex BoldItalicRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"~~(.+?)~~")]
    private static partial Regex StrikethroughRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<script[^>]*>.*?</script>|<style[^>]*>.*?</style>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptStyleRegex();

    [GeneratedRegex(@"</?(?:p|div|tr|h[1-6]|li|br|blockquote|hr)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
