using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Models.Logging;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证应用实际操作产生的会话日志及错误传播边界。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class MainWindowLogTests
{
    /// <summary>
    /// 两次保存都生成独立记录，不依赖状态字符串是否变化。
    /// </summary>
    [Fact]
    public async Task RepeatedSavesEachProduceAnInformationEntry()
    {
        using var context = new LiveRoomSearchTestContext();

        await context.ViewModel.SaveAsync();
        await context.ViewModel.SaveAsync();

        var saves = context.ViewModel.Log.Entries.Where(entry => entry.Source == "配置保存").ToArray();
        Assert.Equal(2, saves.Length);
        Assert.All(saves, entry =>
        {
            Assert.Equal(AppLogLevel.INFO, entry.Level);
            Assert.StartsWith("已保存 ", entry.Message);
        });
        Assert.True(saves[1].Sequence > saves[0].Sequence);
    }

    /// <summary>
    /// 保存失败仍向调用方抛出，同一异常经过上层处理时只记录一次，恢复后可继续保存。
    /// </summary>
    [Fact]
    public async Task SaveFailureIsRecordedOnceAndAllowsRetry()
    {
        using var context = new LiveRoomSearchTestContext();
        var path = context.StateService.SettingsFilePath;
        File.Delete(path);
        Directory.CreateDirectory(path);

        try
        {
            var error = await Record.ExceptionAsync(context.ViewModel.SaveAsync);
            Assert.NotNull(error);
            context.ViewModel.LogErrorOnce("设置", "关闭设置失败", error);

            var entry = Assert.Single(context.ViewModel.Log.Entries, value => value.Level == AppLogLevel.ERROR);
            Assert.Equal("配置保存", entry.Source);
            Assert.Contains(error.GetType().Name, entry.Details);
            Assert.DoesNotContain(context.ViewModel.Log.Entries, value => value.Level == AppLogLevel.INFO);
        }
        finally
        {
            Directory.Delete(path);
        }

        await context.ViewModel.SaveAsync();
        Assert.Contains(context.ViewModel.Log.Entries, entry => entry.Level == AppLogLevel.INFO);
    }

    /// <summary>
    /// 内容相同但属于独立操作的异常均应记录。
    /// </summary>
    [Fact]
    public void SeparateExceptionsWithIdenticalMessagesAreNotSuppressed()
    {
        using var context = new LiveRoomSearchTestContext();

        context.ViewModel.LogErrorOnce("导出", "操作失败", new IOException("磁盘不可写"));
        context.ViewModel.LogErrorOnce("导出", "操作失败", new IOException("磁盘不可写"));

        Assert.Equal(2, context.ViewModel.Log.Entries.Count(entry => entry.Level == AppLogLevel.ERROR));
    }

    /// <summary>
    /// 工作区切换保持同一个全局日志实例及其已有内容。
    /// </summary>
    [Fact]
    public void WorkspaceSwitchKeepsTheSharedSessionLog()
    {
        using var context = new LiveRoomSearchTestContext();
        var log = context.ViewModel.Log;
        log.Information("操作", "切换前的记录");

        context.ViewModel.SelectedWorkspace = context.ViewModel.Workspaces[1];

        Assert.Same(log, context.ViewModel.Log);
        Assert.Contains(log.Entries, entry => entry.Message == "切换前的记录");
    }

    /// <summary>
    /// 主窗口只释放自己创建的日志，外部注入实例由应用生命周期管理。
    /// </summary>
    [Fact]
    public void DisposeRespectsLogOwnership()
    {
        using var context = new LiveRoomSearchTestContext();
        using var sharedLog = new AppLogService();
        using var injectedViewModel = new MainWindowViewModel(context.StateService,
            avatarCacheService: new FakeRoomAvatarService(), log: sharedLog);

        injectedViewModel.Dispose();
        sharedLog.Information("应用", "主窗口释放后的记录");
        Assert.Contains(sharedLog.Entries, entry => entry.Message == "主窗口释放后的记录");

        var ownedLog = context.ViewModel.Log;
        ownedLog.Information("应用", "释放前");
        context.ViewModel.Dispose();
        var count = ownedLog.Entries.Count;
        ownedLog.Information("应用", "释放后");
        Assert.Equal(count, ownedLog.Entries.Count);
    }

    /// <summary>
    /// 搜索错误带有原工作区来源和异常详情，不进入正常结果分支。
    /// </summary>
    [Fact]
    public async Task RoomSearchFailureIsRecordedAsAnError()
    {
        using var context = new LiveRoomSearchTestContext();
        context.SearchService.RespondAsync = (_, _) => throw new HttpRequestException("请求被拦截");
        context.ViewModel.OpenAddRoomDialog(context.ViewModel.Workspaces[0]);
        context.ViewModel.LiveRoomSearchText = "主播";

        await context.ViewModel.SearchLiveRoomsAsync();

        var entry = Assert.Single(context.ViewModel.Log.Entries);
        Assert.Equal(AppLogLevel.ERROR, entry.Level);
        Assert.Contains("目标工作区", entry.Source);
        Assert.Contains("请求被拦截", entry.Message);
        Assert.Contains(nameof(HttpRequestException), entry.Details);
    }

    /// <summary>
    /// 关闭搜索弹窗导致的取消或迟到错误不生成错误记录。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelledSearchDoesNotProduceAnError(bool lateError)
    {
        using var context = new LiveRoomSearchTestContext();
        var pending = new TaskCompletionSource<LiveRoomSearchPage>();
        context.SearchService.RespondAsync = (_, _) => pending.Task;
        context.ViewModel.OpenAddRoomDialog(null);
        context.ViewModel.LiveRoomSearchText = "主播";
        var search = context.ViewModel.SearchLiveRoomsAsync();
        context.ViewModel.CloseAddDialog();
        pending.SetException(lateError ? new IOException("迟到失败") : new OperationCanceledException());

        await search;

        Assert.Empty(context.ViewModel.Log.Entries);
    }

    /// <summary>
    /// 无效房间输入是明确的校验警告，不发起头像或网络请求。
    /// </summary>
    [Fact]
    public async Task InvalidRoomIdProducesAWarning()
    {
        using var context = new LiveRoomSearchTestContext();
        context.ViewModel.OpenAddRoomDialog(null);
        context.ViewModel.NewRoomId = "invalid";

        await context.ViewModel.ConfirmAddDialogAsync();

        var entry = Assert.Single(context.ViewModel.Log.Entries);
        Assert.Equal(AppLogLevel.WARNING, entry.Level);
        Assert.Contains("直播间 ID", entry.Message);
        Assert.Empty(context.AvatarService.RequestedRooms);
    }

    /// <summary>
    /// 本地歌词读取失败每次都记录，既有返回值和状态仍可被调用方使用。
    /// </summary>
    [Fact]
    public async Task EveryFailedLyricImportIsRecorded()
    {
        using var context = new LiveRoomSearchTestContext();
        var path = Path.Combine(context.ViewModel.ConfigDirectory, "missing.lrc");

        Assert.False(await context.ViewModel.ImportLyricFileAsync(path));
        Assert.False(await context.ViewModel.ImportLyricFileToLibraryAsync(path));

        var errors = context.ViewModel.Log.Entries.Where(entry => entry.Level == AppLogLevel.ERROR).ToArray();
        Assert.Equal(2, errors.Length);
        Assert.All(errors, entry =>
        {
            Assert.Equal("歌词导入", entry.Source);
            Assert.Contains(nameof(FileNotFoundException), entry.Details);
        });
    }

    /// <summary>
    /// 备份输出目录不可用时生成错误，不尝试打开系统目录。
    /// </summary>
    [Fact]
    public async Task BackupFailureIsRecorded()
    {
        using var context = new LiveRoomSearchTestContext();
        File.WriteAllText(Path.Combine(context.ViewModel.ConfigDirectory, "Exports"), "occupied");

        await context.ViewModel.ExportAllBackupAsync();

        var entry = Assert.Single(context.ViewModel.Log.Entries);
        Assert.Equal(AppLogLevel.ERROR, entry.Level);
        Assert.Equal("配置备份", entry.Source);
        Assert.Contains("失败", entry.Message);
    }

    /// <summary>
    /// 工作区文件读写错误由业务入口记录，同时保留原有异常传播。
    /// </summary>
    [Fact]
    public async Task WorkspaceFileFailuresAreRecordedAndRethrown()
    {
        using var context = new LiveRoomSearchTestContext();
        var viewModel = context.ViewModel;

        Assert.NotNull(await Record.ExceptionAsync(() =>
            viewModel.ExportSelectedWorkspaceAsync(viewModel.ConfigDirectory)));
        Assert.NotNull(await Record.ExceptionAsync(() =>
            viewModel.ImportWorkspaceAsync(Path.Combine(viewModel.ConfigDirectory, "missing.json"))));

        Assert.Equal(2, viewModel.Log.Entries.Count(entry => entry.Level == AppLogLevel.ERROR));
        Assert.DoesNotContain(viewModel.Log.Entries, entry => entry.Level == AppLogLevel.INFO);
    }

    /// <summary>
    /// 重复的快捷键提示应保留为两次操作记录。
    /// </summary>
    [Fact]
    public void RepeatedShortcutPromptsAreBothRecorded()
    {
        using var context = new LiveRoomSearchTestContext();
        var binding = context.ViewModel.ShortcutBindingGroups.SelectMany(group => group.Bindings).First();

        context.ViewModel.BeginRecordShortcut(binding);
        context.ViewModel.BeginRecordShortcut(binding);

        Assert.Equal(2, context.ViewModel.Log.Entries.Count(entry => entry.Source == "快捷键"));
        Assert.All(context.ViewModel.Log.Entries, entry => Assert.Equal(AppLogLevel.INFO, entry.Level));
    }

    /// <summary>
    /// 自动与手动更新检查都记录真实故障，但保留原有弹窗策略。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateFailuresAreLoggedForAutomaticAndManualChecks(bool manual)
    {
        using var context = new AppUpdateTestContext();
        context.UpdateService.Response.SetException(new HttpRequestException("offline"));

        await (manual
            ? context.ViewModel.CheckForUpdatesManuallyAsync()
            : context.ViewModel.CheckForUpdatesOnStartupAsync());

        var entry = Assert.Single(context.ViewModel.Log.Entries);
        Assert.Equal(AppLogLevel.ERROR, entry.Level);
        Assert.Equal("检查更新", entry.Source);
        Assert.Contains("offline", entry.Details);
        Assert.Equal(manual, context.ViewModel.IsUpdateDialogOpen);
    }

    /// <summary>
    /// 取消更新后迟到的故障也不能变成日志错误。
    /// </summary>
    [AvaloniaFact]
    public async Task CancelledUpdateDoesNotLogLateFailure()
    {
        using var context = new AppUpdateTestContext();
        var check = context.ViewModel.CheckForUpdatesManuallyAsync();
        context.ViewModel.CloseUpdateDialog();
        context.UpdateService.Response.SetException(new HttpRequestException("late failure"));

        await check;

        Assert.Empty(context.ViewModel.Log.Entries);
    }
}
