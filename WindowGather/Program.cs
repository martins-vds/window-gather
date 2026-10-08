namespace WindowGather;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\WindowGather.DesktopUtility", out bool firstInstance);
        ApplicationConfiguration.Initialize();
        if (!firstInstance)
        {
            MessageBox.Show("Window Gather is already running. Open it from the notification area.",
                "Window Gather", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "WindowGather");
            var engine = new GatherEngine(new NativeDesktop(), new SessionStore(directory));
            Application.Run(new MainForm(engine));
        }
        catch (Exception error)
        {
            MessageBox.Show($"Window Gather could not start.\n\n{error.Message}\n\n" +
                "An existing recovery file will not be overwritten.",
                "Window Gather", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}