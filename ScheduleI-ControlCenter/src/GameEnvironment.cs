using System;
using System.Diagnostics;
using System.IO;

namespace ScheduleIControlCenter
{
    internal sealed class GameEnvironment
    {
        private readonly Func<bool> gameRunningProbe;

        public string GameRoot { get; private set; }
        public string SaveRoot { get; private set; }
        public string ToolRoot { get; private set; }

        public GameEnvironment(string gameRoot, string saveRoot, string toolRoot, Func<bool> gameRunningProbe = null)
        {
            GameRoot = gameRoot;
            SaveRoot = saveRoot;
            ToolRoot = toolRoot;
            this.gameRunningProbe = gameRunningProbe;
        }

        public static GameEnvironment Detect()
        {
            string gameRoot = FindGameRoot();
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appData = Directory.GetParent(roaming).FullName;
            string saves = Path.Combine(appData, "LocalLow", "TVGS", "Schedule I", "Saves");
            string toolRoot = Path.Combine(gameRoot, "ScheduleI-ControlCenter");
            return new GameEnvironment(gameRoot, saves, toolRoot);
        }

        public bool IsGameRunning()
        {
            if (gameRunningProbe != null)
                return gameRunningProbe();

            try { return Process.GetProcessesByName("Schedule I").Length > 0; }
            catch { return false; }
        }

        private static string FindGameRoot()
        {
            string[] starts =
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory
            };

            foreach (string start in starts)
            {
                DirectoryInfo current = new DirectoryInfo(start);
                for (int i = 0; i < 6 && current != null; i++, current = current.Parent)
                {
                    if (File.Exists(Path.Combine(current.FullName, "Schedule I.exe")))
                        return current.FullName;
                }
            }

            throw new DirectoryNotFoundException("Could not locate Schedule I.exe. Place the Control Center inside the game directory.");
        }
    }
}
