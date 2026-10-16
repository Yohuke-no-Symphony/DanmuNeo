using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 用户开始激活消息时捕获的房间身份与消息实例。
/// </summary>
internal sealed record WorkbenchFeedActivation(WorkbenchSelection Selection, object Item);