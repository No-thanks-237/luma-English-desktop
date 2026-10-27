using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Luma.UI;

namespace Luma;

internal sealed class App : Application
{
    private Controller? controller;
    private EventWaitHandle? reopen;
    private RegisteredWaitHandle? registered;
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return SelfTest.Run(args.SkipWhile(x => x != "--self-test").Skip(1).FirstOrDefault());
        if (args.Contains("--online-self-test")) return SelfTest.Online(args.SkipWhile(x => x != "--online-self-test").Skip(1).First());
        if (args.Contains("--probe-screen")) return SelfTest.Probe(args.SkipWhile(x => x != "--probe-screen").Skip(1).First());
        if (args.Contains("--reading-fixture"))
        {
            var fixtureApp = new App { ShutdownMode = ShutdownMode.OnLastWindowClose }; Theme.Install(fixtureApp); return fixtureApp.Run(ReadingFixture.Create());
        }
        var requestedDataRoot = args.SkipWhile(x => x != "--data-dir").Skip(1).FirstOrDefault();
        var preview = args.Contains("--ui-preview"); if (preview && string.IsNullOrWhiteSpace(requestedDataRoot)) return 1;
        var suffix = preview ? ".Preview" : ""; var reopenName = @"Local\Luma.Reopen.01" + suffix;
        using var mutex = new Mutex(true, @"Local\Luma.Desktop.01" + suffix, out var first);
        if (!first) { try { using var e = EventWaitHandle.OpenExisting(reopenName); e.Set(); } catch { } return 0; }
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Theme.Install(app);
        app.Startup += (_, _) =>
        {
            try
            {
                app.controller = new Controller(args.Contains("--quiet"), requestedDataRoot);
                var auditFolder = args.SkipWhile(x => x != "--ui-audit").Skip(1).FirstOrDefault();
                if (preview && auditFolder != null) app.Dispatcher.BeginInvoke(async () => { try { await UiAudit.Run(app.controller, (MainWindow)app.MainWindow, auditFolder); } catch (Exception ex) { Directory.CreateDirectory(auditFolder); File.WriteAllText(Path.Combine(auditFolder, "ui-audit-error.txt"), ex.ToString()); } });
                app.reopen = new EventWaitHandle(false, EventResetMode.AutoReset, reopenName);
                app.registered = ThreadPool.RegisterWaitForSingleObject(app.reopen, (_, _) => app.Dispatcher.BeginInvoke(() => app.controller.ShowMain()), null, Timeout.Infinite, false);
            }
            catch (Exception ex) { MessageBox.Show("Luma 无法启动：\n" + ex.Message + "\n\n请完整解压程序目录后再试。", "Luma 拾词"); app.Shutdown(1); }
        };
        app.DispatcherUnhandledException += (_, e) =>
        {
            // Keep request payloads and keys out of logs.
            try { var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma"); Directory.CreateDirectory(root); File.AppendAllText(Path.Combine(root, "errors.log"), DateTime.Now.ToString("s") + " " + e.Exception.GetType().Name + "\n"); } catch { }
            MessageBox.Show("操作未能完成：" + e.Exception.Message, "Luma 拾词"); e.Handled = true;
        };
        app.Exit += (_, _) => { app.registered?.Unregister(null); app.reopen?.Dispose(); };
        return app.Run();
    }
}
