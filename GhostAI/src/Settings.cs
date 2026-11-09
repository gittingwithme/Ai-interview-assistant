using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace GhostAI {
    public class AppSettings {
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public string SystemPrompt { get; set; }
        public double Opacity { get; set; }
        public bool AlwaysOnTop { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }

        public AppSettings() {
            ApiKey = "";
            Model = "gemini-3.5-flash";
            SystemPrompt = "You are a discreet, high-intelligence meeting co-pilot. Keep answers direct, punchy, and actionable. When asked questions, provide crisp bullet points or exact words to say. Format using markdown.";
            Opacity = 0.92;
            AlwaysOnTop = true;
            WindowWidth = 420;
            WindowHeight = 720;
        }

        private static string GetConfigPath() {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "GhostAI");
            if (!Directory.Exists(folder)) {
                Directory.CreateDirectory(folder);
            }
            return Path.Combine(folder, "config.json");
        }

        public static AppSettings Load() {
            try {
                string path = GetConfigPath();
                if (File.Exists(path)) {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var serializer = new JavaScriptSerializer();
                    var settings = serializer.Deserialize<AppSettings>(json);
                    if (settings != null) return settings;
                }
            } catch { }
            return new AppSettings();
        }

        public void Save() {
            try {
                string path = GetConfigPath();
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(this);
                File.WriteAllText(path, json, Encoding.UTF8);
            } catch { }
        }
    }
}
