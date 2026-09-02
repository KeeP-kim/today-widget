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
        public static ImageSource LoadIcon(string path)
        {
            if (!IsAllowed(path)) return null;

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
