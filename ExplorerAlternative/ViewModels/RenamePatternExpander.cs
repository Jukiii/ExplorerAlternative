using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ExplorerAlternative.ViewModels;

public enum BulkRenameMode
{
    Pattern,
    FindReplace
}

public enum CaseConversionMode
{
    None,
    UpperCase,
    LowerCase
}

public enum WidthConversionMode
{
    None,
    ToHalfWidth,
    ToFullWidth
}

/// <summary>
/// 一括名前変更（仕様書21章）の名前生成ロジック。プレビュー表示と実際のリネーム実行の
/// 両方から呼び出し、結果が食い違わないようにする。
/// </summary>
public static class RenamePatternExpander
{
    // {n} = 連番（1始まり）。{n:00}のように0を並べるとその桁数までゼロ埋めする。
    private static readonly Regex SequenceTokenRegex = new(@"\{n(:(0+))?\}", RegexOptions.Compiled);

    // {date} = 今日の日付（yyyy-MM-dd）。{date:yyyyMMdd}のように書式を指定できる。
    private static readonly Regex DateTokenRegex = new(@"\{date(:([^}]+))?\}", RegexOptions.Compiled);

    /// <summary>
    /// {n} = 連番（1始まり、{n:000}でゼロ埋め）、{name} = 拡張子を除いた元の名前、
    /// {ext} = 拡張子（ドットなし）、{date} = 日付。
    /// </summary>
    public static string Expand(string pattern, string originalName, int index)
    {
        var name = Path.GetFileNameWithoutExtension(originalName);
        var ext = Path.GetExtension(originalName).TrimStart('.');

        var result = SequenceTokenRegex.Replace(pattern, match =>
        {
            var digits = match.Groups[2].Success ? match.Groups[2].Value.Length : 1;
            return (index + 1).ToString(new string('0', digits), CultureInfo.InvariantCulture);
        });

        result = DateTokenRegex.Replace(result, match =>
        {
            var format = match.Groups[2].Success ? match.Groups[2].Value : "yyyy-MM-dd";
            try
            {
                return DateTime.Now.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                // 27章：不正な日付書式でクラッシュさせない。トークンをそのまま残す。
                return match.Value;
            }
        });

        return result
            .Replace("{name}", name)
            .Replace("{ext}", ext);
    }

    /// <summary>検索と置換モード：ファイル名全体（拡張子含む）に対して文字列/正規表現の置換を行う。</summary>
    public static string ApplyFindReplace(string originalName, string findText, string replaceText, bool useRegex, bool caseSensitive)
    {
        if (string.IsNullOrEmpty(findText))
        {
            return originalName;
        }

        if (useRegex)
        {
            try
            {
                var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                return Regex.Replace(originalName, findText, replaceText, options);
            }
            catch (ArgumentException)
            {
                // 27章：不正な正規表現パターンでクラッシュさせない。変更しない。
                return originalName;
            }
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return ReplaceWithComparison(originalName, findText, replaceText, comparison);
    }

    private static string ReplaceWithComparison(string source, string find, string replace, StringComparison comparison)
    {
        if (comparison == StringComparison.Ordinal)
        {
            return source.Replace(find, replace);
        }

        var builder = new StringBuilder();
        var index = 0;
        while (true)
        {
            var found = source.IndexOf(find, index, comparison);
            if (found < 0)
            {
                builder.Append(source, index, source.Length - index);
                break;
            }

            builder.Append(source, index, found - index);
            builder.Append(replace);
            index = found + find.Length;
        }

        return builder.ToString();
    }

    /// <summary>大文字/小文字変換・全角半角変換・Unicode正規化（NFKC）を適用する。</summary>
    public static string ApplyTransforms(string name, CaseConversionMode caseMode, WidthConversionMode widthMode, bool normalizeUnicode)
    {
        var result = name;

        result = widthMode switch
        {
            WidthConversionMode.ToHalfWidth => ToHalfWidth(result),
            WidthConversionMode.ToFullWidth => ToFullWidth(result),
            _ => result
        };

        if (normalizeUnicode)
        {
            result = result.Normalize(NormalizationForm.FormKC);
        }

        result = caseMode switch
        {
            CaseConversionMode.UpperCase => result.ToUpper(CultureInfo.CurrentCulture),
            CaseConversionMode.LowerCase => result.ToLower(CultureInfo.CurrentCulture),
            _ => result
        };

        return result;
    }

    // 全角英数記号（U+FF01-FF5E）と全角スペース（U+3000）を半角へ変換する。
    // 半角/全角カナの相互変換までは対象としない（用途上は英数記号で十分なため）。
    private static string ToHalfWidth(string input)
    {
        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '　')
            {
                chars[i] = ' ';
            }
            else if (chars[i] is >= '！' and <= '～')
            {
                chars[i] = (char)(chars[i] - 0xFEE0);
            }
        }

        return new string(chars);
    }

    private static string ToFullWidth(string input)
    {
        var chars = input.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == ' ')
            {
                chars[i] = '　';
            }
            else if (chars[i] is >= '!' and <= '~')
            {
                chars[i] = (char)(chars[i] + 0xFEE0);
            }
        }

        return new string(chars);
    }
}
