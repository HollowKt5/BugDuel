using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace BeetleDesktop.Native
{
    internal static class Program
    {
        static bool diagnosticMode;
        [STAThread]
        static int Main(string[] args)
        {
            bool qa=args.Contains("--self-test") || args.Contains("--qa");
            diagnosticMode=qa;
            using var instance=new Mutex(true,qa?"Local\\BeetleDesktopNative.QA":"Local\\BeetleDesktopNative",out bool first);
            if(!first)return 0;
            try
            {
                var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException+=(sender,e)=>{
                    Log(e.Exception);e.Handled=true;app.Shutdown(1);
                };
                var desktop=new PetDesktop(qa);
                app.Startup+=(sender,e)=>{ desktop.Start();if(args.Contains("--self-test"))desktop.ScheduleSelfTest(); };
                app.Exit+=(sender,e)=>desktop.Dispose();
                return app.Run();
            }
            catch(Exception error) { Log(error);return 1; }
        }
        static void Log(Exception error)
        {
            try
            {
                string dir=diagnosticMode?Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","Artifacts")):
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BeetleDesktop");
                Directory.CreateDirectory(dir);File.AppendAllText(Path.Combine(dir,"errors.log"),DateTime.Now+" "+error+Environment.NewLine);
            }
            catch { Console.Error.WriteLine(error); }
        }
    }
}
