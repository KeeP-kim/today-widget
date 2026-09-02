# 오늘은

Windows 바탕화면에 올려두는 작은 위젯. **환율 · 주식 · 코인 · 금리 · 날씨 · 시계**를 한 카드에서 본다.

> ### 🇰🇷 대한민국 환경 전용
>
> 이 앱은 **한국에서 쓰는 것을 전제로** 만들어졌다. 다른 나라에서도 실행은 되지만 쓸모가 크게 준다.
>
> - **시세 출처가 한국 서비스다** — 네이버 금융(환율·국내외 주식), 업비트(원화 마켓), 한국은행 ECOS(금리·물가)
> - **원화 기준이다** — 환율은 "1 외화 = ? 원", 코인은 KRW 마켓만 본다
> - **은행 고시 환율**은 하나은행 · 신한은행 두 곳
> - **화면 글자와 검색어가 한국어다** — 종목 검색은 한글 이름이 우선이고, 날씨 지역명은 한국 행정구역을 따른다
> - **시세에 찍히는 시각은 한국 서비스가 주는 값**이다 (KST). 시계 자체는 PC 시각을 따른다
>
> 날씨(Open-Meteo)와 시계만은 전 세계 어디서나 정상 동작한다.

<details>
<summary><b>In English</b> — what this is (for reviewers and passers-by)</summary>

**Oneuleun** ("Today is...") is a small Windows desktop widget showing exchange rates, stocks,
crypto, interest rates, weather and a clock on one card.

**It is built for South Korea and is of limited use elsewhere.** Its market data comes from
Korean services (Naver Finance, Upbit KRW markets, Bank of Korea ECOS), everything is priced
in Korean won, and the UI and search are in Korean. Quote timestamps come from those
Korean services and are therefore KST. Only the weather (Open-Meteo) and the clock,
which follows the PC's own time, work worldwide.

- **No installer.** Built with the C# compiler already shipped in Windows; runs as a single file.
- **No API key needed** except an optional free one from Bank of Korea ECOS, for interest-rate items.
- **No telemetry.** It talks only to the data sources listed under [데이터 출처](#데이터-출처).
- Collapsed sections make no network calls at all.

License: MIT. See [README in Korean](#오늘은) for full documentation.

</details>

- **설치할 게 없다.** Windows에 들어 있는 C# 컴파일러로 빌드하고, 실행 파일 하나로 돈다
- **API 키가 (거의) 필요 없다.** 한국은행 통계만 무료 키가 필요하고 나머지는 전부 공개 엔드포인트
- **가볍다.** 작업관리자 기준 7~28MB (평균 12MB), CPU 0.02%대
- 접어둔 항목은 **네트워크 호출도 하지 않는다**

```
┌──────────────────────────────────────┐
│ USD / KRW              하나은행 22:46 ↻ ─│
│ 1,386.40   ▼ 8.40   -0.60%           │
│ ──────────────────────────────────── │
│  🌙  23.7°  대체로 맑음                │
│      여의도 · 체감 29° · 25°/23°       │
│ ──────────────────────────────────── │
│ 8월 23일 (토)              23:46:43   │
└──────────────────────────────────────┘
```

---

## 실행

**받아서 쓰기** — [Releases](https://github.com/KeeP-kim/today-widget/releases) 에서 파일을 내려받아
한 폴더에 두고 `launch.vbs` 를 실행한다.

**직접 빌드하기** — 저장소에는 바이너리를 두지 않는다.

1. `build.cmd` 더블클릭 (최초 1회)
2. 바탕화면에 바로가기를 만들거나 `launch.vbs` 를 더블클릭

종료는 위젯 **우클릭 → 종료**.

---

## 쓰는 법

| 하고 싶은 것 | 방법 |
|---|---|
| **종목 추가** | 항목을 **꾹 눌러** 편집 모드 → 나타나는 **`+`** → 검색해서 선택 |
| **순서 바꾸기** | 편집 모드에서 **끌어서 이동** (다른 항목이 밀려나며 자리가 바뀜) |
| **삭제 / 되돌리기** | 편집 모드의 빨간 **`−`** · 되돌리기는 **`Alt + Z`** |
| **목록 ↔ 타일** | "시세" 옆 **▦** · 타일은 좌우 가장자리를 끌어 **가로 2~10개** |
| **보이는 개수** | 카드 **아래 가장자리**를 위아래로 끌기 · 접힌 목록은 **휠**로 넘김 |
| **크기 / 투명도** | **우하단 모서리** 드래그 (80~180%) · 투명도는 우클릭 메뉴 |
| **섹션 접기** | 각 섹션의 **`−`** (접으면 그 API 호출도 멈춤) |
| **네이버에서 열기** | 숫자나 날씨를 **더블클릭** |
| **은행 바꾸기** | `하나은행` 옆 **▾** → 신한은행 (환율에만 해당) |
| **새 버전 알림** | 편집 모드에서 카드 **맨 아래**에 뜬다 · 우클릭 → **정보 → 로그** 에서 변경 내역 |
| **가장자리에 붙이기** | 모니터 **끝으로 끌면** 얇은 바로 붙는다 · 안쪽으로 끌거나 더블클릭하면 떨어진다 |
| **급등·급락 알림** | 짧은 사이 크게 움직이면 **붉게 깜빡인다** · 타일은 커지기도 한다 · 우클릭에서 끌 수 있다 |

편집 모드는 **빈 곳 클릭 · `Esc` · 다른 창 클릭**으로 빠져나온다.

### 검색

한 검색창에서 **한글 · 영문 · 티커 · 종목코드**가 모두 통한다.

```
엔비디아 / NVDA        →  나스닥
삼성전자 / 005930      →  코스피
비트코인 / BTC         →  업비트
엔화 / JPY / 100엔     →  환율
기준금리 / 미국 / 연준  →  한국은행 통계
코스닥                 →  지수
```

날씨는 **날씨 영역의 `+`** 에서 지역명으로 따로 추가한다 (`여의도`, `해운대`, `제주` …).

---

## 가장자리에 붙이기

위젯을 모니터 **끝으로 끌면** 얇은 바로 붙는다. 상·하·좌·우 모두 된다.

| | |
|---|---|
| **위 / 아래** | 두께 20px · 한 줄로 가로 나열 |
| **좌 / 우** | 폭 60px (3배) · 가로쓰기로 위에서 아래로 쌓임 |
| **담는 내용** | 시세 → 날씨 → 시계 순으로, 자리에 들어가는 만큼만 |
| **떼기** | 화면 안쪽으로 끌기 · 더블클릭 · 우클릭 → 가장자리에서 떼기 |

작업표시줄처럼 **화면 공간을 확보**하므로 최대화한 창이 바를 덮지 않는다.
(Windows 셸의 AppBar 로 등록한다. 종료하면 확보한 공간은 바로 돌려준다)

**듀얼 모니터의 연결지점에는 붙지 않는다.** 거기는 화면 끝이 아니라 옆 모니터로
넘어가는 통로여서, 붙여두면 마우스가 지나갈 수 없게 된다.
그 방향에 맞닿은 모니터가 있는지 보고 판단한다.

---

## 급등·급락 알림

한 번의 갱신 사이에 크게 움직인 항목을 알려준다.

| 보기 | 표시 |
|---|---|
| **타일** | 10% 커지면서 배경이 붉게 깜빡임 |
| **목록 · 가장자리 바** | 배경만 깜빡임 |

문턱은 종류마다 다르다. 환율과 코인은 평소 흔들리는 폭이 자릿수부터 달라서
하나의 값으로는 쓸모가 없기 때문이다.

| 환율 | 지수 | 금리 | 주식 | 코인 |
|---|---|---|---|---|
| 0.15% | 0.3% | 0.5% | 0.7% | 1.0% |

`config.json` 의 `surgePercent` 를 0 보다 크게 주면 **전부 그 값으로 덮어쓴다**
(시험해 볼 때 0.01 처럼 낮게 주면 갱신마다 깜빡이는 걸 볼 수 있다).
`surgeAlert` 를 `false` 로 하면 아예 끈다. 우클릭 메뉴에도 있다.

접어뒀다가 한참 뒤에 편 경우는 알리지 않는다. 5분 넘게 끊겼던 값과의 비교는
'단기 변동' 이 아니기 때문이다.

---

## 데이터 출처

| 항목 | 출처 | 키 |
|---|---|---|
| 환율 (13개 통화) | 네이버 금융 — 하나은행 / 신한은행 고시 | 불필요 |
| 국내주식 · 지수 | 네이버 금융 실시간 | 불필요 |
| 해외주식 | 네이버 해외주식 | 불필요 |
| 코인 | 업비트 공개 API (원화 마켓) | 불필요 |
| 금리 · 물가 등 | 한국은행 ECOS | **무료 발급 필요** |
| 날씨 | [Open-Meteo](https://open-meteo.com) | 불필요 |
| 종목 검색 | 네이버 자동완성 | 불필요 |
| 지역 검색 | 네이버 날씨 + OpenStreetMap | 불필요 |
| 위치 자동감지 | ipapi.co / ipwho.is | 불필요 |

> 네이버 쪽은 **문서화되지 않은 내부 API**다. 예고 없이 바뀔 수 있으므로 실패하면 마지막 값을 유지하고,
> 오래되면 시각이 주황색 "지연"으로 바뀐다.

> 이 저장소에는 **어떤 인증키도 들어 있지 않다.** `config.json` 은 `.gitignore` 에 있고,
> 커밋된 적도 없다. 각자 자기 키를 넣어 쓴다.

### 필요한 키는 하나뿐 — 한국은행 ECOS

금리 · 물가 같은 **한국은행 통계 항목을 쓸 때만** 필요하다. 없어도 환율 · 주식 · 코인 · 날씨 · 시계는 그대로 돈다.

| | |
|---|---|
| **어디서** | [ecos.bok.or.kr](https://ecos.bok.or.kr) → 상단 **오픈API** → **인증키 신청** |
| **비용** | 무료 |
| **얼마나 걸리나** | 이메일 인증하면 즉시 발급 |
| **어디에 넣나** | 위젯 **우클릭 → 정보** 창의 `한국은행 인증키` 칸에 붙여넣고 저장 |

`config.json` 의 `ecosKey` 항목을 직접 고쳐도 된다. 둘은 같은 자리다.

나머지 출처는 **키 없이 그냥 열리는 공개 주소**라 따로 신청할 것이 없다.

---

## 설정

우클릭 → **설정 파일 열기** 로 `config.json` 을 편집한다. 대부분은 위젯에서 직접 조작하는 편이 빠르다.
항목 설명은 [`config.sample.json`](config.sample.json) 에 주석으로 적어두었다.

`config.json` 에는 **인증키와 위치 좌표**가 들어가므로 `.gitignore` 에 포함되어 있다. 공개 저장소에 올리지 말 것.

### 새 버전 알림 (선택)

`updateUrl` 에 아래 형태를 돌려주는 **https 주소**를 넣어두면 하루 한 번 확인해서,
편집 모드일 때 카드 맨 아래에 `새 버전 v0.52 가 있습니다` 한 줄을 띄운다.

```json
{ "version": "0.52" }
```

비워두면 **아무 것도 조회하지 않는다.** 확인에 실패해도 조용히 넘어간다.
`notifyUpdate` 를 `false` 로 하면 주소가 있어도 확인하지 않는다.
지금 버전과 지난 변경 내역은 **우클릭 → 정보 → 로그** 에서 볼 수 있다.

**주소는 직접 마련해야 한다.** 이 저장소에는 들어 있지 않다 —
서버 설정은 만든 사람의 계정과 도메인에 매여 있어서 남이 그대로 쓸 수가 없다.

위 JSON 한 덩이를 돌려주기만 하면 무엇이든 된다. 정적 파일 하나여도 충분하다.

```
GET https://내주소/today-widget.json
-> { "version": "0.52" }
```

- **GitHub Pages · 아무 정적 호스팅** — `today-widget.json` 파일 하나 올리면 끝
- **Cloudflare Workers · Vercel · Netlify** — 여러 앱을 한 주소에서 굴리고 싶을 때
- **GitHub Releases** — `https://api.github.com/repos/KeeP-kim/today-widget/releases/latest` 의
  `tag_name` 을 그대로 쓰려면 `worker` 없이도 되지만, 형식이 달라 중계가 한 번 필요하다

주소를 정했으면 `config.json` 의 `updateUrl` 에 넣는다. 안 넣으면 이 기능은 그냥 꺼져 있다.

---

## 빌드

```
build.cmd
```

Windows에 기본 포함된 `csc.exe`(.NET Framework 4.8)만 사용한다. Visual Studio나 SDK 설치가 필요 없다.
결과물은 두 개다.

| 파일 | 용도 |
|---|---|
| `Onuln.exe` | 직접 실행. 가장 가볍다 (작업관리자 기준 평균 12MB) |
| `Onuln.dll` | 런처가 메모리로 올려 실행 (약 90MB) |

### 왜 DLL 이 같이 나오나

Windows 11의 **Smart App Control** 은 서명되지 않은 exe 실행을 차단한다.
직접 빌드한 exe는 서명이 없으므로 환경에 따라 막힌다.

그래서 `launch.vbs` → `launch.ps1` 런처가 이렇게 동작한다.

```
launch.vbs → launch.ps1
              ├─ Onuln.exe 를 먼저 시도          (되면 이걸로 끝)
              └─ 막히면 Onuln.dll 을 메모리로 로드
                 (powershell.exe 는 MS 서명 파일이라 차단되지 않고,
                  파일 '실행'이 아니라 어셈블리 '로드'라 정책에 걸리지 않는다)
```

차단 여부는 이벤트 뷰어 `Microsoft-Windows-CodeIntegrity/Operational` (Event ID 3077 / 3118)에서 확인할 수 있다.

---

## 구조

| 파일 | 역할 |
|---|---|
| `Program.cs` | 진입점 (exe / DLL 두 경로) |
| `WidgetWindow.cs` | 위젯 본체 — UI, 데이터 루프, 편집 모드, 드래그 |
| `Sources.cs` | 종목 정의와 API 호출 |
| `Net.cs` | HttpClient 싱글톤, 링크 도메인 화이트리스트 |
| `Config.cs` | 설정 로드/저장 (값 범위 검증 포함) |
| `Json.cs` | 최소 JSON 파서 (외부 의존성 없음) |
| `Icons.cs` | 날씨 벡터 아이콘, 색 팔레트 |
| `Theme.cs` | 다크 메뉴·스크롤바 스타일 |
| `SearchWindow.cs` | 종목·지역 검색 창 |
| `AboutWindow.cs` | 정보 창 |
| `AssemblyInfo.cs` | **`TargetFramework` 특성 — 지우면 TLS가 깨진다** |

**보안 관련 원칙**

- 모든 통신 HTTPS, 인증서 검증은 .NET 기본 동작 그대로 (우회하지 않음)
- 프록시 자격 증명은 시스템에 설정된 프록시로만 가고 대상 서버로는 전송되지 않는다
- 브라우저로 여는 링크는 **네이버 · 업비트 · 한국은행 도메인만** 허용 (설정이 오염돼도 임의 URL 실행 불가)
- 설정 파일의 종목 코드는 URL에 들어가므로 문자 화이트리스트로 거른다
- JSON 파서에 재귀 깊이 제한 (깊게 중첩된 입력으로 프로세스를 죽일 수 없게)

---

## 개발 노트 — 같은 데서 두 번 막히지 않으려고 적어둔 것

- **`TargetFramework` 특성이 없으면 TLS가 깨진다.**
  `csc` 로 직접 빌드한 어셈블리에 이게 없으면 .NET이 4.0 호환(quirks) 모드로 돌린다.
  구형 TLS로 핸드셰이크를 시도해서 Cloudflare 뒤에 있는 서버와 연결이
  `SSL/TLS 보안 채널을 만들 수 없습니다` 로 실패한다. 네이버는 통과해서 **환율만 멀쩡한** 상태가 된다.

- **`AutomaticDecompression` 에 `Deflate` 를 넣지 말 것.**
  서버가 zlib 헤더가 붙은 deflate를 보내면 .NET의 `DeflateStream` 이 풀지 못한다. gzip만 쓴다.

- **`ServicePointManager.SecurityProtocol` 을 건드리지 말 것.**
  `SystemDefault` 로 두면 OS가 알아서 협상한다. `Tls12` 로 고정하면 오히려 일부 서버와 깨진다.

- **WPF에서 애니메이션이 걸린 속성은 값을 직접 넣어도 무시된다.**
  흔들림 애니메이션이 각도를 점유한 상태에서 `Angle = 8` 을 대입해도 아무 일도 일어나지 않는다.
  `BeginAnimation(prop, null)` 로 먼저 떼어내야 한다.

- **드래그 판정에 `GetPosition(this)` 를 쓰지 말 것.**
  마우스를 캡처한 상태에서 창 기준 좌표가 갱신되지 않는 경우가 있다. `GetCursorPos` 로 화면 절대 좌표를 쓴다.

- **`.cmd` 파일에 한글을 넣지 말 것.**
  cmd는 ANSI 코드페이지로 읽어서 UTF-8 주석이 깨지고 파싱이 망가진다. 한글이 필요하면 `.ps1`(UTF-8 BOM)로 뺀다.

- **PowerShell 리플렉션 호출 시 인자가 `PSObject` 로 감싸진다.**
  `$m.Invoke($null, @($a, $b))` 는 실패한다. `New-Object object[]` 로 배열을 만들어 `[string]` 으로 넣어야 한다.

- **WPF 기본 `MenuItem` 템플릿에는 흰색 아이콘 컬럼이 박혀 있다.**
  색 속성만 바꿔서는 다크 메뉴가 안 된다. `ControlTemplate` 을 통째로 교체해야 하고 하위 메뉴 팝업도 직접 만들어야 한다.

- **사내 프록시 뒤에서는 프록시 자격 증명을 넘겨줘야 한다.**
  `HttpClientHandler` 는 `UseProxy = true` 만으로는 인증을 요구하는 프록시에 응답하지 못해
  모든 요청이 407 로 죽는다. `WebRequest.GetSystemWebProxy()` 에 `CredentialCache.DefaultCredentials`
  를 물려 핸들러에 넣어야 회사망에서 동작한다. 프록시가 없는 환경에서는 아무 영향이 없다.

- **빌드 스크립트가 DLL 런처를 못 죽이면 구버전이 계속 돈다.**
  DLL 폴백으로 실행 중이면 프로세스명이 `powershell.exe` 라 exe 이름으로는 안 잡힌다.
  새로 띄운 최신 버전은 중복 실행 방지로 즉시 종료되어, **고쳐도 안 고쳐진 것처럼 보인다.**

---

## 내려받은 파일이 차단될 때

Windows 11 의 **Smart App Control** 은 서명이 없는 실행 파일을 막는다. 예외 등록이라는 것이 없고,
한번 끄면 Windows 를 다시 깔기 전에는 켤 수 없으므로 **끄지 말 것.**

대신 이 프로젝트는 두 벌로 나온다.

| | 언제 쓰나 |
|---|---|
| `Onuln.exe` | Smart App Control 이 꺼져 있거나, 서명된 릴리스를 받았을 때 |
| `Onuln.dll` + `launch.vbs` | 차단될 때. PowerShell 이 DLL 을 **메모리로 올려** 실행한다 |

`launch.vbs` 는 exe 를 먼저 시도하고, 막히면 알아서 DLL 쪽으로 넘어간다. 그냥 이것만 써도 된다.
(DLL 경로는 메모리를 20MB 대신 90MB 정도 쓴다.)

### 서명

서명은 **GitHub Actions 가 만든 릴리스 산출물에만** 붙는다.
서명이 보증하는 것은 '누가 만들었나' 가 아니라 '이 저장소의 이 소스로 만들었나' 이기 때문이다.
직접 빌드한 파일에 서명이 없는 것은 정상이다.

---

## 라이선스

[MIT](LICENSE) © keep kim
