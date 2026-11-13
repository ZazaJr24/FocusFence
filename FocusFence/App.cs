using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

[assembly: AssemblyTitle("FocusFence")]
[assembly: AssemblyDescription("Mouse confinement and Picture-in-Picture protection for games")]
[assembly: AssemblyCompany("FocusFence")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]
[assembly: SupportedOSPlatform("windows10.0.17763")]

namespace FocusFence
{
    internal static class App
    {
        internal static string ExecutablePath { get { return Environment.ProcessPath ?? throw new InvalidOperationException("The application executable path is unavailable."); } }
        [STAThread]
        private static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            if (args.Length == 2 && args[0] == "--runtime-info")
            {
                File.WriteAllLines(args[1], new[] { RuntimeInformation.FrameworkDescription, typeof(App).Assembly.GetCustomAttribute<TargetFrameworkAttribute>().FrameworkName, RuntimeInformation.ProcessArchitecture.ToString(), "Self-contained executable: " + ExecutablePath });
                return 0;
            }
            if (args.Length == 2 && args[0] == "--test-host") return SelfTests.TestHost(args[1]);
            if (args.Length == 2 && args[0] == "--smoke") return SelfTests.Smoke(args[1]);
            if (args.Length == 2 && args[0] == "--core-test") return SelfTests.RunCore(args[1]);
            if (args.Length == 2 && args[0] == "--inspect") return SelfTests.Inspect(args[1]);
            if (args.Length == 3 && args[0] == "--crash-worker") return SelfTests.CrashWorker(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--watch")
            {
                int pid;
                return int.TryParse(args[1], out pid) ? GuardEngine.Watch(pid, args[2]) : 1;
            }
            if (args.Length > 0 && args[0] == "--self-test") return SelfTests.Run(args.Length > 1 ? args[1] : "test-results.txt");
            if (args.Length >= 2 && args[0] == "--render") return Render(args[1], args.Length > 2 ? args[2] : "Overview");
            bool first;
            using (var mutex = new Mutex(true, "Local\\FocusFence.Desktop.v1", out first))
            {
                if (!first) { MessageBox.Show("FocusFence is already running. Open it from the system tray.", "FocusFence"); return 0; }
                MainController controller = null;
                try
                {
                    var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    application.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                    {
                        if (controller != null) controller.EmergencyStop();
                        Storage.Log(e.Exception.ToString());
                        MessageBox.Show("FocusFence has stopped protection.\n\n" + e.Exception.Message, "FocusFence");
                        e.Handled = true;
                    };
                    controller = new MainController(LoadWindow(), false);
                    application.Run();
                    return 0;
                }
                catch (Exception e) { Storage.Log(e.ToString()); MessageBox.Show(e.Message, "FocusFence could not start"); return 1; }
                finally { if (controller != null) controller.Dispose(); }
            }
        }
        internal static Window LoadWindow()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FocusFence.MainWindow.xaml")) return (Window)XamlReader.Load(stream);
        }
        private static int Render(string path, string page)
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var window = LoadWindow();
                using (var controller = new MainController(window, true))
                {
                    var navigation = window.FindName("Nav" + page) as System.Windows.Controls.Button;
                    if (navigation != null) navigation.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    window.ShowActivated = false; window.ShowInTaskbar = false; window.Show(); window.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(window);
                    var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using (var file = File.Create(path)) png.Save(file);
                    controller.CloseForReal();
                }
                app.Shutdown(); return 0;
            }
            catch (Exception e) { File.WriteAllText(path + ".error.txt", e.ToString()); return 1; }
        }
    }
}
