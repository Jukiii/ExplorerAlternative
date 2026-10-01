namespace ExplorerAlternative.Models;

/// <summary>
/// 分割ペイン（仕様書19章）の2つのペインの大きさと、境界（スプリッター）のドラッグによる
/// 比率の計算。画面（WPF）に依存しない純粋な計算で、<c>SplitPanel</c>（Views）と
/// <c>TabViewModel</c>が使う。
/// </summary>
public static class SplitLayout
{
    /// <summary>一方のペインの最小の比率。これ以上は、ドラッグしても小さくできない（ペインが消えないように）。</summary>
    public const double MinRatio = 0.1;

    public const double MaxRatio = 0.9;

    public const double DefaultRatio = 0.5;

    /// <summary>境界（スプリッター）の太さ。つかみやすい幅にしてある。</summary>
    public const double SplitterThickness = 6;

    /// <summary>比率を、有効な範囲（最小〜最大）に収める。数値でない値（NaN）・無限大は既定値に戻す。</summary>
    public static double ClampRatio(double ratio)
    {
        if (double.IsNaN(ratio) || double.IsInfinity(ratio))
        {
            return DefaultRatio;
        }

        return Math.Clamp(ratio, MinRatio, MaxRatio);
    }

    /// <summary>
    /// 全体の長さ<paramref name="total"/>を、最初のペイン・境界・2つ目のペインに分ける。
    /// 最初のペインの長さは、境界を除いた長さの<paramref name="ratio"/>倍。
    /// 全体が境界より小さい場合は、境界を詰めて、ペインの長さを0にする。
    /// </summary>
    public static (double First, double SplitterOffset, double SecondOffset, double Second) Calculate(
        double total,
        double ratio,
        double thickness = SplitterThickness)
    {
        if (double.IsNaN(total) || total <= 0)
        {
            return (0, 0, 0, 0);
        }

        var gap = Math.Min(thickness, total);
        var usable = total - gap;
        var first = usable * ClampRatio(ratio);
        var second = usable - first;

        return (first, first, first + gap, second);
    }

    /// <summary>
    /// ドラッグ中のマウス位置（境界の中心の位置）から、比率を求める。
    /// <paramref name="position"/>は、パネルの端からの距離。
    /// </summary>
    public static double RatioFromPosition(double position, double total, double thickness = SplitterThickness)
    {
        var usable = total - Math.Min(thickness, total);
        if (usable <= 0)
        {
            return DefaultRatio;
        }

        // 境界の中心がマウス位置に来るように、境界の太さの半分を引く。
        return ClampRatio((position - (thickness / 2)) / usable);
    }

    /// <summary>指定の位置が、境界（スプリッター）の上かどうか。</summary>
    public static bool IsOnSplitter(double position, double total, double ratio, double thickness = SplitterThickness)
    {
        var layout = Calculate(total, ratio, thickness);

        return position >= layout.SplitterOffset && position <= layout.SecondOffset;
    }
}
