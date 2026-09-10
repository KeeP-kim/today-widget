// 기존 위젯을 종료하지 않고 분석 창만 별도 프로세스로 여는 진입점.
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace DeskWidget
{
    internal static class DollarProgram
    {
        [STAThread]
        public static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, @"Local\Onuln_DollarAnalysis_v1", out created))
            {
                if (!created) return;
                string root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                Program.BaseDir = root;
                // 실행 중인 본 위젯과 같은 config.json을 저장하면 서로 덮어쓴다.
                // 별도 실행 창은 자기 설정 파일에 위치만 별도로 보관한다.
                var cfg = new Config(Path.Combine(root, "dollar-analysis.json"));
                cfg.Load();
                // 별도 실행도 본체에서 선택한 은행을 읽는다. 본체 설정은 저장하지 않는다.
                var mainConfig = new Config(Path.Combine(root, "config.json"));
                mainConfig.Load();
                if (!mainConfig.LoadFailed) cfg.Bank = mainConfig.Bank;
                var app = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
                Theme.Apply(app);
                var window = new DollarAnalysisWindow(cfg, ct => DollarAnalysis.FetchAsync(cfg.Bank, ct));
                app.Run(window);
                GC.KeepAlive(mutex);
            }
        }
    }
}
