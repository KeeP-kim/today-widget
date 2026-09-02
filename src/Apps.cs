// 즐겨찾기 - 바로가기(.lnk) 를 아이콘으로 띄우고 눌러서 연다.
//
// ★ .lnk 만 받는다 ★
//   설정 파일이 오염되면 여기 적힌 것이 그대로 실행된다. 그래서 두 가지를 지킨다.
//     1) 확장자는 .lnk 뿐이다. exe 를 직접 받지 않는다.
//     2) 명령줄을 우리가 만들지 않는다. 인자도 저장하지 않는다.
//        셸에 바로가기 경로만 넘기고 나머지는 셸이 판단한다.
//   바로가기는 사용자가 파일 선택창에서 고른 것만 들어온다.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskWidget
{
    /// <summary>즐겨찾기 한 칸.</summary>
    internal sealed class AppDef
    {
        public string Path;     // 보관소 안의 .lnk 전체 경로 (실행할 때 쓴다)
        public string File;     // 보관소 안의 파일 이름 (설정에 남기는 것은 이것뿐)
        public string Label;    // 화면에 띄울 이름 (툴팁)

        public string Key { get { return (Path ?? "").ToLowerInvariant(); } }
    }

    internal static class Apps
    {
        public const int MaxApps = 20;

        // ---------- 보관소 ----------
        //
        // 바탕화면의 바로가기를 '가리키기만' 하면, 그 파일이 지워지거나 옮겨지는 순간
        // 검사에 걸려 조용히 사라진다. 껐다 켜면 즐겨찾기가 비는 것이 그것이다.
        // 그래서 고를 때 이 폴더로 복사해 두고, 이후로는 여기서만 읽는다.
        //
        // 설정에는 파일 이름만 남는다. 위젯 폴더를 통째로 옮겨도 따라오고,
        // 설정이 오염되어도 보관소 밖을 가리킬 수 없다 (PathOf 가 이름만 받는다).

        /// <summary>
        /// 위젯이 스스로 챙기는 것들을 두는 폴더. 탐색기에서 바로 알아볼 수 있게 한글로 둔다.
        /// 나중에 다른 것도 여기 아래로 모은다.
        /// </summary>
        public static string DataDir
        {
            get
            {
                string b = Program.BaseDir;
                if (string.IsNullOrEmpty(b)) b = AppDomain.CurrentDomain.BaseDirectory;
                return System.IO.Path.Combine(b, "앱저장");
            }
        }

        /// <summary>바로가기를 모아 두는 폴더.</summary>
        public static string StoreDir
        {
            get { return System.IO.Path.Combine(DataDir, "즐겨찾기"); }
        }

        private static bool _storeReady;

        /// <summary>
        /// 보관소를 마련한다. 예전 자리(apps\)에 있던 것은 옮겨 온다.
        /// 한 번만 하면 되므로 표시를 남긴다.
        /// </summary>
        private static void EnsureStore()
        {
            if (_storeReady) return;
            _storeReady = true;
            try
            {
                string now = StoreDir;
                if (!Directory.Exists(now)) Directory.CreateDirectory(now);

                string b = Program.BaseDir;
                if (string.IsNullOrEmpty(b)) b = AppDomain.CurrentDomain.BaseDirectory;
                string old = System.IO.Path.Combine(b, "apps");
                if (!Directory.Exists(old)) return;

                foreach (string src in Directory.GetFiles(old, "*.lnk"))
                {
                    string dst = System.IO.Path.Combine(now, System.IO.Path.GetFileName(src));
                    try { if (!File.Exists(dst)) File.Move(src, dst); }
                    catch { }
                }
                // 비었으면 예전 폴더는 치운다. 남아 있으면 그대로 둔다.
                try { if (Directory.GetFileSystemEntries(old).Length == 0) Directory.Delete(old); }
                catch { }
            }
            catch { }
        }

        /// <summary>
        /// 보관소 안의 파일 이름을 전체 경로로 바꾼다.
        /// 이름만 받는다 - 구분자가 섞여 있으면 보관소 밖을 가리키려는 것이므로 거절한다.
        /// </summary>
        public static string PathOf(string file)
        {
            if (string.IsNullOrEmpty(file) || file.Length > 120) return null;
            if (file.IndexOf('\\') >= 0 || file.IndexOf('/') >= 0 || file.IndexOf(':') >= 0) return null;
            if (file.IndexOf("..", StringComparison.Ordinal) >= 0) return null;
            if (!file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
            try { EnsureStore(); return System.IO.Path.Combine(StoreDir, file); }
            catch { return null; }
        }

        /// <summary>
        /// 바로가기를 보관소로 들여온다. 보관된 파일 이름을 돌려준다 (실패하면 null).
        /// 같은 내용이 이미 있으면 그것을 쓴다.
        /// </summary>
        public static string Import(string src)
        {
            if (!IsAllowed(src)) return null;
            try
            {
                EnsureStore();
                string dir = StoreDir;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                foreach (string f in Directory.GetFiles(dir, "*.lnk"))
                    if (SameFile(src, f)) return System.IO.Path.GetFileName(f);

                string stem = SafeName(System.IO.Path.GetFileNameWithoutExtension(src));
                if (stem.Length == 0) stem = "shortcut";

                string name = stem + ".lnk";
                string dst = System.IO.Path.Combine(dir, name);
                for (int i = 2; File.Exists(dst) && i < 200; i++)
                {
                    name = stem + " (" + i + ").lnk";
                    dst = System.IO.Path.Combine(dir, name);
                }
                if (File.Exists(dst)) return null;

                File.Copy(src, dst);
                return name;
            }
            catch { return null; }
        }

        // ---------- 스토어 · 웹앱 ----------
        //
        // Claude·ChatGPT·Gemini·M365 같은 것들은 Store/PWA 앱이라 **.lnk 가 아예 없다**.
        // 대신 셸이 'AppUserModelID' 라는 이름표로 들고 있다.
        // 그래서 셸이 알려준 그 이름표로 바로가기를 하나 만들어 보관소에 둔다.
        //
        // ★ 보안 ★
        //   ID 는 사용자가 셸의 '설치된 앱' 목록에서 고른 것만 쓴다. 손으로 친 문자열은 안 받는다.
        //   그마저도 SafeAppId 로 글자를 제한한다 - 역슬래시·따옴표·공백이 섞이면 거절한다.
        //   여기서도 명령줄은 만들지 않는다. 만드는 것은 '앱 하나를 가리키는 바로가기' 뿐이고,
        //   여는 방법은 기존과 똑같다 (셸에 경로만 넘긴다).

        /// <summary>설치된 앱 하나.</summary>
        internal sealed class InstalledApp
        {
            public string Name;
            public string Id;      // AppUserModelID
        }

        /// <summary>
        /// 셸이 아는 설치된 앱 목록. 실패하면 빈 목록.
        /// COM 을 늦게 묶어 쓴다 - 참조를 더하지 않으려는 것이다.
        /// </summary>
        public static List<InstalledApp> InstalledApps()
        {
            var list = new List<InstalledApp>();
            object shell = null;
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                if (t == null) return list;
                shell = Activator.CreateInstance(t);

                object folder = t.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell,
                                               new object[] { "shell:AppsFolder" });
                if (folder == null) return list;

                object items = folder.GetType().InvokeMember("Items", BindingFlags.InvokeMethod, null, folder, null);
                if (items == null) return list;

                var en = items as System.Collections.IEnumerable;
                if (en == null) return list;

                foreach (object it in en)
                {
                    if (it == null) continue;
                    Type ti = it.GetType();
                    string name = ti.InvokeMember("Name", BindingFlags.GetProperty, null, it, null) as string;
                    string id = ti.InvokeMember("Path", BindingFlags.GetProperty, null, it, null) as string;
                    if (string.IsNullOrEmpty(name) || !IsSafeAppId(id)) continue;

                    list.Add(new InstalledApp { Name = name.Trim(), Id = id });
                    if (list.Count >= 600) break;   // 이상하게 많으면 거기서 끊는다
                }
            }
            catch { }
            finally
            {
                try { if (shell != null) Marshal.ReleaseComObject(shell); }
                catch { }
            }

            list.Sort(delegate(InstalledApp a, InstalledApp b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        /// <summary>AppUserModelID 로 쓸 수 있는 글자만 통과시킨다.</summary>
        public static bool IsSafeAppId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 256) return false;
            if (id.IndexOf('!') < 0) return false;          // 이름표에는 ! 가 반드시 있다
            foreach (char c in id)
            {
                if (char.IsLetterOrDigit(c)) continue;
                if (c == '.' || c == '_' || c == '-' || c == '!' || c == '+') continue;
                return false;                               // 역슬래시·공백·따옴표 등은 거절
            }
            return true;
        }

        /// <summary>
        /// 설치된 앱을 가리키는 바로가기를 보관소에 만든다. 만든 파일 이름을 돌려준다.
        /// 이미 같은 앱이 있으면 그것을 쓴다.
        /// </summary>
        public static string ImportApp(InstalledApp app)
        {
            if (app == null || !IsSafeAppId(app.Id)) return null;

            object shell = null;
            try
            {
                EnsureStore();
                string dir = StoreDir;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string stem = SafeName(app.Name);
                if (stem.Length == 0) stem = "app";

                string name = stem + ".lnk";
                string dst = System.IO.Path.Combine(dir, name);
                for (int i = 2; File.Exists(dst) && i < 200; i++)
                {
                    name = stem + " (" + i + ").lnk";
                    dst = System.IO.Path.Combine(dir, name);
                }
                if (File.Exists(dst)) return null;

                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                shell = Activator.CreateInstance(t);

                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                                            new object[] { dst });
                if (lnk == null) return null;

                Type tl = lnk.GetType();
                tl.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk,
                                new object[] { "shell:AppsFolder\\" + app.Id });
                tl.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);

                return File.Exists(dst) ? name : null;
            }
            catch { return null; }
            finally
            {
                try { if (shell != null) Marshal.ReleaseComObject(shell); }
                catch { }
            }
        }

        /// <summary>보관소에서 지운다. 즐겨찾기에서 뺄 때 부른다.</summary>
        public static void Forget(string file)
        {
            string p = PathOf(file);
            if (p == null) return;

            ForgetIcon(p);          // 들고 있을 이유가 없어졌다
            ClearIconOverride(p);   // 갈아끼운 그림도 같이 지운다

            // ★ 그림을 남기면 다음 사람이 그것을 물려받는다 ★
            //   .lnk 를 지우면 그 이름이 다시 비므로, 같은 프로그램을 나중에 또 넣으면
            //   ImportApp 이 똑같은 이름으로 만든다. 옆에 옛 그림이 남아 있으면
            //   ReadIcon 이 그것을 먼저 집어, 새로 넣은 즐겨찾기가 남의 그림을 뒤집어쓴다.
            //   우클릭해 되돌리는 법을 모르면 원인을 알 수가 없다.

            try { if (File.Exists(p)) File.Delete(p); }
            catch { }
        }

        /// <summary>파일 이름으로 쓸 수 있게 다듬는다.</summary>
        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(64);
            foreach (char c in s)
            {
                if (sb.Length >= 60) break;
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '(' || c == ')')
                    sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString().Trim().TrimEnd('.');
        }

        /// <summary>내용이 같은 파일인가. 바로가기는 1KB 안팎이라 통째로 비교해도 된다.</summary>
        private static bool SameFile(string a, string b)
        {
            try
            {
                var fa = new FileInfo(a);
                var fb = new FileInfo(b);
                if (!fa.Exists || !fb.Exists || fa.Length != fb.Length || fa.Length > 512 * 1024) return false;

                byte[] ba = File.ReadAllBytes(a);
                byte[] bb = File.ReadAllBytes(b);
                if (ba.Length != bb.Length) return false;
                for (int i = 0; i < ba.Length; i++) if (ba[i] != bb[i]) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 열어도 되는 바로가기인가.
        /// 확장자와 실제 존재 여부만 본다. 대상 프로그램이 무엇인지는 셸이 판단한다.
        /// </summary>
        public static bool IsAllowed(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 400) return false;

            // 경로에 줄바꿈이나 인자 구분자가 섞여 있으면 받지 않는다
            foreach (char c in path)
                if (c < ' ' || c == '"' || c == '|' || c == '<' || c == '>') return false;

            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return false;

            try
            {
                if (!System.IO.Path.IsPathRooted(path)) return false;
                return File.Exists(path);
            }
            catch { return false; }
        }

        /// <summary>바로가기를 연다. 허용되지 않으면 아무 것도 하지 않는다.</summary>
        /// <summary>
        /// 관리자 권한(높은 무결성)으로 돌고 있는가.
        ///
        /// 이걸 봐야 하는 이유: Windows UIPI 는 낮은 권한(탐색기)에서 높은 권한 창으로의
        /// 끌어다 놓기를 통째로 막는다. 우리 코드가 잘못한 것이 아니라 OLE 드롭 자체가
        /// 도달하지 못하는 것이라, 사용자에게 그 사실을 알려줘야 한다.
        /// </summary>
        public static bool IsElevated()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var pr = new System.Security.Principal.WindowsPrincipal(id);
                return pr.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        public static void Open(AppDef def)
        {
            if (def == null || !IsAllowed(def.Path)) return;
            try
            {
                // 인자 없이 경로만 넘긴다. 대상 실행은 셸이 바로가기를 풀어서 한다.
                var psi = new ProcessStartInfo(def.Path) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch { }
        }

        /// <summary>바로가기 이름에서 확장자를 뗀 것. 이름이 비면 이걸 쓴다.</summary>
        public static string NameOf(string path)
        {
            try { return System.IO.Path.GetFileNameWithoutExtension(path); }
            catch { return ""; }
        }

        /// <summary>
        /// 바로가기 아이콘. 실패하면 null 을 돌려주고, 부르는 쪽에서 글자로 대신한다.
        /// 얼려서 돌려주므로 여러 번 그려도 부담이 없다.
        /// </summary>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int count);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr h);

        /// <summary>
        /// 아이콘을 읽는다.
        ///
        /// ★ .lnk 를 그대로 넘기면 화살표가 따라온다 ★
        ///   ExtractAssociatedIcon 은 셸을 거치므로 바로가기 겹장식(작은 화살표)이 얹힌
        ///   그림을 준다. 바로가기라는 것은 이미 아는 사실이라 화면에서는 방해만 된다.
        ///   그래서 바로가기가 적어 둔 **원본 아이콘 자리**에서 직접 꺼낸다.
        ///
        /// 못 꺼내면(스토어 앱처럼 아이콘 자리가 비어 있을 때) 원래 방식으로 돌아간다 -
        /// 화살표가 붙더라도 아무것도 안 나오는 것보다는 낫다.
        /// </summary>
        /// <summary>
        /// 갈아끼운 그림이 있으면 그 자리. 바로가기 옆에 같은 이름 + .png 로 둔다.
        ///
        /// 확장자를 지우지 않고 덧붙이는 이유: "Claude.lnk.png" 는 어떤 앱 이름과도 겹치지 않는다.
        /// "Claude.png" 로 하면 진짜 'Claude.png' 라는 바로가기가 생겼을 때 서로를 덮는다.
        /// </summary>
        public static string IconOverridePath(string lnkPath)
        {
            if (string.IsNullOrEmpty(lnkPath)) return null;
            return lnkPath + ".png";
        }

        /// <summary>갈아끼운 그림이 있나.</summary>
        public static bool HasIconOverride(string lnkPath)
        {
            try
            {
                string p = IconOverridePath(lnkPath);
                return p != null && File.Exists(p);
            }
            catch { return false; }
        }

        /// <summary>
        /// 고른 그림에서 받아들일 상한. 압축 폭탄으로 메모리를 터뜨리지 않게 막는다.
        ///
        /// ★ 파일 크기만으로는 못 막는다 ★
        ///   zlib 은 1000:1 넘게 줄어든다. 실측으로 389KB 짜리 10000x10000 PNG 하나가
        ///   푸는 순간 384MB 를 먹었다. 변 길이를 **디코딩 전에** 보는 것이 진짜 방어다.
        ///   4096 이면 아이콘 원본으로 넉넉하고, 최악이라도 4096x4096x4 = 64MB 에서 멈춘다.
        /// </summary>
        private const long MaxPickBytes = 16 * 1024 * 1024;
        private const int MaxPickSide = 4096;
        private const int IconSide = 256;

        /// <summary>
        /// 그림을 갈아끼운다. **크기와 형식을 가리지 않는다** - 256x256 PNG 로 맞춰 보관한다.
        ///
        /// 읽을 수 있는 형식은 Windows 의 그림 코덱(WIC)이 정한다. PNG·JPG·BMP·GIF·TIFF 는
        /// 늘 되고, WEBP·HEIC 는 그 코덱이 깔려 있어야 된다(Windows 11 은 WEBP 를 기본 포함).
        /// 못 읽는 형식이면 그냥 거짓을 돌려주고, 부르는 쪽이 왜 안 되는지 알려준다.
        ///
        /// ★ 전에는 머리 24바이트만 읽고 크기가 정확히 256인지 봤다 ★
        ///   남이 준 파일을 디코더에 안 넘기려던 것인데, 이제는 넘긴다. 대신 그럴 만한 이유가 있다 -
        ///   이 파일은 사용자가 파일 선택창에서 **제 손으로 고른 제 파일**이고, 탐색기가 그 파일의
        ///   미리보기를 만들 때 이미 같은 코덱으로 디코딩한 것이다. 위험이 달라지지 않는다.
        ///   대신 파일 크기와 변 길이에 상한을 둬서 압축 폭탄만 막는다.
        ///
        /// 비율은 지킨다. 정사각형이 아니면 남는 자리를 투명하게 두고 가운데에 놓는다 -
        /// 억지로 늘리면 로고가 찌그러진다.
        /// </summary>
        public static bool SetIconOverride(string lnkPath, string src)
        {
            string why;
            return SetIconOverride(lnkPath, src, out why);
        }

        /// <param name="why">
        /// 실패한 까닭. 빈 문자열이면 "그냥 못 읽었다" 는 뜻이고, 채워져 있으면 그대로 보여준다.
        /// </param>
        public static bool SetIconOverride(string lnkPath, string src, out string why)
        {
            why = "";
            try
            {
                if (!IsAllowed(lnkPath) || string.IsNullOrEmpty(src)) return false;
                if (!File.Exists(src)) return false;

                var fi = new FileInfo(src);
                if (fi.Length < 16 || fi.Length > MaxPickBytes) return false;

                // ★ 파일 스트림으로 열고 PreservePixelFormat 을 줘야 투명도가 산다 ★
                //   Uri 로 열면 WebP 의 알파가 통째로 사라진다 - 형식이 Bgr32(알파 없음)로 오고,
                //   어떤 옵션을 줘도 안 살아난다. 같은 파일을 스트림으로 열면서
                //   PreservePixelFormat 을 주면 그대로 온다. 둘 다 있어야 한다.
                //   (여기까지 와도 아직 끝이 아니다 - FitSquare 의 Pbgra32 변환까지 있어야 산다)
                //
                // ★ 그리고 OnDemand 로 연다 - OnLoad 는 프레임을 만드는 그 자리에서 통째로 푼다 ★
                //   OnLoad 를 쓰면 아래 MaxPickSide 검사가 소용없다. 이미 다 풀린 뒤에 거절하는
                //   셈이라 메모리는 벌써 나갔다(실측 389KB 짜리 파일 하나에 384MB).
                //   OnDemand 는 머리글만 읽으므로 PixelWidth 를 공짜로 보고, 실제로 푸는 것은
                //   아래 FitSquare 다. 그래서 FitSquare 까지 스트림을 열어 둔 채로 한다.
                BitmapSource fitted;
                bool codecDropped;
                using (var fs = File.OpenRead(src))
                {
                    var dec = BitmapDecoder.Create(fs,
                        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
                    if (dec.Frames.Count == 0) return false;
                    BitmapFrame frame = dec.Frames[0];

                    if (frame.PixelWidth < 4 || frame.PixelHeight < 4) return false;
                    if (frame.PixelWidth > MaxPickSide || frame.PixelHeight > MaxPickSide) return false;

                    codecDropped = CodecDroppedAlpha(frame.Format, src);
                    fitted = FitSquare(frame, IconSide);
                }

                // ★ 코덱이 알파를 통째로 버렸으면 조용히 넘기지 않는다 ★
                //   판정은 **형식**으로 한다. 결과의 투명 픽셀을 세는 방법을 한 번 썼다가
                //   멀쩡한 그림을 막았다 - 알파 채널이 있으면서 값이 전부 255 인 PNG(포토샵
                //   PNG-24 등)는 아주 흔하고, 원본이 정사각형이면 FitSquare 가 여백조차 안 만들어
                //   투명 픽셀이 0 이 된다. 그러면 '불투명한 그림' 과 '알파를 잃은 그림' 이 구분되지
                //   않아, 멀쩡한 PNG 에 대고 "PNG 로 저장해서 넣으라" 는 말을 하게 된다.
                //   형식으로 보면 그 둘이 갈린다 - 알파 없는 형식으로 왔을 때만 코덱 탓이다.
                if (codecDropped)
                {
                    why = "이 그림에는 투명한 부분이 있는데, 이 PC 의 코덱이 그것을 버립니다.\n"
                        + "PNG 로 저장해서 넣어 주세요.";
                    return false;
                }

                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(fitted));

                string dst = IconOverridePath(lnkPath);
                using (var fs = File.Create(dst)) enc.Save(fs);

                ForgetIcon(lnkPath);   // 새 그림을 읽도록 캐시를 비운다
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 파일은 투명한 데가 있다는데, 코덱이 알파 없는 형식으로 내놓았나.
        ///
        /// **분명할 때만 참이다.** 알파를 담을 수 있는 형식(Bgra32/Pbgra32 등)이나 판단이 서지
        /// 않는 형식(WebP 가 흔히 주는 PixelFormats.Default)은 전부 거짓으로 둔다 -
        /// 잘못 참이 되면 멀쩡한 그림을 막게 되고, 그때 사용자에게는 고칠 방법이 없다.
        /// 놓치는 쪽이 막는 쪽보다 싸다.
        /// </summary>
        private static bool CodecDroppedAlpha(PixelFormat f, string src)
        {
            if (!FileClaimsAlpha(src)) return false;

            // 알파를 담지 못하는 것이 확실한 형식들. 여기 없는 것은 판단하지 않는다.
            return f == PixelFormats.Bgr32  || f == PixelFormats.Bgr24
                || f == PixelFormats.Rgb24  || f == PixelFormats.Bgr101010
                || f == PixelFormats.Bgr555 || f == PixelFormats.Bgr565
                || f == PixelFormats.Gray2  || f == PixelFormats.Gray4
                || f == PixelFormats.Gray8  || f == PixelFormats.Gray16;
        }

        /// <summary>
        /// 파일 스스로 '투명한 데가 있다' 고 말하고 있나.
        ///
        /// 코덱 탓인지 원래 불투명한 그림인지 갈라야 한다. 그러려면 파일 머리를 직접 읽는 수밖에 없다.
        /// PNG 는 IHDR 의 색 유형(6=RGBA, 4=회색+알파), WebP 는 VP8X 의 알파 비트와
        /// VP8L 의 alpha_is_used 비트를 본다. 머리 32바이트만 읽으므로 뒤에 오는 tRNS 청크는
        /// 못 본다 - 색 유형 3(팔레트)+tRNS 는 여기서 거짓이 된다. 못 읽으면 거짓, 괜히 막지 않는다.
        /// </summary>
        private static bool FileClaimsAlpha(string path)
        {
            try
            {
                byte[] h = new byte[32];
                using (var fs = File.OpenRead(path))
                    if (fs.Read(h, 0, 32) < 32) return false;

                // WebP : "RIFF" .... "WEBP" <fourcc>
                if (h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F'
                 && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P')
                {
                    if (h[12] == 'V' && h[13] == 'P' && h[14] == '8' && h[15] == 'X')
                        return (h[20] & 0x10) != 0;                       // VP8X 알파 플래그

                    if (h[12] == 'V' && h[13] == 'P' && h[14] == '8' && h[15] == 'L')
                    {
                        // VP8L: 시그니처(0x2F) 뒤 4바이트 중 29번째 비트가 alpha_is_used
                        uint bits = (uint)(h[21] | (h[22] << 8) | (h[23] << 16) | (h[24] << 24));
                        return ((bits >> 28) & 1) != 0;
                    }
                    return false;   // "VP8 " 단독은 알파를 담지 못한다
                }

                // PNG : 서명 + IHDR, 25번째 바이트가 색 유형
                if (h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47)
                    return h[25] == 6 || h[25] == 4;

                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// 비율을 지킨 채 한 변이 side 인 정사각형 한가운데에 놓는다. 남는 자리는 투명.
        ///
        /// ★ 그리기 전에 Pbgra32 로 바꾼다. 이 한 줄이 없으면 투명도가 죽는다 ★
        ///   PreservePixelFormat 으로 연 WebP 프레임은 형식이 PixelFormats.Default 로 온다.
        ///   알파는 분명히 들어 있는데(실측: 프레임에서 투명 픽셀 42437·192360), 그 형식 그대로
        ///   DrawImage 에 넘기면 결과에 투명 픽셀이 하나도 안 남는다. 미리 Pbgra32 로 바꿔 주면
        ///   그대로 산다(같은 파일에서 44606·48326). 원인을 디코더 쪽으로 오래 잘못 짚었던 자리다.
        /// </summary>
        private static BitmapSource FitSquare(BitmapSource src, int side)
        {
            if (src.Format != PixelFormats.Pbgra32)
                src = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);

            double k = Math.Min((double)side / src.PixelWidth, (double)side / src.PixelHeight);
            double w = src.PixelWidth * k;
            double h = src.PixelHeight * k;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawImage(src, new Rect((side - w) / 2, (side - h) / 2, w, h));

            var rtb = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        /// <summary>갈아끼운 그림을 버리고 원래 아이콘으로 돌아간다.</summary>
        public static void ClearIconOverride(string lnkPath)
        {
            try
            {
                string p = IconOverridePath(lnkPath);
                if (p != null && File.Exists(p)) File.Delete(p);
            }
            catch { }
        }

        private sealed class IconEntry
        {
            public ImageSource Img;
            public string Stamp;
        }

        private static readonly Dictionary<string, IconEntry> _icons =
            new Dictionary<string, IconEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 바로가기와 갈아끼운 그림의 '고친 때' 를 묶은 도장.
        ///
        /// 파일 정보를 두 번 읽는 것으로 끝난다 - 아이콘을 실제로 꺼내는 것(COM 개체 생성 +
        /// 아이콘 추출)에 비하면 값이 거의 안 나간다.
        /// </summary>
        private static string StampOf(string lnk)
        {
            try
            {
                var a = new FileInfo(lnk);
                var b = new FileInfo(IconOverridePath(lnk));
                return (a.Exists ? a.LastWriteTimeUtc.Ticks : 0L)
                     + "|"
                     + (b.Exists ? b.LastWriteTimeUtc.Ticks : 0L);
            }
            catch { return ""; }
        }

        /// <summary>
        /// 아이콘을 읽는다. 한 번 읽은 것은 들고 있는다.
        ///
        /// ★ 다시 그릴 때마다 새로 꺼내면 안 된다 ★
        ///   아이콘 하나를 꺼내는 데 WScript.Shell COM 개체를 만들고 바로가기를 열고
        ///   아이콘을 추출한다. 이것을 UI 스레드에서, 카드·본 바·조각 바 세 군데에서,
        ///   순서를 바꾸거나 구분선을 넣을 때마다 되풀이하고 있었다.
        ///   아이콘 일곱 개면 한 번 다시 그릴 때 COM 개체 스물한 개다.
        ///
        ///   그림은 Freeze 해 두므로 여러 자리에서 같은 것을 나눠 써도 안전하다.
        ///   바로가기나 갈아끼운 PNG 를 고치면 도장이 달라져 저절로 다시 읽는다.
        /// </summary>
        public static ImageSource LoadIcon(string path)
        {
            if (!IsAllowed(path)) return null;

            string stamp = StampOf(path);
            IconEntry hit;
            if (_icons.TryGetValue(path, out hit) && hit.Stamp == stamp) return hit.Img;

            ImageSource img = ReadIcon(path);
            _icons[path] = new IconEntry { Img = img, Stamp = stamp };
            return img;
        }

        /// <summary>보관소에서 사라진 것은 들고 있을 이유가 없다.</summary>
        public static void ForgetIcon(string path)
        {
            try { if (path != null) _icons.Remove(path); }
            catch { }
        }

        private static ImageSource ReadIcon(string path)
        {
            ImageSource swapped = FromPngFile(IconOverridePath(path));
            if (swapped != null) return swapped;

            ImageSource clean = FromIconLocation(path);
            if (clean != null) return clean;

            try
            {
                using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(path))
                {
                    if (ico == null) return null;
                    var src = Imaging.CreateBitmapSourceFromHIcon(
                        ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// 갈아끼운 PNG 를 읽는다.
        ///
        /// OnLoad 로 통째로 읽고 손을 뗀다 - 스트림을 물고 있으면 파일이 잠겨서
        /// 다음에 다시 갈아끼울 때 덮어쓰지 못한다.
        /// </summary>
        private static ImageSource FromPngFile(string png)
        {
            try
            {
                if (string.IsNullOrEmpty(png) || !File.Exists(png)) return null;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(png, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>
        /// 바로가기가 가리키는 원본에서 아이콘을 꺼낸다.
        ///
        /// ★ 여기서 얻은 경로는 그림에만 쓴다 ★
        ///   실행은 지금도 앞으로도 .lnk 를 통해서만 한다(Open 참고). 이 경로를 저장하지도,
        ///   명령줄로 만들지도 않는다. 그러면 '바로가기만 경유한다' 는 규칙이 유지된다.
        /// </summary>
        private static ImageSource FromIconLocation(string lnk)
        {
            object shell = null;
            IntPtr[] big = new IntPtr[1];
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return null;
                shell = Activator.CreateInstance(t);

                object o = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
                                          new object[] { lnk });
                if (o == null) return null;
                Type tl = o.GetType();

                // "C:\경로\프로그램.exe,0" 형태. 비어 있으면 대상 파일에서 꺼낸다.
                string loc = tl.InvokeMember("IconLocation", BindingFlags.GetProperty, null, o, null) as string;
                string file = null;
                int index = 0;

                if (!string.IsNullOrEmpty(loc))
                {
                    int c = loc.LastIndexOf(',');
                    if (c > 0)
                    {
                        file = loc.Substring(0, c).Trim();
                        int.TryParse(loc.Substring(c + 1).Trim(), out index);
                    }
                    else file = loc.Trim();
                }

                if (string.IsNullOrEmpty(file) || !File.Exists(file))
                {
                    file = tl.InvokeMember("TargetPath", BindingFlags.GetProperty, null, o, null) as string;
                    index = 0;
                }
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return null;

                if (ExtractIconEx(file, index, big, null, 1) <= 0) return null;
                if (big[0] == IntPtr.Zero) return null;

                var src = Imaging.CreateBitmapSourceFromHIcon(
                    big[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            catch { return null; }
            finally
            {
                try { if (big[0] != IntPtr.Zero) DestroyIcon(big[0]); }
                catch { }
                try { if (shell != null) Marshal.ReleaseComObject(shell); }
                catch { }
            }
        }
    }
}
