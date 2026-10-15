using System.Diagnostics;
using System.Reflection;

namespace ToolBox
{
	internal static class Logger
	{
		public static readonly string pluginName = Assembly.GetExecutingAssembly().GetName().Name;

		public static void Log(object text) => UnityEngine.Debug.Log($"[{pluginName}] {text}");
		public static void LogW(object text) => UnityEngine.Debug.LogWarning($"[{pluginName}] {text}");
		public static void LogE(object text) => UnityEngine.Debug.LogError($"[{pluginName}] {text}");

		[Conditional("DEBUG")] public static void Line(object text) => System.Console.WriteLine($"[{pluginName}] {text}");
		[Conditional("DEBUG")] public static void LogD(object text) => UnityEngine.Debug.Log($"[{pluginName}] {text}");
		[Conditional("DEBUG")] public static void LogDW(object text) => UnityEngine.Debug.LogWarning($"[{pluginName}] {text}");
		[Conditional("DEBUG")] public static void LogDE(object text) => UnityEngine.Debug.LogError($"[{pluginName}] {text}");
	}
}
