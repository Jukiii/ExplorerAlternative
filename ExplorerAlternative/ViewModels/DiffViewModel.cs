using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using ExplorerAlternative.Models;
using ExplorerAlternative.Mvvm;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.ViewModels;

/// <summary>
/// Diffウィンドウ（仕様書23章・25章）。Git Diffと同じ左右比較のUIを、Git管理外の
/// 任意の2ファイル比較（25章）にも共用する。
/// </summary>
public sealed class DiffViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;
    private int _currentChangeIndex = -1;

    private DiffViewModel(string title, string leftLabel, string rightLabel, IReadOnlyList<DiffRowViewModel> rows, IDialogService dialogService)
    {
        Title = title;
        LeftLabel = leftLabel;
        RightLabel = rightLabel;
        _dialogService = dialogService;

        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        ChangeRowIndexes = Rows
            .Select((row, index) => (row, index))
            .Where(t => t.row.IsChange)
            .Select(t => t.index)
            .ToList();

        NextDiffCommand = new RelayCommand(_ => MoveToChange(1), _ => ChangeRowIndexes.Count > 0);
        PreviousDiffCommand = new RelayCommand(_ => MoveToChange(-1), _ => ChangeRowIndexes.Count > 0);
        SaveDiffCommand = new RelayCommand(_ => SaveDiff());
    }

    public string Title { get; }

    public string LeftLabel { get; }

    public string RightLabel { get; }

    public ObservableCollection<DiffRowViewModel> Rows { get; } = new();

    public IReadOnlyList<int> ChangeRowIndexes { get; }

    public RelayCommand NextDiffCommand { get; }

    public RelayCommand PreviousDiffCommand { get; }

    public RelayCommand SaveDiffCommand { get; }

    /// <summary>次/前の差分（23章）へ移動した際、View側にスクロールしてもらうための通知。</summary>
    public event Action<int>? ScrollToRowRequested;

    public static DiffViewModel Create(
        string title, string leftLabel, string rightLabel,
        string? leftText, string? rightText,
        IDiffService diffService, IDialogService dialogService)
    {
        var lines = diffService.Compare(leftText, rightText);
        var rows = BuildRows(lines);
        return new DiffViewModel(title, leftLabel, rightLabel, rows, dialogService);
    }

    private static List<DiffRowViewModel> BuildRows(IReadOnlyList<DiffLine> lines)
    {
        var rows = new List<DiffRowViewModel>();
        var leftNumber = 1;
        var rightNumber = 1;
        var i = 0;

        while (i < lines.Count)
        {
            if (lines[i].Kind == DiffLineKind.Equal)
            {
                rows.Add(new DiffRowViewModel
                {
                    LeftKind = DiffLineKind.Equal,
                    LeftText = lines[i].Text,
                    LeftNumber = leftNumber++,
                    RightKind = DiffLineKind.Equal,
                    RightText = lines[i].Text,
                    RightNumber = rightNumber++
                });
                i++;
                continue;
            }

            var removed = new List<string>();
            var added = new List<string>();

            while (i < lines.Count && lines[i].Kind != DiffLineKind.Equal)
            {
                if (lines[i].Kind == DiffLineKind.Removed)
                {
                    removed.Add(lines[i].Text);
                }
                else
                {
                    added.Add(lines[i].Text);
                }

                i++;
            }

            var count = Math.Max(removed.Count, added.Count);
            for (var k = 0; k < count; k++)
            {
                var hasLeft = k < removed.Count;
                var hasRight = k < added.Count;

                rows.Add(new DiffRowViewModel
                {
                    LeftKind = hasLeft ? DiffLineKind.Removed : DiffLineKind.Equal,
                    LeftText = hasLeft ? removed[k] : string.Empty,
                    LeftNumber = hasLeft ? leftNumber++ : null,
                    RightKind = hasRight ? DiffLineKind.Added : DiffLineKind.Equal,
                    RightText = hasRight ? added[k] : string.Empty,
                    RightNumber = hasRight ? rightNumber++ : null
                });
            }
        }

        return rows;
    }

    private void MoveToChange(int direction)
    {
        if (ChangeRowIndexes.Count == 0)
        {
            return;
        }

        _currentChangeIndex += direction;

        if (_currentChangeIndex < 0)
        {
            _currentChangeIndex = ChangeRowIndexes.Count - 1;
        }
        else if (_currentChangeIndex >= ChangeRowIndexes.Count)
        {
            _currentChangeIndex = 0;
        }

        ScrollToRowRequested?.Invoke(ChangeRowIndexes[_currentChangeIndex]);
    }

    // 仕様書23章「Diff保存」。厳密なunified diff形式ではなく、左右比較の内容をそのまま
    // 読みやすいテキストとして保存する（保存後にGit等へ再適用することは想定しない）。
    private void SaveDiff()
    {
        var outputPath = _dialogService.ShowSaveFileDialog("Diffの保存", "テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*", "diff.txt");
        if (outputPath is null)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"--- {LeftLabel}");
        builder.AppendLine($"+++ {RightLabel}");

        foreach (var row in Rows)
        {
            if (row.LeftKind == DiffLineKind.Equal && row.RightKind == DiffLineKind.Equal)
            {
                builder.AppendLine($"  {row.LeftText}");
                continue;
            }

            if (row.LeftKind == DiffLineKind.Removed)
            {
                builder.AppendLine($"- {row.LeftText}");
            }

            if (row.RightKind == DiffLineKind.Added)
            {
                builder.AppendLine($"+ {row.RightText}");
            }
        }

        try
        {
            File.WriteAllText(outputPath, builder.ToString(), new UTF8Encoding(false));
            _dialogService.ShowInfo($"Diffを保存しました。\n{outputPath}");
        }
        catch (IOException ex)
        {
            _dialogService.ShowError($"Diffの保存に失敗しました。({ex.Message})");
        }
    }
}
