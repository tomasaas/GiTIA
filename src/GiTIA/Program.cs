using System;
using System.Windows.Forms;

namespace GiTIA
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Tia.RegisterAssemblyResolver(); // before anything touches a Siemens type
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
