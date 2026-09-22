using System;
using System.IO;
using System.Reflection;
using System.Windows.Controls;

namespace DeskWidget
{
    internal static class PersistenceTests
    {
        internal static int Run(string work)
        {
            int count = 0;
            Action<bool, string> check = (ok, why) => { if (!ok) throw new Exception("Persistence: " + why); count++; };
            string path = Path.Combine(work, "persistence-config.json");
            var cfg = new Config(path);
            cfg.SetEcosKey("OldFixtureKey");
            check(cfg.Save(), "initial save failed");
            string original = File.ReadAllText(path);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var window = (AboutWindow)Activator.CreateInstance(typeof(AboutWindow), flags, null, new object[] { work, null }, null);
            int callbacks = 0;
            typeof(AboutWindow).GetField("_cfg", flags).SetValue(window, cfg);
            typeof(AboutWindow).GetField("_onKeySaved", flags).SetValue(window, new Action(() => callbacks++));
            var input = (TextBox)typeof(AboutWindow).GetField("_keyBox", flags).GetValue(window);
            var note = (TextBlock)typeof(AboutWindow).GetField("_keyNote", flags).GetValue(window);
            var saveKey = typeof(AboutWindow).GetMethod("SaveKey", flags);
            try
            {
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    check(!cfg.Save(), "file lock reported success");
                    input.Text = "NewFixtureKey";
                    saveKey.Invoke(window, null);
                    check(note.Text.Contains("저장하지 못했습니다"), "UI reported successful failed save");
                    check(cfg.EcosKey == "OldFixtureKey" && callbacks == 0, "failed save activated unsaved key");
                    check(input.Text == "NewFixtureKey", "retry input was lost");
                    check(File.ReadAllText(path) == original, "locked config changed");
                }
                saveKey.Invoke(window, null);
                var loaded = new Config(path); loaded.Load();
                check(!loaded.LoadFailed && loaded.EcosKey == "NewFixtureKey", "retry did not persist");
                check(note.Text.StartsWith("저장했습니다") && callbacks == 1, "successful retry not announced");
                string invalid = Path.Combine(work, "invalid-save-config.json");
                File.WriteAllText(invalid, "{");
                var corrupt = new Config(invalid); corrupt.Load();
                check(!corrupt.Save() && File.ReadAllText(invalid) == "{", "failed load overwrote original");
            }
            finally { window.Close(); }
            return count;
        }
    }
}
