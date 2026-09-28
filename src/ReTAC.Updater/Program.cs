using System;
using System.Windows.Forms;

namespace ReTAC.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        return 0;
    }
}
