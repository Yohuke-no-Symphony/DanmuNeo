namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 隔离普通视图模型测试对全局 Dispatcher 的访问，避免与 Headless 的重建过程并发。
/// </summary>
[CollectionDefinition(NAME, DisableParallelization = true)]
public class AvaloniaDispatcherTestCollection
{
    /// <summary>
    /// 使用全局 Dispatcher 的普通测试集合名称。
    /// </summary>
    public const string NAME = "Avalonia Dispatcher";
}
