using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace DeskWidget
{
    // 진입점은 두 가지다.
    //   1) Main()  - Onuln.exe 로 직접 실행할 때
    //   2) Run()   - Smart App Control 이 서명 없는 exe 를 막는 PC 에서,
    //                PowerShell 런처가 Onuln.dll 을 메모리로 올린 뒤 호출할 때
    public static class Program
    {
        /// <summary>config.json 을 둘 폴더. 런처가 알려준다.</summary>
        internal static string BaseDir;

        /// <summary>시작 프로그램에 등록할 실행 명령. 런처가 알려준다(없으면 exe 자신).</summary>
        internal static string LaunchCommand;

        [STAThread]
        public static void Main()
        {
            string dir = null;
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc)) dir = Path.GetDirectoryName(loc);
            }
            catch { }
            Run(dir, null);
        }

        /// <param name="baseDir">config.json 을 둘 폴더</param>
        /// <param name="launchCommand">시작 프로그램에 등록할 명령 (null 이면 현재 exe)</param>
        [STAThread]
        public static void Run(string baseDir, string launchCommand)
        {
            // 중복 실행 방지 - 이미 떠 있으면 조용히 끝낸다
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\DeskWidget_KR_v2", out createdNew))
            {
                if (!createdNew) return;

                if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir))
                    baseDir = AppDomain.CurrentDomain.BaseDirectory;

                BaseDir = baseDir;
                LaunchCommand = launchCommand;

                var cfg = new Config(Path.Combine(baseDir, "config.json"));
                cfg.Load();
                Sources.EcosKey = cfg.EcosKey;   // 한국은행 통계용 인증키

                var app = Application.Current;
                bool ownsApp = false;
                if (app == null)
                {
                    app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
                    ownsApp = true;
                }
                Theme.Apply(app);

                // 예기치 못한 오류로 위젯이 사라지지 않도록 삼킨다
                app.DispatcherUnhandledException += (s, e) => { e.Handled = true; };
                AppDomain.CurrentDomain.UnhandledException += (s, e) => { };

                var win = new WidgetWindow(cfg);
                win.Show();

                if (ownsApp) app.Run();
                else
                {
                    // 이미 Application 이 있는 호스트(PowerShell 등)에서는 직접 메시지 루프를 돈다
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    win.Closed += (s, e) => frame.Continue = false;
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                }

                GC.KeepAlive(mutex);
            }
        }
    }
}
