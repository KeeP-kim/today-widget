using System.Reflection;
using System.Runtime.Versioning;

// ★ 이 특성은 반드시 있어야 한다 ★
//
// csc 로 직접 빌드한 어셈블리에 TargetFramework 특성이 없으면 .NET Framework 는
// 해당 앱을 "4.0 시절 앱"으로 간주해 호환(quirks) 모드로 실행한다.
// 그 상태에서는 SchUseStrongCrypto 가 꺼져 구형 TLS 로 핸드셰이크를 시도하고,
// TLS 1.2 이상만 받는 서버(Cloudflare 뒤의 Open-Meteo 등)와 연결이
// "SSL/TLS 보안 채널을 만들 수 없습니다" 로 실패한다.
//
// 이 한 줄을 넣으면 SecurityProtocol 을 SystemDefault 로 두어도 정상 연결된다.
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

[assembly: AssemblyTitle("오늘은")]
[assembly: AssemblyProduct("오늘은")]
[assembly: AssemblyDescription("환율 / 시세 / 날씨 데스크톱 위젯")]
// Config.AppVersion 과 함께 올린다
[assembly: AssemblyVersion("0.88.0.0")]
[assembly: AssemblyFileVersion("0.88.0.0")]



































