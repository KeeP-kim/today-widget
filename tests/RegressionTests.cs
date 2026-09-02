using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeskWidget
{
    internal static class RegressionTests
    {
        private static int checks;
        private static void Check(bool value, string name)
        {
            if (!value) throw new Exception(name);
            checks++;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            string root = args[0];
            string suite = args.Length > 1 ? args[1] : "all";
            string work = Path.Combine(root, "_test", "cases-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            Program.BaseDir = work;
            try
            {
                if (suite == "all" || suite == "json") JsonAndConfig(root, work);
                if (suite == "all" || suite == "ui") CollapsedQuotes(work);
                Console.WriteLine("PASS: " + checks + " checks (" + suite + ")");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex);
                return 1;
            }
        }

        private static void JsonAndConfig(string root, string work)
        {
            string[] invalid = {
                "", "  ", "hello", "{", "[", "{\"x\":", "{\"x\":1", "[1",
                "{\"x\" 1}", "{\"x\":}", "{\"x\":1,}", "[1,]", "[1 2]",
                "{\"x\":1 \"y\":2}", "{}garbage", "{}{}", "true false",
                "tru", "fals", "nul", "{\"x\":truth}", "{\"x\":undefined}",
                "\"unterminated", "\"slash\\", "\"bad\\q\"", "\"bad\\u12\"",
                "\"bad\\uZZZZ\"", "\"bad\\u 123\"", "\"raw\nline\"",
                "\"escaped\\nthen\tcontrol\"", "01", "+1", ".1", "1.", "1e", "1e+", "--1",
                "NaN", "Infinity", "1e9999", new string('[', 70) + "0" + new string(']', 70)
            };
            foreach (string value in invalid)
                Check(!Json.Parse(value).Exists, "Malformed JSON accepted: " + Array.IndexOf(invalid, value));

            string[] valid = {
                "{}", "[]", "true", "false", "0", "-0", "123", "-0.125", "1e+2", "1E-2",
                " \r\n {\"a\":null,\"b\":[true,false,{},[],1]} \t",
                "\"한글 😀\"", "\"\\\"\\\\\\/\\b\\f\\n\\r\\t\\uD55C\\uAE00\""
            };
            foreach (string value in valid)
                Check(Json.Parse(value).Exists, "Valid JSON rejected: " + Array.IndexOf(valid, value));
            Check(Json.Parse("[null,1]").Count == 2, "Null array item lost");
            Check(Json.Parse("{\"value\":\"1,386.00\"}")["value"].D == 1386, "API numeric string conversion");
            Check(Json.Parse("{\"tag_name\":\"v0.94\"}")["tag_name"].S == "v0.94", "Release response");
            Check(Json.Parse("{\"current\":{\"temperature_2m\":23.7},\"daily\":{\"time\":[\"2026-09-08\"]}}")
                      ["current"]["temperature_2m"].D == 23.7, "Weather response");
            string escaped = "한글\n\t\"\\\u0001";
            Check(Json.Parse("\"" + Json.Escape(escaped) + "\"").S == escaped, "Escape round trip");

            string path = Path.Combine(work, "config.json");
            foreach (string value in invalid)
                Preserve(path, value);
            foreach (string value in new[] { "[]", "[1]", "true", "123", "\"text\"", "null" })
                Preserve(path, value);

            var missing = new Config(Path.Combine(work, "missing.json"));
            missing.Load();
            Check(!missing.LoadFailed && missing.Symbols.Count > 0, "Fresh install defaults");
            missing.Save();
            Check(Json.Parse(File.ReadAllText(missing.Path)).IsObject, "Fresh install save");

            File.Copy(Path.Combine(root, "config.sample.json"), path, true);
            var cfg = new Config(path);
            cfg.Load();
            Check(!cfg.LoadFailed && cfg.Symbols.Count == 5, "Sample config load");
            Check(cfg.FileVersion == Config.AppVersion, "Sample version differs from app");
            Check(Assembly.GetExecutingAssembly().GetName().Version.ToString() == Config.AppVersion + ".0.0",
                  "Assembly version differs from app");
            cfg.ShowQuotes = false;
            cfg.Save();
            var reloaded = new Config(path);
            reloaded.Load();
            Check(!reloaded.LoadFailed && !reloaded.ShowQuotes && reloaded.Symbols.Count == 5,
                  "Normal config save and reload");
            Check(reloaded.FileVersion == Config.AppVersion, "Saved version");
        }

        private static void Preserve(string path, string value)
        {
            File.WriteAllText(path, value, new UTF8Encoding(true));
            string before = Convert.ToBase64String(File.ReadAllBytes(path));
            var cfg = new Config(path);
            cfg.Load();
            Check(cfg.LoadFailed, "Invalid config did not block saving");
            cfg.ShowQuotes = false;
            cfg.Save();
            Check(Convert.ToBase64String(File.ReadAllBytes(path)) == before, "Invalid config overwritten");
            Check(!File.Exists(path + ".tmp"), "Invalid config created a replacement");
        }

        private static void CollapsedQuotes(string work)
        {
            var cfg = new Config(Path.Combine(work, "ui-config.json"));
            cfg.Load();
            var window = new WidgetWindow(cfg);
            try
            {
                var type = typeof(WidgetWindow);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var bar = (Border)type.GetField("_quotesBar", flags).GetValue(window);
                Check(bar != null && bar.Parent is Grid, "Quotes restore strip missing from visual tree");
                Check(Grid.GetRow(bar) == 1, "Quotes restore strip in wrong row");
                var apply = type.GetMethod("ApplyMinimized", flags);
                var header = (UIElement)type.GetField("_headerRow", flags).GetValue(window);
                var body = (UIElement)type.GetField("_bodyHost", flags).GetValue(window);
                Check(bar.Visibility == Visibility.Collapsed && header.Visibility == Visibility.Visible,
                      "Expanded quotes show duplicate restore strip");
                cfg.ShowQuotes = false;
                apply.Invoke(window, null);
                Check(bar.Visibility == Visibility.Visible && header.Visibility == Visibility.Collapsed &&
                      body.Visibility == Visibility.Collapsed, "Collapsed quotes cannot be restored");
                cfg.Minimized = true;
                apply.Invoke(window, null);
                Check(bar.Visibility == Visibility.Collapsed && header.Visibility == Visibility.Visible,
                      "Minimized window lost restore header");
                cfg.Minimized = false;
                cfg.QuotesClosed = true;
                apply.Invoke(window, null);
                Check(bar.Visibility == Visibility.Collapsed, "Closed section exposes restore strip");
                cfg.QuotesClosed = false;
                apply.Invoke(window, null);
                type.GetField("_forceQuote", flags).SetValue(window, false);
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left);
                click.RoutedEvent = UIElement.MouseLeftButtonDownEvent;
                bar.RaiseEvent(click);
                Check(click.Handled && cfg.ShowQuotes && bar.Visibility == Visibility.Collapsed &&
                      body.Visibility == Visibility.Visible, "Restore click failed or leaked to window drag");
                Check((bool)type.GetField("_forceQuote", flags).GetValue(window), "Restore did not request fresh quotes");
                var saved = new Config(cfg.Path);
                saved.Load();
                Check(saved.ShowQuotes && !saved.LoadFailed, "Restore state not saved");
            }
            finally { window.Close(); }
        }
    }
}
