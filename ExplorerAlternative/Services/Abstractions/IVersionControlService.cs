using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

public interface IVersionControlService
{
    /// <summary>
    /// 指定フォルダについてGit/SVNの管理情報を判定する。仕様書12章：現在のフォルダまたは
    /// その下位階層に管理情報が存在する場合に情報を返す。どちらにも該当しない場合は
    /// <see cref="VersionControlInfo.None"/> を返す。
    /// </summary>
    VersionControlInfo Detect(string path);
}
