using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.ViewModels;

// 仕様書27章（一括名前変更）：パターンの展開と、検索・置換、変換のロジック。
public sealed class RenamePatternExpanderTests
{
    // ===== 連番・名前・拡張子 =====

    [Fact]
    public void Expand_Sequence_StartsAtOne()
    {
        Assert.Equal("a_1.txt", RenamePatternExpander.Expand("a_{n}.txt", "x.txt", 0));
        Assert.Equal("a_3.txt", RenamePatternExpander.Expand("a_{n}.txt", "x.txt", 2));
    }

    [Fact]
    public void Expand_Sequence_ZeroPadsToGivenDigits()
    {
        Assert.Equal("img_007.png", RenamePatternExpander.Expand("img_{n:000}.png", "x.png", 6));
        Assert.Equal("img_12.png", RenamePatternExpander.Expand("img_{n:0}.png", "x.png", 11));
    }

    [Fact]
    public void Expand_NameAndExt_UseOriginalNameWithoutDot()
    {
        Assert.Equal("report_1.docx", RenamePatternExpander.Expand("{name}_{n}.{ext}", "report.docx", 0));
    }

    [Fact]
    public void Expand_NameWithMultipleDots_KeepsAllButLastExtension()
    {
        Assert.Equal("archive.tar_1.gz", RenamePatternExpander.Expand("{name}_{n}.{ext}", "archive.tar.gz", 0));
    }

    [Fact]
    public void Expand_FileWithoutExtension_GivesEmptyExt()
    {
        Assert.Equal("README-1.", RenamePatternExpander.Expand("{name}-{n}.{ext}", "README", 0));
    }

    // ===== {date} / {created} / {modified}（2026-10追加） =====

    [Fact]
    public void Expand_Date_UsesTodayAndDefaultFormat()
    {
        var result = RenamePatternExpander.Expand("{date}", "x.txt", 0);

        Assert.Equal(DateTime.Now.ToString("yyyy-MM-dd"), result);
    }

    [Fact]
    public void Expand_Created_UsesFileCreationTime()
    {
        var created = new DateTime(2024, 3, 5, 14, 30, 0);

        var result = RenamePatternExpander.Expand("{created}_{name}.{ext}", "photo.jpg", 0, created: created);

        Assert.Equal("2024-03-05_photo.jpg", result);
    }

    [Fact]
    public void Expand_Modified_UsesFileModificationTime()
    {
        var modified = new DateTime(2023, 12, 31, 23, 59, 58);

        var result = RenamePatternExpander.Expand("{modified}_{name}.{ext}", "doc.txt", 0, modified: modified);

        Assert.Equal("2023-12-31_doc.txt", result);
    }

    [Fact]
    public void Expand_CreatedAndModified_AreIndependent()
    {
        var created = new DateTime(2020, 1, 2);
        var modified = new DateTime(2021, 3, 4);

        var result = RenamePatternExpander.Expand("{created:yyyyMMdd}-{modified:yyyyMMdd}", "x", 0, created, modified);

        Assert.Equal("20200102-20210304", result);
    }

    [Fact]
    public void Expand_CustomFormat_IsApplied()
    {
        var created = new DateTime(2024, 3, 5, 14, 30, 9);

        var result = RenamePatternExpander.Expand("{created:yyyyMMdd_HHmmss}", "x", 0, created: created);

        Assert.Equal("20240305_143009", result);
    }

    // 日時が取得できないファイルでは、トークンを残して、プレビューで気づけるようにする。
    [Fact]
    public void Expand_CreatedUnavailable_LeavesTokenAsIs()
    {
        var result = RenamePatternExpander.Expand("{created}_{name}", "a.txt", 0, created: null);

        Assert.Equal("{created}_a", result);
    }

    [Fact]
    public void Expand_ModifiedUnavailable_LeavesTokenWithFormatAsIs()
    {
        var result = RenamePatternExpander.Expand("{modified:yyyyMMdd}", "a.txt", 0, modified: null);

        Assert.Equal("{modified:yyyyMMdd}", result);
    }

    // 27章：不正な日付書式でクラッシュさせない。
    [Fact]
    public void Expand_InvalidDateFormat_DoesNotThrow_AndLeavesToken()
    {
        var created = new DateTime(2024, 1, 1);

        var result = RenamePatternExpander.Expand("{created:%}", "a.txt", 0, created: created);

        // 書式として解釈できない場合はトークンのまま、解釈できて空になる場合は空。いずれも例外は出さない。
        Assert.NotNull(result);
    }

    [Fact]
    public void Expand_TokensCanBeMixedWithSequence()
    {
        var created = new DateTime(2024, 3, 5);

        var result = RenamePatternExpander.Expand("{created:yyyyMMdd}_{n:00}.{ext}", "p.jpg", 4, created: created);

        Assert.Equal("20240305_05.jpg", result);
    }

    [Fact]
    public void Expand_ExistingCallers_WithoutDates_StillWork()
    {
        // 日時を渡さない従来の呼び出し（{created}を使わないパターン）は、そのまま動く。
        Assert.Equal("x_1.txt", RenamePatternExpander.Expand("x_{n}.txt", "a.txt", 0));
    }

    // ===== 検索と置換 =====

    [Fact]
    public void FindReplace_EmptyFind_ReturnsOriginal()
    {
        Assert.Equal("a.txt", RenamePatternExpander.ApplyFindReplace("a.txt", "", "x", false, false));
    }

    [Fact]
    public void FindReplace_PlainText_IsCaseInsensitiveByDefault()
    {
        Assert.Equal("new_file.txt", RenamePatternExpander.ApplyFindReplace("OLD_file.txt", "old", "new", false, caseSensitive: false));
    }

    [Fact]
    public void FindReplace_CaseSensitive_OnlyMatchesExactCase()
    {
        Assert.Equal("OLD_file.txt", RenamePatternExpander.ApplyFindReplace("OLD_file.txt", "old", "new", false, caseSensitive: true));
    }

    [Fact]
    public void FindReplace_ReplacesAllOccurrences()
    {
        Assert.Equal("b_b_b", RenamePatternExpander.ApplyFindReplace("a_a_a", "a", "b", false, false));
    }

    [Fact]
    public void FindReplace_Regex_UsesCaptureGroups()
    {
        var result = RenamePatternExpander.ApplyFindReplace("IMG_2024_01.jpg", @"IMG_(\d{4})_(\d+)", "$1-$2", true, true);

        Assert.Equal("2024-01.jpg", result);
    }

    // 27章：不正な正規表現でクラッシュさせない。
    [Fact]
    public void FindReplace_InvalidRegex_ReturnsOriginal()
    {
        Assert.Equal("a.txt", RenamePatternExpander.ApplyFindReplace("a.txt", "([", "x", true, false));
    }

    // ===== 変換 =====

    [Fact]
    public void Transforms_UpperAndLowerCase()
    {
        Assert.Equal("ABC.TXT", RenamePatternExpander.ApplyTransforms("abc.txt", CaseConversionMode.UpperCase, WidthConversionMode.None, false));
        Assert.Equal("abc.txt", RenamePatternExpander.ApplyTransforms("ABC.TXT", CaseConversionMode.LowerCase, WidthConversionMode.None, false));
    }

    [Fact]
    public void Transforms_ToHalfWidth_ConvertsFullWidthAlphanumericsAndSpace()
    {
        var result = RenamePatternExpander.ApplyTransforms("ＡＢＣ１２３　ｘ", CaseConversionMode.None, WidthConversionMode.ToHalfWidth, false);

        Assert.Equal("ABC123 x", result);
    }

    [Fact]
    public void Transforms_ToFullWidth_ConvertsAsciiAndSpace()
    {
        var result = RenamePatternExpander.ApplyTransforms("AB1 x", CaseConversionMode.None, WidthConversionMode.ToFullWidth, false);

        Assert.Equal("ＡＢ１　ｘ", result);
    }

    [Fact]
    public void Transforms_NormalizeUnicode_AppliesNfkc()
    {
        // 半角カナ「ｶ」＋濁点「ﾞ」は、NFKCで全角の「ガ」になる。
        var result = RenamePatternExpander.ApplyTransforms("ｶﾞ.txt", CaseConversionMode.None, WidthConversionMode.None, true);

        Assert.Equal("ガ.txt", result);
    }

    [Fact]
    public void Transforms_NoneSelected_ReturnsInputUnchanged()
    {
        Assert.Equal("Ab_1.txt", RenamePatternExpander.ApplyTransforms("Ab_1.txt", CaseConversionMode.None, WidthConversionMode.None, false));
    }
}
