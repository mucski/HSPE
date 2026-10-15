using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HSKitGuard
{
    // Compiled into every plugin that depends on the separate HSUIKit.dll (add it as a linked
    // file: <Compile Include="..\_shared\KitGuard.cs" Link="KitGuard.cs" />). It contains NO
    // reference to any HSUIKit type, so it loads fine when HSUIKit.dll is missing or too old,
    // which is exactly when it is needed.
    //
    //   if (KitGuard.Check("MyPlugin", "1.2.0")) CreateUi();   // CreateUi in a [NoInlining] method
    //
    // On failure it logs a warning and shows a red banner on screen (plain IMGUI) telling the
    // player which plugin needs which HSUIKit.dll, instead of the plugin dying with a bare
    // TypeLoadException in the log. Call it from OnApplicationStart: Mono only lets the banner's
    // MonoBehaviour be added to a GameObject that early.
    internal static class KitGuard
    {
        public static bool Check(string plugin, string minVersion)
        {
            string found;
            Version need = new Version(minVersion);
            Version have = InstalledVersion(out found);
            if (have != null && have >= need) return true;

            string text = plugin + " needs HSUIKit.dll " + minVersion + " or newer, but " + found +
                ". Put the newest HSUIKit.dll in the Plugins folder (replace the old one) and restart. " +
                "Its new-style window is disabled until then (a basic fallback may be shown).";
            try { Debug.LogWarning("[" + plugin + "] " + text); }
            catch (Exception) { }
            try { KitGuardBanner.Show(text); }
            catch (Exception ex) { try { Debug.LogWarning("[KitGuard] could not show the banner: " + ex.Message); } catch (Exception) { } }
            return false;
        }

        // null when HSUIKit is absent or too old to report a version; 'found' describes what was seen.
        private static Version InstalledVersion(out string found)
        {
            Assembly asm = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "HSUIKit") { asm = a; break; }
            }
            if (asm == null)
            {
                try
                {
                    string path = Path.GetFullPath(Path.Combine(Application.dataPath, Path.Combine("..", Path.Combine("Plugins", "HSUIKit.dll"))));
                    if (File.Exists(path)) asm = Assembly.LoadFrom(path);
                }
                catch (Exception) { }
            }
            if (asm == null) { found = "HSUIKit.dll was not found"; return null; }

            try
            {
                Type t = asm.GetType("HSUIKit.KitVersion");
                FieldInfo f = t == null ? null : t.GetField("Current", BindingFlags.Public | BindingFlags.Static);
                Version v = f == null ? null : f.GetValue(null) as Version;
                if (v != null) { found = "the installed one is " + v; return v; }
            }
            catch (Exception) { }
            found = "the installed HSUIKit.dll is too old to report its version";
            return null;
        }
    }

    // Plain IMGUI so it needs nothing from HSUIKit. Several plugins can each show one; they stack.
    // Unity calls OnGUI by name, so an obfuscator must leave this type alone.
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    internal sealed class KitGuardBanner : MonoBehaviour
    {
        private const string ObjectName = "HSUIKit version warning";
        private string _text;
        private int _slot;
        private bool _dismissed;

        public static void Show(string text)
        {
            int slot = 0;
            foreach (GameObject go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                if (go.name == ObjectName) slot++;
            GameObject host = new GameObject(ObjectName);
            UnityEngine.Object.DontDestroyOnLoad(host);
            KitGuardBanner b = host.AddComponent<KitGuardBanner>();
            b._text = text;
            b._slot = slot;
        }

        private void OnGUI()
        {
            if (_dismissed) return;
            GUI.depth = -10000;
            float w = Mathf.Min(760f, Screen.width - 40f);
            float h = 78f;
            Rect r = new Rect((Screen.width - w) * 0.5f, 12f + _slot * (h + 8f), w, h);
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.55f, 0.08f, 0.08f, 1f);
            GUI.Box(r, "");
            GUI.Box(r, "");
            GUI.backgroundColor = old;
            GUIStyle label = new GUIStyle(GUI.skin.label);
            label.wordWrap = true;
            label.normal.textColor = Color.white;
            label.fontSize = 14;
            GUI.Label(new Rect(r.x + 12f, r.y + 6f, r.width - 110f, r.height - 12f), _text, label);
            if (GUI.Button(new Rect(r.xMax - 92f, r.y + 8f, 80f, 26f), "Dismiss")) _dismissed = true;
        }
    }
}
