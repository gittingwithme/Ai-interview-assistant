using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace GhostAI {
    public class Program {
        public static void Log(string msg) {
            Console.WriteLine("[GhostAI] " + msg);
            try {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " - " + msg + Environment.NewLine
                );
            } catch { }
        }

        [STAThread]
        public static void Main() {
            const string mutexName = "GhostAI_SingleInstance_Mutex_Session";
            bool isNewInstance;

            using (var mutex = new Mutex(true, mutexName, out isNewInstance)) {
                if (!isNewInstance) {
                    Log("Another instance is already running. Exiting silently.");
                    return;
                }

                try {
                    Log("Main starting");
                    var app = new Application();
                    Log("Application created");
                    var window = new MainWindow();
                    Log("MainWindow created");
                    app.Run(window);
                    Log("app.Run finished");
                } catch (Exception ex) {
                    Log("CRASH: " + ex.ToString());
                }
            }
        }
    }
}
