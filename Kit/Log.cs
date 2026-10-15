using System;
using System.IO;

namespace HSPE.Kit
{
    // Consolidated logging: every plugin in this project writes into ONE shared file
    // instead of its own <PluginName>.debug.log littering the game root - and does NOT
    // write at all unless the opt-in marker file exists, so a fresh install (or anyone a
    // plugin gets shared with) has zero log clutter until they explicitly ask for it.
    // (Debug.Log from HSPE doesn't reach output_log.txt on this setup - use this.)
    internal static class Log
    {
        private const string PluginName = "HSPE";
        private const string LogDir = "Plugins/Logs";
        private const string LogPath = LogDir + "/HSPlugins.debug.log";
        private const string EnabledMarker = LogDir + "/enabled.txt";

        public static void Write(string message)
        {
            try
            {
                if (!File.Exists(EnabledMarker)) return;
                Directory.CreateDirectory(LogDir);
                File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + "  [" + PluginName + "]  " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}
