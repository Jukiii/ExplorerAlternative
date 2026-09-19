using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ExplorerAlternative.Models;
using ExplorerAlternative.Services.Abstractions;

namespace ExplorerAlternative.Services;

/// <summary>
/// Windows Restart Manager API（rstrtmgr.dll）を使って、ファイルを使用しているプロセスを調べる
/// （仕様書52章「ファイルロック」）。追加のNuGet依存は不要で、Windows標準のAPIのみを使う。
///
/// Restart Managerは「アプリの再起動を伴うインストーラー向け」のAPIだが、登録したファイルを
/// 開いているプロセスの一覧を得る用途にも使える（実際にアプリを終了・再起動することはしない）。
/// 調査はあくまで失敗の原因の手掛かりを示すためのものなので、失敗しても例外は投げず、
/// 「使用中のプロセスは見つからなかった」として扱う。
/// </summary>
public sealed class FileLockService : IFileLockService
{
    // フォルダを指定された場合に調べる配下ファイル数の上限。巨大なフォルダで
    // 失敗時の応答が遅くならないようにするための安全弁。
    private const int MaxFilesPerQuery = 2000;

    private const int ErrorMoreData = 234;
    private const int MaxRetryCount = 5;

    public IReadOnlyList<LockingProcess> GetLockingProcesses(IEnumerable<string> paths)
    {
        try
        {
            var files = CollectFiles(paths);
            if (files.Count == 0)
            {
                return Array.Empty<LockingProcess>();
            }

            return QueryRestartManager(files);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or DllNotFoundException or EntryPointNotFoundException
                                       or InvalidOperationException or ExternalException or MarshalDirectiveException)
        {
            // 原因調査が失敗しても、元のエラー（削除失敗など）の表示を妨げない。
            return Array.Empty<LockingProcess>();
        }
    }

    public string? Describe(IEnumerable<string> paths)
    {
        var processes = GetLockingProcesses(paths);
        return processes.Count == 0 ? null : Format(processes);
    }

    /// <summary>仕様書52章の書式（プロセス名とPIDを1件ずつ）に整形する。</summary>
    public static string Format(IReadOnlyList<LockingProcess> processes)
    {
        var builder = new StringBuilder("このファイルを使用している可能性のあるプロセス：");

        foreach (var process in processes)
        {
            builder.AppendLine().AppendLine();

            var name = string.IsNullOrEmpty(process.ExecutableName)
                ? (string.IsNullOrEmpty(process.ApplicationName) ? "（不明）" : process.ApplicationName)
                : process.ExecutableName;
            builder.Append(name);

            if (!string.IsNullOrEmpty(process.ExecutableName) && !string.IsNullOrEmpty(process.ApplicationName))
            {
                builder.Append($"（{process.ApplicationName}）");
            }

            builder.AppendLine().Append($"PID: {process.ProcessId}");
        }

        return builder.ToString();
    }

    // 指定パス群を、Restart Managerに登録できるファイルの一覧へ展開する。
    // フォルダはRestart Managerが直接は扱えないため、配下のファイルを列挙する。
    private static List<string> CollectFiles(IEnumerable<string> paths)
    {
        var files = new List<string>();

        foreach (var path in paths)
        {
            if (files.Count >= MaxFilesPerQuery)
            {
                break;
            }

            if (File.Exists(path))
            {
                files.Add(path);
            }
            else if (Directory.Exists(path))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        files.Add(file);
                        if (files.Count >= MaxFilesPerQuery)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 一部のサブフォルダに入れなくても、列挙できた分だけで調べる。
                }
            }
        }

        return files;
    }

    private static IReadOnlyList<LockingProcess> QueryRestartManager(IReadOnlyList<string> files)
    {
        var sessionKey = new StringBuilder(NativeMethods.SessionKeyLength + 1);
        var startResult = NativeMethods.RmStartSession(out var session, 0, sessionKey);
        if (startResult != 0)
        {
            return Array.Empty<LockingProcess>();
        }

        try
        {
            var registerResult = NativeMethods.RmRegisterResources(
                session, (uint)files.Count, files.ToArray(), 0, null, 0, null);
            if (registerResult != 0)
            {
                return Array.Empty<LockingProcess>();
            }

            uint needed = 0;
            uint count = 0;
            NativeMethods.RM_PROCESS_INFO[]? buffer = null;

            // 1回目は件数だけを問い合わせ、ERROR_MORE_DATAで必要な数を受け取ってから
            // 取得し直す。その間にプロセスが増える場合に備えて数回まで再試行する。
            for (var attempt = 0; attempt < MaxRetryCount; attempt++)
            {
                var listResult = NativeMethods.RmGetList(session, out needed, ref count, buffer, out _);

                if (listResult == 0)
                {
                    return ToLockingProcesses(buffer, count);
                }

                if (listResult != ErrorMoreData)
                {
                    return Array.Empty<LockingProcess>();
                }

                buffer = new NativeMethods.RM_PROCESS_INFO[needed];
                count = needed;
            }

            return Array.Empty<LockingProcess>();
        }
        finally
        {
            NativeMethods.RmEndSession(session);
        }
    }

    private static IReadOnlyList<LockingProcess> ToLockingProcesses(NativeMethods.RM_PROCESS_INFO[]? buffer, uint count)
    {
        if (buffer is null || count == 0)
        {
            return Array.Empty<LockingProcess>();
        }

        var result = new List<LockingProcess>();
        var seen = new HashSet<int>();

        for (var i = 0; i < count && i < buffer.Length; i++)
        {
            var processId = buffer[i].Process.dwProcessId;
            if (!seen.Add(processId))
            {
                continue;
            }

            var executableName = TryGetExecutableName(processId);
            var applicationName = buffer[i].strAppName ?? string.Empty;

            // アプリ名が実行ファイル名と実質同じ場合は重複表示になるため省く。
            if (string.Equals(applicationName, executableName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(applicationName + ".exe", executableName, StringComparison.OrdinalIgnoreCase))
            {
                applicationName = string.Empty;
            }

            result.Add(new LockingProcess(processId, executableName, applicationName));
        }

        return result;
    }

    // 別ユーザー権限のプロセス等はMainModuleを読めないが、ProcessNameは通常取得できる。
    private static string TryGetExecutableName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName + ".exe";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // 調べている間にプロセスが終了した場合など。
            return string.Empty;
        }
    }

    private static class NativeMethods
    {
        // CCH_RM_SESSION_KEY（GUID文字列長）。
        internal const int SessionKeyLength = 32;

        [StructLayout(LayoutKind.Sequential)]
        internal struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            // CCH_RM_MAX_APP_NAME(255) + 1
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strAppName;

            // CCH_RM_MAX_SVC_NAME(63) + 1
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string strServiceShortName;

            public int ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;

            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        internal static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, StringBuilder strSessionKey);

        [DllImport("rstrtmgr.dll")]
        internal static extern int RmEndSession(uint pSessionHandle);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        internal static extern int RmRegisterResources(
            uint pSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            RM_UNIQUE_PROCESS[]? rgApplications,
            uint nServices,
            string[]? rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        internal static extern int RmGetList(
            uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[]? rgAffectedApps,
            out uint lpdwRebootReasons);
    }
}
