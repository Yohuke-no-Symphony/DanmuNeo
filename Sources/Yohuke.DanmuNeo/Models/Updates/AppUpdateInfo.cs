namespace Yohuke.DanmuNeo.Models.Updates;

/// <summary>
/// 可供用户查看的应用正式发布版本。
/// </summary>
/// <param name="Version">发布版本。</param>
/// <param name="TagName">GitHub 发布标签。</param>
/// <param name="ReleaseUri">对应的 GitHub 发布页面。</param>
public sealed record AppUpdateInfo(Version Version, string TagName, Uri ReleaseUri);