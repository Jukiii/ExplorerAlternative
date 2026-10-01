namespace ExplorerAlternative.Tests.TestDoubles;

/// <summary>
/// WPFの要素（FlowDocument・パネル・RichTextBoxなど）は、STA（シングルスレッド アパートメント）の
/// スレッドで扱う必要がある。xUnitのテストはSTAではないため、専用のスレッドで実行して、
/// そこで起きた例外は、テストの失敗として呼び出し元へ伝える。
/// </summary>
internal static class StaTest
{
    public static void Run(Action action)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Exception("STAスレッド上のテストが失敗しました。", failure);
        }
    }
}
