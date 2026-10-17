using Yohuke.DanmuNeo.I18n;
using System.Text.Json;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 负责导入和导出可分享工作区。
/// </summary>
public class WorkspaceShareService
{
    private static readonly JsonSerializerOptions JSON_OPTIONS = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// 从工作区创建分享包。
    /// </summary>
    public WorkspaceSharePackage CreatePackage(WorkspaceState workspace, AppSettings settings)
    {
        var markGroup = new MarkSymbolService().Resolve(settings, workspace);

        return new()
        {
            WorkspaceName = workspace.Name,
            TranslateOpenMark = markGroup.TranslateOpenMark,
            TranslateCloseMark = markGroup.TranslateCloseMark,
            LyricOpenMark = markGroup.LyricOpenMark,
            LyricCloseMark = markGroup.LyricCloseMark,
            DefaultLyricSource = settings.DefaultLyricSource,
            Rooms = workspace.LiveRooms.Select(room => new WorkspaceShareRoom
            {
                RoomId = room.RoomId,
                RoomName = room.RoomName,
                OwnerUid = room.OwnerUid,
                LyricTitle = room.LyricTitle,
                LyricText = room.LyricText,
                TranslatedLyricText = room.TranslatedLyricText,
                LyricSendMode = NormalizeLyricSendMode(room.LyricSendMode)
            }).ToList()
        };
    }

    /// <summary>
    /// 导出分享包到指定文件。
    /// </summary>
    public async Task ExportAsync(WorkspaceState workspace, AppSettings settings, string filePath,
        CancellationToken cancellationToken = default)
    {
        var package = CreatePackage(workspace, settings);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, package, JSON_OPTIONS, cancellationToken);
    }

    /// <summary>
    /// 从文件导入分享包并创建工作区。
    /// </summary>
    public async Task<WorkspaceState> ImportAsync(IEnumerable<WorkspaceState> existingWorkspaces, string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(filePath);
        var package =
            await JsonSerializer.DeserializeAsync<WorkspaceSharePackage>(stream, JSON_OPTIONS, cancellationToken)
            ?? throw LocalizedExceptionText.Create("Services.WorkspaceShare.Empty",
                message => new InvalidDataException(message));

        return CreateWorkspace(existingWorkspaces, package);
    }

    /// <summary>
    /// 从分享包创建新的工作区。
    /// </summary>
    public WorkspaceState CreateWorkspace(IEnumerable<WorkspaceState> existingWorkspaces, WorkspaceSharePackage package)
    {
        var names = existingWorkspaces.Select(workspace => workspace.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var workspaceName =
            GetUniqueName(string.IsNullOrWhiteSpace(package.WorkspaceName) ? Localization.Get("Services.WorkspaceShare.ImportedName") : package.WorkspaceName, names);

        return new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = workspaceName,
            AccountOverrideId = null,
            LiveRooms = package.Rooms.Select(room => new LiveRoomTabState
            {
                Id = Guid.NewGuid().ToString("N"),
                RoomId = room.RoomId,
                RoomName = string.IsNullOrWhiteSpace(room.RoomName) ? room.RoomId : room.RoomName,
                OwnerUid = room.OwnerUid,
                AccountOverrideId = null,
                InputDraft = "",
                LyricTitle = room.LyricTitle,
                LyricText = room.LyricText,
                TranslatedLyricText = room.TranslatedLyricText,
                LyricSendMode = NormalizeLyricSendMode(room.LyricSendMode)
            }).ToList()
        };
    }

    private static string GetUniqueName(string baseName, ISet<string> names)
    {
        if (!names.Contains(baseName))
        {
            return baseName;
        }

        var index = 2;

        while (true)
        {
            var candidate = $"{baseName} ({index})";

            if (!names.Contains(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private static LyricSendMode NormalizeLyricSendMode(LyricSendMode value)
    {
        return Enum.IsDefined(value) ? value : LyricSendMode.Bilingual;
    }
}
