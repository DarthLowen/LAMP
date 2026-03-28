using System.Windows.Forms;

namespace BusylightTray;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplication());
    }
}
