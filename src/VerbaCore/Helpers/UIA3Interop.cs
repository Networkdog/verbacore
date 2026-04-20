using System.Runtime.InteropServices;

namespace VerbaCore.Helpers;

/// <summary>
/// Minimal COM interop definitions for UIA3 (IUIAutomation).
/// The managed System.Windows.Automation uses UIA2 which Chromium/Electron does not support.
/// NVDA and FlaUI use the COM-based UIA3 API — this file provides the subset we need.
/// </summary>
internal static class UIA3
{
    // Pattern IDs
    public const int UIA_TextPatternId = 10014;

    // Create the UIA3 automation instance (CUIAutomation8 / CUIAutomation)
    public static IUIAutomation CreateAutomation()
    {
        // Try CUIAutomation8 first (Windows 8+), fall back to CUIAutomation
        try
        {
            var type8 = Type.GetTypeFromCLSID(CLSID_CUIAutomation8, throwOnError: false);
            if (type8 != null)
                return (IUIAutomation)Activator.CreateInstance(type8)!;
        }
        catch { /* Fall through to CUIAutomation */ }

        var type = Type.GetTypeFromCLSID(CLSID_CUIAutomation, throwOnError: true)!;
        return (IUIAutomation)Activator.CreateInstance(type)!;
    }

    // CUIAutomation8: {E22AD333-B25F-460C-83D0-0581107395C9}
    private static readonly Guid CLSID_CUIAutomation8 =
        new("E22AD333-B25F-460C-83D0-0581107395C9");

    // CUIAutomation: {FF48DBA4-60EF-4201-AA87-54103EEF594E}
    private static readonly Guid CLSID_CUIAutomation =
        new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
}

// ─── COM Interfaces ───────────────────────────────────────────────────

[ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomation
{
    // Methods in vtable order (IUIAutomation):
    // 0: CompareElements
    int CompareElements(IUIAutomationElement el1, IUIAutomationElement el2);
    // 1: CompareRuntimeIds
    int CompareRuntimeIds(int[] runtimeId1, int[] runtimeId2);
    // 2: GetRootElement
    IUIAutomationElement GetRootElement();
    // 3: ElementFromHandle
    IUIAutomationElement ElementFromHandle(IntPtr hwnd);
    // 4: ElementFromPoint
    IUIAutomationElement ElementFromPoint(tagPOINT pt);
    // 5: GetFocusedElement
    IUIAutomationElement GetFocusedElement();
    // 6: GetRootElementBuildCache
    IUIAutomationElement GetRootElementBuildCache(IntPtr cacheRequest);
    // 7: ElementFromHandleBuildCache
    IUIAutomationElement ElementFromHandleBuildCache(IntPtr hwnd, IntPtr cacheRequest);
    // 8: ElementFromPointBuildCache
    IUIAutomationElement ElementFromPointBuildCache(tagPOINT pt, IntPtr cacheRequest);
    // 9: GetFocusedElementBuildCache
    IUIAutomationElement GetFocusedElementBuildCache(IntPtr cacheRequest);
    // 10: CreateTreeWalker
    IUIAutomationTreeWalker CreateTreeWalker(IntPtr pCondition);
    // 11: ControlViewWalker
    IUIAutomationTreeWalker ControlViewWalker { get; }
    // 12: ContentViewWalker
    IUIAutomationTreeWalker ContentViewWalker { get; }
    // 13: RawViewWalker
    IUIAutomationTreeWalker RawViewWalker { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct tagPOINT
{
    public int x;
    public int y;
}

[ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationElement
{
    // 0: SetFocus
    void SetFocus();
    // 1: GetRuntimeId
    int[] GetRuntimeId();
    // 2: FindFirst
    IUIAutomationElement FindFirst(int scope, IntPtr condition);
    // 3: FindAll
    IntPtr FindAll(int scope, IntPtr condition);
    // 4: FindFirstBuildCache
    IUIAutomationElement FindFirstBuildCache(int scope, IntPtr condition, IntPtr cacheRequest);
    // 5: FindAllBuildCache
    IntPtr FindAllBuildCache(int scope, IntPtr condition, IntPtr cacheRequest);
    // 6: BuildUpdatedCache
    IUIAutomationElement BuildUpdatedCache(IntPtr cacheRequest);
    // 7: GetCurrentPropertyValue
    object GetCurrentPropertyValue(int propertyId);
    // 8: GetCurrentPropertyValueEx
    object GetCurrentPropertyValueEx(int propertyId, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue);
    // 9: GetCachedPropertyValue
    object GetCachedPropertyValue(int propertyId);
    // 10: GetCachedPropertyValueEx
    object GetCachedPropertyValueEx(int propertyId, [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue);
    // 11: GetCurrentPatternAs
    IntPtr GetCurrentPatternAs(int patternId, ref Guid riid);
    // 12: GetCachedPatternAs
    IntPtr GetCachedPatternAs(int patternId, ref Guid riid);
    // 13: GetCachedPattern
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object GetCachedPattern(int patternId);
    // 14: GetCurrentParent  (not in IUIAutomationElement — skip)
    // Actually 14 is: get_CachedParent
    IUIAutomationElement CachedParent { get; }
    // 15: get_CachedChildren
    IntPtr CachedChildren { get; }
    // 16-end: Properties — we'll use GetCurrentPropertyValue instead

    // We need GetCurrentPattern:
    // Actually the vtable is different. Let me use GetCurrentPatternAs with Guid.
}

[ComImport, Guid("4042C624-389C-4AFC-A630-9DF854A541FC")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTreeWalker
{
    IUIAutomationElement GetParentElement(IUIAutomationElement element);
    IUIAutomationElement GetFirstChildElement(IUIAutomationElement element);
    IUIAutomationElement GetLastChildElement(IUIAutomationElement element);
    IUIAutomationElement GetNextSiblingElement(IUIAutomationElement element);
    IUIAutomationElement GetPreviousSiblingElement(IUIAutomationElement element);
    IUIAutomationElement NormalizeElement(IUIAutomationElement element);
}

[ComImport, Guid("32EBA289-3583-42C9-9C59-3B6D9A1E9B6A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextPattern
{
    IUIAutomationTextRangeArray RangeFromPoint(tagPOINT pt);
    IUIAutomationTextRangeArray RangeFromChild(IUIAutomationElement child);
    IUIAutomationTextRangeArray GetSelection();
    IUIAutomationTextRangeArray GetVisibleRanges();
    IUIAutomationTextRange DocumentRange { get; }
    int SupportedTextSelection { get; }
}

[ComImport, Guid("CE4AE76A-E717-4C98-81EA-47371D028EB6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextRangeArray
{
    int Length { get; }
    IUIAutomationTextRange GetElement(int index);
}

[ComImport, Guid("A543CC6A-F4AE-494B-8239-C814481187A8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IUIAutomationTextRange
{
    IUIAutomationTextRange Clone();
    [return: MarshalAs(UnmanagedType.Bool)]
    bool Compare(IUIAutomationTextRange range);
    int CompareEndpoints(int srcEndPoint, IUIAutomationTextRange range, int targetEndPoint);
    void ExpandToEnclosingUnit(int textUnit);
    IUIAutomationTextRange FindAttribute(int attr, object val, [MarshalAs(UnmanagedType.Bool)] bool backward);
    IUIAutomationTextRange FindText([MarshalAs(UnmanagedType.BStr)] string text, [MarshalAs(UnmanagedType.Bool)] bool backward, [MarshalAs(UnmanagedType.Bool)] bool ignoreCase);
    object GetAttributeValue(int attr);
    void GetBoundingRectangles(out double[] boundingRects);
    IUIAutomationElement GetEnclosingElement();
    [return: MarshalAs(UnmanagedType.BStr)]
    string GetText(int maxLength);
}
