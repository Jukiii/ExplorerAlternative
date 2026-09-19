using System.Text.RegularExpressions;
using ExplorerAlternative.Models;

namespace ExplorerAlternative.Rendering;

/// <summary>
/// 仕様書16章「コードシンボル表示」：完全なIDEの構文解析ではなく、正規表現ベースの簡易抽出で
/// クラス・関数・メソッド等の一覧を作る。ネストの深さはC系言語では中括弧の深さ、Pythonは
/// インデント幅から推定する簡易的なものであり、厳密な構文解析ではない。
/// </summary>
public static class CodeSymbolExtractor
{
    private static readonly string[] CBraceExtensions =
    {
        "cs", "java", "c", "h", "cpp", "hpp", "cc", "cxx", "go", "js", "jsx", "ts", "tsx"
    };

    private static readonly Regex CSharpJavaContainerPattern = new(
        @"^\s*(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal|static|sealed|abstract|partial|final|\s)*\b(class|interface|struct|enum|record)\s+(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex CSharpJavaMethodPattern = new(
        @"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|virtual|override|async|sealed|abstract|readonly|new|final)\s+)+[\w<>\[\],\.\?]+\s+(\w+)\s*\([^;{]*\)\s*(?:where\s+.+)?\{?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex GoFuncPattern = new(
        @"^\s*func\s+(?:\([^)]*\)\s*)?(\w+)\s*\(", RegexOptions.Compiled);

    private static readonly Regex GoTypePattern = new(
        @"^\s*type\s+(\w+)\s+(struct|interface)\b", RegexOptions.Compiled);

    private static readonly Regex JsClassPattern = new(@"^\s*(?:export\s+)?(?:default\s+)?class\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex JsFunctionPattern = new(@"^\s*(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s*\*?\s+(\w+)\s*\(", RegexOptions.Compiled);
    private static readonly Regex JsArrowConstPattern = new(@"^\s*(?:export\s+)?const\s+(\w+)\s*=\s*(?:async\s*)?\([^)]*\)\s*(?::\s*[\w<>\[\],\s]+)?\s*=>", RegexOptions.Compiled);
    private static readonly Regex TsInterfacePattern = new(@"^\s*(?:export\s+)?interface\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex JsMethodPattern = new(@"^\s*(?:public|private|protected|static|async|readonly|\*)*\s*(\w+)\s*\([^;{()]*\)\s*\{?\s*$", RegexOptions.Compiled);

    private static readonly Regex PythonClassPattern = new(@"^(\s*)class\s+(\w+)", RegexOptions.Compiled);
    private static readonly Regex PythonDefPattern = new(@"^(\s*)def\s+(\w+)", RegexOptions.Compiled);

    private static readonly Regex CFunctionPattern = new(
        @"^[\w][\w\s\*&:<>,]*?\b(\w+)\s*\([^;{}]*\)\s*\{?\s*$", RegexOptions.Compiled);
    private static readonly Regex CStructPattern = new(@"^\s*(?:typedef\s+)?(struct|class|enum|union)\s+(\w+)", RegexOptions.Compiled);

    private static readonly Regex CssSelectorPattern = new(@"^([^{}\r\n]+)\{\s*$", RegexOptions.Compiled);

    private static readonly Regex HtmlHeadingPattern = new(@"<h([1-6])[^>]*>(.*?)</h\1>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlIdPattern = new(@"\bid\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled);

    private static readonly string[] ControlFlowKeywords =
    {
        "if", "for", "while", "switch", "catch", "using", "return", "new", "foreach", "else",
        "try", "lock", "fixed", "do", "case"
    };

    public static IReadOnlyList<CodeSymbol> Extract(string content, string extension)
    {
        extension = extension.TrimStart('.').ToLowerInvariant();
        var lines = content.Replace("\r\n", "\n").Split('\n');

        return extension switch
        {
            "py" => ExtractPython(lines),
            "go" => ExtractGo(lines),
            "js" or "jsx" or "mjs" or "cjs" => ExtractJavaScript(lines),
            "ts" or "tsx" => ExtractTypeScript(lines),
            "cs" or "java" => ExtractCSharpJava(lines),
            "c" or "h" or "cpp" or "hpp" or "cc" or "cxx" => ExtractCFamily(lines),
            "html" or "htm" => ExtractHtml(lines),
            "css" or "scss" or "less" => ExtractCss(lines),
            _ => Array.Empty<CodeSymbol>()
        };
    }

    public static bool IsSupported(string extension) =>
        CBraceExtensions.Contains(extension.TrimStart('.').ToLowerInvariant()) ||
        extension.TrimStart('.').ToLowerInvariant() is "py" or "html" or "htm" or "css" or "scss" or "less";

    private static List<CodeSymbol> ExtractCSharpJava(string[] lines)
    {
        var symbols = new List<CodeSymbol>();
        var depth = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            var containerMatch = CSharpJavaContainerPattern.Match(line);
            if (containerMatch.Success)
            {
                symbols.Add(new CodeSymbol { Name = containerMatch.Groups[2].Value, Kind = containerMatch.Groups[1].Value, Line = i + 1, Depth = depth });
            }
            else if (!IsControlFlowLine(trimmed))
            {
                var methodMatch = CSharpJavaMethodPattern.Match(line);
                if (methodMatch.Success)
                {
                    symbols.Add(new CodeSymbol { Name = methodMatch.Groups[1].Value, Kind = "method", Line = i + 1, Depth = depth });
                }
            }

            depth += CountNetBraces(line);
        }

        return symbols;
    }

    private static List<CodeSymbol> ExtractCFamily(string[] lines)
    {
        var symbols = new List<CodeSymbol>();
        var depth = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            var structMatch = CStructPattern.Match(line);
            if (structMatch.Success)
            {
                symbols.Add(new CodeSymbol { Name = structMatch.Groups[2].Value, Kind = structMatch.Groups[1].Value, Line = i + 1, Depth = depth });
            }
            else if (!IsControlFlowLine(trimmed) && !trimmed.StartsWith("#") && depth == 0)
            {
                var funcMatch = CFunctionPattern.Match(trimmed);
                if (funcMatch.Success)
                {
                    symbols.Add(new CodeSymbol { Name = funcMatch.Groups[1].Value, Kind = "function", Line = i + 1, Depth = depth });
                }
            }

            depth += CountNetBraces(line);
        }

        return symbols;
    }

    private static List<CodeSymbol> ExtractGo(string[] lines)
    {
        var symbols = new List<CodeSymbol>();
        var depth = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            var typeMatch = GoTypePattern.Match(line);
            if (typeMatch.Success)
            {
                symbols.Add(new CodeSymbol { Name = typeMatch.Groups[1].Value, Kind = typeMatch.Groups[2].Value, Line = i + 1, Depth = depth });
            }
            else
            {
                var funcMatch = GoFuncPattern.Match(line);
                if (funcMatch.Success)
                {
                    symbols.Add(new CodeSymbol { Name = funcMatch.Groups[1].Value, Kind = "function", Line = i + 1, Depth = depth });
                }
            }

            depth += CountNetBraces(line);
        }

        return symbols;
    }

    private static List<CodeSymbol> ExtractJavaScript(string[] lines) => ExtractJsLike(lines, includeInterfaces: false);

    private static List<CodeSymbol> ExtractTypeScript(string[] lines) => ExtractJsLike(lines, includeInterfaces: true);

    private static List<CodeSymbol> ExtractJsLike(string[] lines, bool includeInterfaces)
    {
        var symbols = new List<CodeSymbol>();
        var depth = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            var classMatch = JsClassPattern.Match(line);
            if (classMatch.Success)
            {
                symbols.Add(new CodeSymbol { Name = classMatch.Groups[1].Value, Kind = "class", Line = i + 1, Depth = depth });
            }
            else if (includeInterfaces && TsInterfacePattern.Match(line) is { Success: true } interfaceMatch)
            {
                symbols.Add(new CodeSymbol { Name = interfaceMatch.Groups[1].Value, Kind = "interface", Line = i + 1, Depth = depth });
            }
            else if (JsFunctionPattern.Match(line) is { Success: true } funcMatch)
            {
                symbols.Add(new CodeSymbol { Name = funcMatch.Groups[1].Value, Kind = "function", Line = i + 1, Depth = depth });
            }
            else if (JsArrowConstPattern.Match(line) is { Success: true } arrowMatch)
            {
                symbols.Add(new CodeSymbol { Name = arrowMatch.Groups[1].Value, Kind = "function", Line = i + 1, Depth = depth });
            }
            else if (depth > 0 && !IsControlFlowLine(trimmed) && JsMethodPattern.Match(line) is { Success: true } methodMatch)
            {
                symbols.Add(new CodeSymbol { Name = methodMatch.Groups[1].Value, Kind = "method", Line = i + 1, Depth = depth });
            }

            depth += CountNetBraces(line);
        }

        return symbols;
    }

    private static List<CodeSymbol> ExtractPython(string[] lines)
    {
        var symbols = new List<CodeSymbol>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            var classMatch = PythonClassPattern.Match(line);
            if (classMatch.Success)
            {
                symbols.Add(new CodeSymbol { Name = classMatch.Groups[2].Value, Kind = "class", Line = i + 1, Depth = IndentDepth(classMatch.Groups[1].Value) });
                continue;
            }

            var defMatch = PythonDefPattern.Match(line);
            if (defMatch.Success)
            {
                symbols.Add(new CodeSymbol { Name = defMatch.Groups[2].Value, Kind = "function", Line = i + 1, Depth = IndentDepth(defMatch.Groups[1].Value) });
            }
        }

        return symbols;
    }

    private static int IndentDepth(string indent) => indent.Length / 4;

    private static List<CodeSymbol> ExtractCss(string[] lines)
    {
        var symbols = new List<CodeSymbol>();

        for (var i = 0; i < lines.Length; i++)
        {
            var match = CssSelectorPattern.Match(lines[i].Trim());
            if (match.Success)
            {
                var selector = match.Groups[1].Value.Trim();
                if (selector.Length > 0 && !selector.StartsWith("@"))
                {
                    symbols.Add(new CodeSymbol { Name = selector, Kind = "selector", Line = i + 1, Depth = 0 });
                }
            }
        }

        return symbols;
    }

    private static List<CodeSymbol> ExtractHtml(string[] lines)
    {
        var symbols = new List<CodeSymbol>();

        for (var i = 0; i < lines.Length; i++)
        {
            foreach (Match match in HtmlHeadingPattern.Matches(lines[i]))
            {
                var text = Regex.Replace(match.Groups[2].Value, "<[^>]+>", string.Empty).Trim();
                if (text.Length > 0)
                {
                    symbols.Add(new CodeSymbol { Name = text, Kind = "heading", Line = i + 1, Depth = int.Parse(match.Groups[1].Value) - 1 });
                }
            }

            foreach (Match match in HtmlIdPattern.Matches(lines[i]))
            {
                symbols.Add(new CodeSymbol { Name = $"#{match.Groups[1].Value}", Kind = "id", Line = i + 1, Depth = 0 });
            }
        }

        return symbols;
    }

    private static bool IsControlFlowLine(string trimmedLine)
    {
        foreach (var keyword in ControlFlowKeywords)
        {
            if (trimmedLine.StartsWith(keyword + " ", StringComparison.Ordinal) ||
                trimmedLine.StartsWith(keyword + "(", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountNetBraces(string line)
    {
        var open = line.Count(c => c == '{');
        var close = line.Count(c => c == '}');
        return open - close;
    }
}
