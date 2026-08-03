using System;
using System.Windows.Forms;

namespace DeskMadeline
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // DWM 窗口矩形、鼠标、显示器边界和 UpdateLayeredWindow 必须使用同一套
            // 物理像素坐标。DpiUnaware 会让其中一部分 API 被 Windows 虚拟化，跨越
            // 不同缩放比例的显示器时便会产生位置和碰撞偏移。
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new PetWindow());
        }
    }
}
