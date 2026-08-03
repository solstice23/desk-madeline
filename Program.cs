using System;
using System.Windows.Forms;

namespace DeskMadeline
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Per-Monitor V2：所有坐标（Screen/Cursor/GetWindowRect/UpdateLayeredWindow）统一为物理像素。
            // 之前用 DpiUnaware，系统把屏幕/光标坐标虚拟化(逻辑像素)，但 UpdateLayeredWindow 用物理像素，
            // 两者不一致导致缩放≠100% 时窗口定位偏移。
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PetWindow());
        }
    }
}
