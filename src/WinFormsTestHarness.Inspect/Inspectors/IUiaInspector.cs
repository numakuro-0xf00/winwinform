namespace WinFormsTestHarness.Inspect.Inspectors;

using WinFormsTestHarness.Inspect.Models;

public interface IUiaInspector : IDisposable
{
    IReadOnlyList<WindowInfo> ListWindows();
    UiaNode GetTree(IntPtr hwnd, int? maxDepth = null);
    UiaNode? GetElementAtPoint(IntPtr hwnd, int x, int y);
}
