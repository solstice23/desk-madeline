using System;
using System.Windows.Forms;

namespace DeskMadeline
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // DWM window rects, the mouse, monitor bounds, and UpdateLayeredWindow must share one
            // physical-pixel coordinate space. DpiUnaware lets Windows virtualize some of those APIs; crossing
            // monitors with different DPI scales then produces position and collision offsets.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PetWindow());
        }
    }
}
