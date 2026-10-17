using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

namespace Yohuke.DanmuNeo.Windowing;

[SupportedOSPlatform("macos")]
internal static partial class MacOsCaptionButtons
{
    private const string OBJC_LIBRARY = "/usr/lib/libobjc.A.dylib";
    private const nuint FULL_SCREEN_STYLE_MASK = 1 << 14;
    private static readonly nint standardButtonSelector = RegisterSelector("standardWindowButton:");
    private static readonly nint boundsSelector = RegisterSelector("bounds");
    private static readonly nint contentViewSelector = RegisterSelector("contentView");
    private static readonly nint convertRectSelector = RegisterSelector("convertRect:toView:");
    private static readonly nint windowSelector = RegisterSelector("window");
    private static readonly nint flippedSelector = RegisterSelector("isFlipped");
    private static readonly nint hiddenSelector = RegisterSelector("isHiddenOrHasHiddenAncestor");
    private static readonly nint styleMaskSelector = RegisterSelector("styleMask");

    internal static bool IsFullScreen(nint window)
    {
        return (GetStyleMask(window, styleMaskSelector) & FULL_SCREEN_STYLE_MASK) != 0;
    }

    internal static Rect MeasureBounds(nint window)
    {
        var content = GetObject(window, contentViewSelector);
        if (content == 0 || GetObject(content, windowSelector) != window)
        {
            return default;
        }

        var contentBounds = GetRect(content, boundsSelector);
        var contentIsFlipped = GetBoolean(content, flippedSelector) != 0;
        var result = default(Rect);
        for (var i = 0; i < 3; i++)
        {
            var button = GetButton(window, standardButtonSelector, i);
            if (button == 0 || GetBoolean(button, hiddenSelector) != 0 || GetObject(button, windowSelector) != window)
            {
                continue;
            }

            var converted = ConvertRect(button, GetRect(button, boundsSelector), content);
            var bounds = MacOsCaptionGeometry.GetClientBounds(converted, contentBounds, contentIsFlipped);
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                result = result.Width > 0 ? result.Union(bounds) : bounds;
            }
        }

        return result;
    }

    internal static int CountVisible(nint window)
    {
        var count = 0;
        for (var i = 0; i < 3; i++)
        {
            var button = GetButton(window, standardButtonSelector, i);
            if (button != 0 && GetBoolean(button, hiddenSelector) == 0)
            {
                count++;
            }
        }

        return count;
    }

    private static MacOsRect GetRect(nint view, nint selector)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            GetRectIntel(out var rect, view, selector);
            return rect;
        }

        return GetRectArm(view, selector);
    }

    private static MacOsRect ConvertRect(nint view, MacOsRect rect, nint target)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            ConvertRectIntel(out var converted, view, convertRectSelector, rect, target);
            return converted;
        }

        return ConvertRectArm(view, convertRectSelector, rect, target);
    }

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RegisterSelector(string name);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend")]
    private static partial nint GetButton(nint receiver, nint selector, nint button);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend")]
    private static partial nint GetObject(nint receiver, nint selector);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend")]
    private static partial nuint GetStyleMask(nint receiver, nint selector);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend")]
    private static partial byte GetBoolean(nint receiver, nint selector);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend")]
    private static partial MacOsRect GetRectArm(nint receiver, nint selector);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend_stret")]
    private static partial void GetRectIntel(out MacOsRect rect, nint receiver, nint selector);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend")]
    private static partial MacOsRect ConvertRectArm(nint receiver, nint selector, MacOsRect rect, nint target);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend_stret")]
    private static partial void ConvertRectIntel(out MacOsRect converted, nint receiver, nint selector, MacOsRect rect, nint target);
}
