using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;

namespace ExplorerAlternative.ViewModels;

/// <summary>タグの追加・編集ダイアログ（仕様書5章：アイコン・色の選択）の入力値。</summary>
public sealed class TagEditorViewModel : ObservableObject
{
    /// <summary>仕様書5章の例に挙げられているアイコンを基本の候補とする。</summary>
    public static IReadOnlyList<string> IconChoices { get; } = new[]
    {
        "🏷", "⭐", "🔵", "🟢", "📦", "🔴", "🟡", "🟣", "❗", "✅"
    };

    public static IReadOnlyList<string> ColorChoices { get; } = new[]
    {
        "#EF4444", "#F97316", "#EAB308", "#22C55E", "#06B6D4", "#3B82F6", "#8B5CF6", "#EC4899", "#6B7280"
    };

    private string _name;
    private string _iconGlyph;
    private string? _colorHex;

    private TagEditorViewModel(string name, string iconGlyph, string? colorHex)
    {
        _name = name;
        _iconGlyph = iconGlyph;
        _colorHex = colorHex;
    }

    public static TagEditorViewModel CreateNew() => new(string.Empty, IconChoices[0], null);

    public static TagEditorViewModel FromDefinition(TagDefinition tag) =>
        new(tag.Name, string.IsNullOrEmpty(tag.IconGlyph) ? IconChoices[0] : tag.IconGlyph, tag.ColorHex);

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string IconGlyph
    {
        get => _iconGlyph;
        set => SetProperty(ref _iconGlyph, value);
    }

    public string? ColorHex
    {
        get => _colorHex;
        set => SetProperty(ref _colorHex, value);
    }

    public TagDefinition ToDefinition() => new()
    {
        Name = Name.Trim(),
        IconGlyph = string.IsNullOrWhiteSpace(IconGlyph) ? IconChoices[0] : IconGlyph,
        ColorHex = ColorHex
    };
}
