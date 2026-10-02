using System.Diagnostics;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services;
using ExplorerAlternative.Services.Abstractions;
using ExplorerAlternative.Tests.TestDoubles;
using ExplorerAlternative.ViewModels;

namespace ExplorerAlternative.Tests.Services;

// 仕様書44章「パスフレーズ付き鍵でのSFTP接続」：パスフレーズで保護された秘密鍵を、接続のたびに利用者に入力してもらって開く
// （パスフレーズは保存しない）。実際の秘密鍵（OpenSSHのssh-keygenで作る）で、読み込みの判定を確認する。
public sealed class SftpKeyPassphraseTests : IDisposable
{
    private const string Passphrase = "correct horse battery";

    private static readonly string SshKeygen = Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh-keygen.exe");

    private readonly string _root = Directory.CreateTempSubdirectory("eat_key_").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ssh-keygenで鍵を作る。使えない環境では、null（そのテストは、何もせず終わる）。
    private string? MakeKey(string name, string type, string passphrase, bool pem)
    {
        if (!File.Exists(SshKeygen))
        {
            return null;
        }

        var path = Path.Combine(_root, name);
        var arguments = $"-q -t {type} -N \"{passphrase}\" -f \"{path}\"" + (pem ? " -m PEM" : string.Empty);
        using var process = Process.Start(new ProcessStartInfo(SshKeygen, arguments) { UseShellExecute = false, CreateNoWindow = true })!;
        process.WaitForExit(30_000);
        return process.ExitCode == 0 ? path : null;
    }

    // ===== 秘密鍵の読み込み =====

    [Theory]
    [InlineData("ed25519", false)]
    [InlineData("rsa", true)]
    public void KeyWithoutPassphrase_LoadsWithoutAsking(string type, bool pem)
    {
        var key = MakeKey("plain", type, string.Empty, pem);
        if (key is null) return;

        Assert.NotNull(SftpService.LoadPrivateKey(key, null));
    }

    [Theory]
    [InlineData("ed25519", false)]
    [InlineData("rsa", true)]
    public void ProtectedKey_WithoutAPassphrase_AsksForOne(string type, bool pem)
    {
        var key = MakeKey("locked", type, Passphrase, pem);
        if (key is null) return;

        var ex = Assert.Throws<SftpKeyPassphraseException>(() => SftpService.LoadPrivateKey(key, null));

        Assert.False(ex.WasWrong);
        Assert.Equal("locked", ex.KeyFileName);
        Assert.Contains("パスフレーズで保護", ex.Message);
    }

    [Theory]
    [InlineData("ed25519", false)]
    [InlineData("rsa", true)]
    public void ProtectedKey_WithTheRightPassphrase_Loads(string type, bool pem)
    {
        var key = MakeKey("locked", type, Passphrase, pem);
        if (key is null) return;

        Assert.NotNull(SftpService.LoadPrivateKey(key, Passphrase));
    }

    [Theory]
    [InlineData("ed25519", false)]
    [InlineData("rsa", true)]
    public void ProtectedKey_WithAWrongPassphrase_ReportsItWasWrong(string type, bool pem)
    {
        var key = MakeKey("locked", type, Passphrase, pem);
        if (key is null) return;

        var ex = Assert.Throws<SftpKeyPassphraseException>(() => SftpService.LoadPrivateKey(key, "wrong"));

        Assert.True(ex.WasWrong);
        Assert.Contains("違います", ex.Message);
    }

    [Fact]
    public void FileThatIsNotAKey_IsIgnored_SoPasswordLoginCanBeTried()
    {
        var path = Path.Combine(_root, "notakey");
        File.WriteAllText(path, "これは秘密鍵ではありません");

        Assert.Null(SftpService.LoadPrivateKey(path, null));
        Assert.Null(SftpService.LoadPrivateKey(path, "anything"));
    }

    // ===== 画面側の流れ（入力 → つなぎ直し） =====

    private sealed class Scenario
    {
        public IDialogService Dialog { get; } = StubProxy.Create<IDialogService>();

        public StubProxy DialogControl { get; }

        public List<string?> PassphrasesTried { get; } = new();

        public SftpBrowserViewModel Sut { get; }

        public Scenario(string correctPassphrase, params string?[] answers)
        {
            DialogControl = StubProxy.Of(Dialog);
            var queue = new Queue<string?>(answers);
            DialogControl.On("PromptPassword", _ => queue.Count > 0 ? queue.Dequeue() : null);

            var session = StubProxy.Create<ISftpSession>();
            var sessionControl = StubProxy.Of(session);
            sessionControl.On("get_HomeDirectory", _ => "/home/u");
            sessionControl.On("ListDirectory", _ => (IReadOnlyList<RemoteFileEntry>)Array.Empty<RemoteFileEntry>());

            var service = StubProxy.Create<ISftpService>();
            StubProxy.Of(service).On("Connect", args =>
            {
                var passphrase = (string?)args[2];
                PassphrasesTried.Add(passphrase);

                if (string.IsNullOrEmpty(passphrase))
                {
                    throw new SftpKeyPassphraseException("id_ed25519", wasWrong: false);
                }

                if (passphrase != correctPassphrase)
                {
                    throw new SftpKeyPassphraseException("id_ed25519", wasWrong: true);
                }

                return session;
            });

            Sut = new SftpBrowserViewModel(new SshConnectionProfile { DisplayName = "x", Host = "h", Port = 22 }, service, null, Dialog);
        }

        public string[] Prompts => DialogControl.ArgsOf("PromptPassword").Select(a => (string)a[1]!).ToArray();
    }

    [Fact]
    public void ProtectedKey_AsksOnce_ThenConnectsWithTheAnswer()
    {
        var scenario = new Scenario("secret", "secret");

        Assert.True(scenario.Sut.IsConnected);
        Assert.Equal(new string?[] { null, "secret" }, scenario.PassphrasesTried);
        Assert.Single(scenario.Prompts);
        Assert.Contains("保存はされません", scenario.Prompts[0]);
        Assert.Equal(0, scenario.DialogControl.CountOf("ShowError"));
    }

    [Fact]
    public void WrongPassphrase_AsksAgain_WithTheAttemptCount()
    {
        var scenario = new Scenario("secret", "oops", "secret");

        Assert.True(scenario.Sut.IsConnected);
        Assert.Equal(2, scenario.Prompts.Length);
        Assert.Contains("違います", scenario.Prompts[1]);
        Assert.Contains("2/3回目", scenario.Prompts[1]);
    }

    [Fact]
    public void Cancelling_StopsWithoutAnError()
    {
        var scenario = new Scenario("secret", (string?)null);

        Assert.False(scenario.Sut.IsConnected);
        Assert.Equal("接続をキャンセルしました。", scenario.Sut.StatusMessage);
        Assert.Equal(0, scenario.DialogControl.CountOf("ShowError"));
        Assert.Single(scenario.PassphrasesTried);
    }

    [Fact]
    public void EmptyAnswer_IsTreatedAsCancel()
    {
        var scenario = new Scenario("secret", string.Empty);

        Assert.False(scenario.Sut.IsConnected);
        Assert.Equal(0, scenario.DialogControl.CountOf("ShowError"));
    }

    [Fact]
    public void TooManyWrongAnswers_GiveUp_WithAJapaneseError()
    {
        var scenario = new Scenario("secret", "a", "b", "c", "d");

        Assert.False(scenario.Sut.IsConnected);
        Assert.Equal(SftpBrowserViewModel.MaxPassphraseAttempts, scenario.Prompts.Length);
        var error = (string)Assert.Single(scenario.DialogControl.ArgsOf("ShowError"))[0]!;
        Assert.Contains("超えた", error);
        Assert.Equal("接続に失敗しました。", scenario.Sut.StatusMessage);
    }

    [Fact]
    public void AKeyWithoutAPassphrase_NeverPrompts()
    {
        var session = StubProxy.Create<ISftpSession>();
        var sessionControl = StubProxy.Of(session);
        sessionControl.On("get_HomeDirectory", _ => "/");
        sessionControl.On("ListDirectory", _ => (IReadOnlyList<RemoteFileEntry>)Array.Empty<RemoteFileEntry>());
        var service = StubProxy.Create<ISftpService>();
        StubProxy.Of(service).On("Connect", _ => session);
        var dialog = StubProxy.Create<IDialogService>();

        var sut = new SftpBrowserViewModel(new SshConnectionProfile { DisplayName = "x", Host = "h", Port = 22 }, service, null, dialog);

        Assert.True(sut.IsConnected);
        Assert.Equal(0, StubProxy.Of(dialog).CountOf("PromptPassword"));
    }
}
