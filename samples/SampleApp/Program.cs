using WinFormsTestHarness.Logger;

namespace SampleApp;

internal static class Program
{
    [STAThread]
    static void Main()
    {
#if E2E_TEST
        TestLogger.Attach();
#endif

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());

#if E2E_TEST
        TestLogger.Detach();
#endif
    }
}
