using WinFormsTestHarness.Inspect.Inspectors;

namespace WinFormsTestHarness.Inspect.Helpers;

public static class InspectorFactory
{
    public static IUiaInspector Create(string backend)
    {
        return backend.ToLowerInvariant() switch
        {
            "flaui" => new FlaUiInspector(),
            "swa" => new SwaUiaInspector(),
            _ => throw new ArgumentException($"Unknown backend: '{backend}'. Supported: flaui, swa")
        };
    }
}
