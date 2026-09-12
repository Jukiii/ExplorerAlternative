namespace ExplorerAlternative.Services;

/// <summary>
/// ファイル操作・外部プロセス起動などで発生した失敗を、ユーザー向けの日本語メッセージとして
/// 上位層（ViewModel）へ伝えるための例外。仕様書27章のエラーハンドリング方針に対応する。
/// </summary>
public sealed class AppOperationException : Exception
{
    public AppOperationException(string userMessage, Exception? innerException = null)
        : base(userMessage, innerException)
    {
    }
}
