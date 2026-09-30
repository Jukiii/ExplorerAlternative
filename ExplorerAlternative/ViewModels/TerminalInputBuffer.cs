namespace ExplorerAlternative.ViewModels;

/// <summary>
/// ターミナルの入力行（プロンプトの後ろに打ちかけているコマンド）の編集状態（仕様書17章）。
/// 文字列とカーソル位置だけを持つ純粋なモデルで、画面（View）には依存しない。
///
/// 各操作は、実際に内容またはカーソル位置が変わったかどうかを返す。呼び出し側はそれを見て
/// 画面の更新要否を判断する（何も変わらない操作で再描画しないため）。
/// </summary>
public sealed class TerminalInputBuffer
{
    public string Text { get; private set; } = string.Empty;

    /// <summary>カーソル位置（0〜<see cref="Text"/>の長さ）。文字と文字の間の位置を表す。</summary>
    public int Caret { get; private set; }

    /// <summary>カーソル位置へ文字列を挿入し、カーソルを挿入した文字列の後ろへ進める。</summary>
    public bool Insert(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        Text = Text.Insert(Caret, text);
        Caret += text.Length;
        return true;
    }

    /// <summary>カーソルの直前の1文字を削除する（Backspace）。</summary>
    public bool Backspace()
    {
        if (Caret == 0)
        {
            return false;
        }

        Text = Text.Remove(Caret - 1, 1);
        Caret--;
        return true;
    }

    /// <summary>カーソルの直後の1文字を削除する（Delete）。</summary>
    public bool DeleteForward()
    {
        if (Caret >= Text.Length)
        {
            return false;
        }

        Text = Text.Remove(Caret, 1);
        return true;
    }

    public bool MoveLeft()
    {
        if (Caret == 0)
        {
            return false;
        }

        Caret--;
        return true;
    }

    public bool MoveRight()
    {
        if (Caret >= Text.Length)
        {
            return false;
        }

        Caret++;
        return true;
    }

    public bool MoveToStart()
    {
        if (Caret == 0)
        {
            return false;
        }

        Caret = 0;
        return true;
    }

    public bool MoveToEnd()
    {
        if (Caret == Text.Length)
        {
            return false;
        }

        Caret = Text.Length;
        return true;
    }

    /// <summary>内容を置き換え、カーソルを末尾へ置く（履歴の呼び出し等）。</summary>
    public void Set(string text)
    {
        Text = text ?? string.Empty;
        Caret = Text.Length;
    }

    /// <summary>現在の内容を返し、入力行を空にする（Enterでの確定）。</summary>
    public string Take()
    {
        var text = Text;
        Set(string.Empty);
        return text;
    }
}
