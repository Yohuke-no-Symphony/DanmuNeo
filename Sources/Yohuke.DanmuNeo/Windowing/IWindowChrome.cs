using Avalonia;

namespace Yohuke.DanmuNeo.Windowing;

internal interface IWindowChrome : IDisposable
{
    void ResizeClient(Size size);
}
