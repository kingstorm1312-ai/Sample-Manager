using System;
using System.Threading;
using System.Windows.Forms;

namespace SampleManager
{
    internal static class Program
    {
        private const string GlobalMutexName = @"Global\CpcSampleManager.SingleInstance";
        private const string LocalMutexName = @"Local\CpcSampleManager.SingleInstance";

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool createdNew;
            Mutex singleInstanceMutex;
            try
            {
                singleInstanceMutex = new Mutex(true, GlobalMutexName, out createdNew);
            }
            catch (UnauthorizedAccessException)
            {
                singleInstanceMutex = new Mutex(true, LocalMutexName, out createdNew);
            }

            if (!createdNew)
            {
                singleInstanceMutex.Close();
                MessageBox.Show(
                    "Sample Manager đang chạy.",
                    "Sample Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                GoogleSheetsSampleRepository repository = new GoogleSheetsSampleRepository();
                SampleManagerCache cache = new SampleManagerCache();
                MainForm mainForm = new MainForm(repository, cache);
                mainForm.Shown += delegate { UpdateCoordinator.Start(mainForm); };
                Application.Run(mainForm);
            }
            finally
            {
                singleInstanceMutex.ReleaseMutex();
                singleInstanceMutex.Close();
            }
        }
    }
}
