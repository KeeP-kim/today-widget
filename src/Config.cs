// 설정 파일 로드/저장. 외부 파일에서 읽은 값은 전부 범위 검증을 거친다.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DeskWidget
{
    internal sealed class Config
    {
        /// <summary>
        /// 변경 내역. 손볼 때마다 0.01 씩 올리고 (0.10 부터 시작) 맨 위에 묶음을 하나 추가한다.
        /// 첫 칸이 버전이고 그 뒤가 그 버전에서 바뀐 것들이다.
        /// 여기가 유일한 기록처다 - AppVersion 을 이 배열에서 끌어오므로 버전과 내용이 어긋날 수 없고,
        /// 정보 창의 '로그' 버튼이 여는 팝업도 이 배열을 그대로 그린다.
        /// AssemblyInfo.cs 의 AssemblyVersion 도 같이 올릴 것.
        /// </summary>
        public static readonly string[][] Changelog =
        {
            new[] { "0.83",
                    "붙은 바에서 아이콘에 마우스를 올리면 커졌다 작아졌다 하던 것 - 진짜 원인을 잡았다",
                    "  (배경 알파가 0 이라 윈도우가 그 자리를 '창이 없는 곳' 으로 봤다. 그림의 불투명한",
                    "   점만 마우스를 받는데 호버로 그림이 커지면 그 점무늬가 움직여 되먹임이 생겼다)",
                    "같은 이유로 죽어 있던 것들이 살아났다 - 바 두께 손잡이, 꾹 눌러 구분선 넣기,",
                    "  투명한 바를 끌어서 창으로 되돌리기 (한 번도 눌린 적이 없었다)",
                    "가장자리에서 떼기가 창별로 동작한다 - 즐겨찾기 바에서 눌러도 시세 창이 떨어지던 것",
                    "크기 조절도 연 창의 것이 된다. 투명도는 원래대로 전체에 적용된다",
                    "조각 창 메뉴의 '종료' 는 그 조각만 닫는다. 앱을 끄는 것은 '앱 종료' 로 따로 뒀다",
                    "붙은 바 사이가 1px 벌어지던 것 - 자리와 크기를 따로 반올림해서 생긴 틈이었다",
                    "세로 바를 치운 뒤에도 가로 바가 옛 자리에 남던 것",
                    "세로 바가 화면 모서리까지 내려간다. 가로 바와 만나는 구석이 비지 않는다",
                    "조각 바가 붙어 있던 모니터를 기억한다 - 다시 켤 때 엉뚱한 화면에 붙던 것",
                    "바 두께를 끌어서 조절한다. 지금 크기의 절반까지 얇아진다",
                    "날씨·시계 바의 배경을 없앨 수 있다 (우클릭 → 바 배경 없애기)",
                    "바 끝에 ≡ 를 더했다. 붙어 있는 채로 설정에 닿는다",
                    "아이콘을 눌러 열면 세 번 튄다 (올라갈 때 감속, 떨어질 때 가속)",
                    "좁은 세로 바에서 긴 숫자를 줄인다. 106,110,000 → 1.06억 (올리면 원래 값)",
                    "즐겨찾기 아이콘의 바로가기 화살표를 뗐다",
                    "바에서 창으로 되돌릴 때 화면 밖으로 나가지 않는다",
                    "산출물 이름이 Onuln.exe / Onuln.dll 이다 (화면에 보이는 이름은 그대로)",
                    "빌드가 실패를 실패라고 말한다 - 컴파일이 깨져도 [완료] 를 찍던 것" },

            new[] { "0.82",
                    "가장자리에 붙인 바가 화면을 덜 밀어낸다 - 아이콘이 커질 자리까지 뺏고 있었다",
                    "  (즐겨찾기 바 169px 중 실제로 쓰는 것은 112px. 나머지는 바탕화면 위로 넘긴다)",
                    "같은 변에 붙이면 시세도 한 줄에 합류한다. 저마다 제 두께를 지켜 턱이 생기지 않는다",
                    "줄을 나눌 때 길이를 안 밝힌 바(시세)가 남는 자리를 갖는다 - 전에는 가장 작은 몫을 받아 날씨가 화면 절반을 빈 채로 차지했다",
                    "바 끝 − × 가 이웃 아이콘 위에 올라앉던 것. 이제 자리를 나눠 가져 겹칠 수 없다",
                    "− × 는 평소 옅게 물러나 있다가 손이 오면 또렷해진다",
                    "아이콘이 붙은 변을 딛고 선다. 띠 한가운데 떠 있어 발밑에 14px 이 남던 것",
                    "바를 지금 크기의 절반까지 얇게 만들 수 있다 (최소 배율 0.8 → 0.5)",
                    "한국은행 인증키를 정보 창에서 바로 넣는다. 설정 파일을 열지 않아도 된다",
                    "소스를 공개했다 - github.com/KeeP-kim/today-widget (MIT)" },

            new[] { "0.81",
                    "떼어낸 창의 머리 버튼(≡ − ×)이 알맹이를 가리지 않는다 - 얹지 않고 한 줄을 따로 쓴다",
                    "떼어낸 창에서 섹션 구분선을 없앴다. 카드 테두리가 이미 구분해 주는데 선까지 있어 괜히 길었다",
                    "시계 글자가 오른쪽 아래 크기 손잡이 밑으로 들어가 겹치던 것",
                    "머리 버튼 세로 정렬 - 즐겨찾기는 아이콘 여백만큼 더 올려 위아래를 맞췄다" },

            new[] { "0.80",
                    "스토어 앱·웹앱도 즐겨찾기에 담는다 - + 칸에서 '설치된 앱…' 을 고르면 목록이 뜬다",
                    "  (Claude·ChatGPT·Gemini·Microsoft 365 는 .lnk 가 아예 없어 파일 선택창으로는 못 담았다)",
                    "섹션 머리에 X - 접기와 달리 '펴기' 줄도 없이 완전히 닫힌다. 우클릭 메뉴로 다시 연다",
                    "우클릭 메뉴에서 '표시' 를 끄는 것도 이제 닫기다. 안 쓰는 것은 자리까지 없앤다",
                    "닫은 섹션은 조회도 멈춘다 - 안 보는 것 때문에 남의 서버를 두드리지 않는다",
                    "측면 바 즐겨찾기가 세로로 쌓인다. 아이콘만 싣는 바라 폭도 절반으로 줄였다",
                    "바 두께를 끌면 즐겨찾기 아이콘도 같이 커진다 (글자 상한 20에 묶여 안 커지던 것)",
                    "바 아이콘 사방 5px 여백 - 이웃끼리 10px 이 뜬다",
                    "조각 창 머리의 ≡ 와 − 가 세로로 어긋나던 것 (─ 는 기준선이 다른 글자였다)",
                    "조각 창 알맹이가 오른쪽 아래 크기 손잡이와 겹치던 것" },

            new[] { "0.79",
                    "섹션을 끄면 가장자리에 붙어 있던 그 조각 바도 사라지고 확보한 자리까지 돌려준다",
                    "(전에는 '즐겨찾기 표시' 를 꺼도 하단·측면 바에 그대로 남아 있었다)" },

            new[] { "0.78",
                    "가장자리에 붙인 바가 화면 끝에 딱 붙는다. 아래가 그만큼 비던 것을 고쳤다",
                    "같은 변에 여럿 붙이면 시세는 제 줄을 쓰고 날씨·즐겨찾기·시계는 한 줄에 나란히 선다",
                    "바 자리를 작업영역이 아니라 '화면 끝 + 남이 먹은 몫' 에서 직접 계산한다 (구멍이 나던 원인)",
                    "셸이 등록 직후 바를 안쪽으로 밀어내는 것을 알아채고 도로 앉힌다",
                    "좌·우 세로 바에서 긴 글자가 잘리지 않고 두 줄로 접힌다",
                    "떼어낸 조각 창에 설정(≡)·접기(─) 버튼과 우클릭 메뉴가 생겼다 (전에는 설정에 닿을 길이 없었다)",
                    "나눠 놓으면 바에 시계가 두 번 나오던 것, 즐겨찾기 아이콘이 남던 것을 고쳤다",
                    "조각 바 시계 날짜가 9-1 로 나오던 것을 9/1 로 맞췄다 (문화권 날짜 구분자)" },

            new[] { "0.77",
                    "가장자리에서 떼면 날씨·즐겨찾기·시계 창이 붙이기 전 자리로 함께 돌아온다",
                    "붙은 바에도 즐겨찾기 아이콘이 실린다 (시계 옆 고정 자리)",
                    "즐겨찾기에 + 칸을 두어 바로가기를 골라서도 담을 수 있다 (관리자 권한이면 끌어다 놓기가 막힌다)",
                    "혼합 DPI(배율이 다른 듀얼 모니터)에서 조각 창이 커서보다 2배 멀리 가던 것을 고쳤다",
                    "화면 밖으로 나간 조각 창을 다시 켤 때 찾아온다. 모니터 배치가 ㄱ자여도 빈 구역으로 안 밀린다",
                    "조각 창마다 오른쪽 아래에 크기 조절 손잡이. 배율을 창마다 따로 기억한다",
                    "조각 창도 각자 상하좌우에 붙는다. 같은 변에 여럿 붙이면 나란히 쌓인다",
                    "조각 창은 본 창이 접히거나 붙어도 제 알맹이를 유지한다. 접으면 창째 숨지 않고 '펴기' 줄이 남는다",
                    "가로 바에서 좌우로 끌면 마퀴 되돌리기, 위아래로 끌면 떼어내기 - 둘 다 산다",
                    "바 시계 왼쪽에 20px 여백. 흐르는 값이 시계에 바짝 붙지 않는다",
                    "즐겨찾기를 '앱저장 / 즐겨찾기' 폴더에 보관한다. 원본 바로가기가 지워져도 남고, 위젯 폴더를 옮겨도 따라온다" },

            new[] { "0.76",
                    "본 창을 끌면 붙어 있던 조각 창들도 같이 따라온다 (한쪽 방향만 되던 것)",
                    "조각을 끌어 본 창이 딸려 갔을 때도 그 자리를 저장한다" },

            new[] { "0.75",
                    "창 나누기 — 날씨·즐겨찾기·시계를 카드에서 떼어 각자 창으로 띄운다 (우클릭 메뉴)",
                    "가장자리를 맞대면 한 덩어리가 되어 같이 움직이고, 떼면 따로 움직인다" },

            new[] { "0.74",
                    "즐겨찾기 칸 추가 — 바탕화면이나 탐색기에서 바로가기(.lnk)를 끌어다 놓으면 아이콘으로 담긴다",
                    "누르면 열리고, 꾹 눌러 편집 모드에서 빨간 − 로 뺀다",
                    "exe 는 받지 않는다. 바로가기만 받아 셸에 그대로 넘기고 명령줄은 우리가 만들지 않는다" },

            new[] { "0.73",
                    "흐르는 바를 마우스로 잡아 좌우로 끌 수 있다 · 놓으면 그 자리에서 이어 흐른다",
                    "마퀴 항목이 잘려 한 바퀴만 돌던 문제 수정 (Canvas 로 감싸 제 크기대로 배치)" },

            new[] { "0.72",
                    "마퀴가 한 바퀴만 돌고 멈추던 문제 수정 — 흐를 자리 폭에 눌려 항목이 잘려 있었다" },

            new[] { "0.71",
                    "전체화면 앱(게임 등)이 뜨면 가장자리 바가 뒤로 빠진다 — 게임 위에 계속 떠 있던 문제",
                    "전체화면이 끝나면 다시 올라온다" },

            new[] { "0.70",
                    "세로 바에서 시계가 바닥까지 밀려 빈 자리가 생기던 문제 수정 — 항목 바로 아래에 붙는다" },

            new[] { "0.69",
                    "가장자리 바에서 날짜·시각 글자도 두께를 따라 커진다 (시계만 빠져 있었다)" },

            new[] { "0.68",
                    "가로 바에 내용이 넘치면 흘러간다 (마퀴) · 마우스를 올리면 멈춘다",
                    "혼합 DPI에서 확보 공간이 화면 절반으로 잡히던 문제 수정 — 두께 대신 모니터 모서리로 배율을 뽑는다",
                    "가장자리 바 배율을 카드와 따로 저장한다 (dockScale)" },

            new[] { "0.67",
                    "가장자리 바에서 항목을 더블클릭하면 해당 페이지가 열린다 — 바가 떨어져 버리던 문제 수정",
                    "바의 날씨 아이콘·글자도 두께를 따라 커진다" },

            new[] { "0.66",
                    "가장자리 바 급등 표시를 항목별로 — 이번엔 실제로 (지난번 패치가 엉뚱한 곳을 짚었다)",
                    "AppBar 좌표를 물리 픽셀로 변환 — 배율 다른 보조 모니터에서 공간이 안 잡히던 원인",
                    "붙이기 판정을 후하게 — 커서가 가장자리 근처거나 창이 걸쳐 있으면 붙는다",
                    "바 안쪽 가장자리를 끌어 두께를 바꾼다. 글자도 같이 커진다" },

            new[] { "0.65",
                    "가장자리 바의 급등 표시를 항목별로 — 바 전체가 물들지 않고 움직인 항목만 번쩍인다",
                    "자리를 확보하기 전에 창을 먼저 옮긴다 — 셸은 창이 놓인 모니터로 대상을 판단한다" },

            new[] { "0.64",
                    "바에서 부풀기 시작하는 변동폭을 설정으로 뺐다 (surgeGrowPercent, 비우면 5%)",
                    "커질 때는 최소 5%는 커진다 — 문턱만 낮추면 티가 안 나던 것" },

            new[] { "0.63",
                    "가장자리 바에서 5% 이상 움직인 항목이 스스로 부풀어 오른다 (움직인 만큼, 최대 10%)",
                    "1초 주기 대비 최적화 — 바를 매번 새로 만들지 않고 값만 갈아끼우고, 붙어 있으면 카드는 그리지 않는다",
                    "갱신 주기가 짧아도 최소 1분에 한 번은 메모리를 반환한다 (1초 주기에서 워킹셋이 부풀던 문제)",
                    "가장자리 바가 시세 갱신 때도 다시 그려진다 - 이전에는 날씨 갱신에 묻어갔다" },

            new[] { "0.62",
                    "혼합 DPI 보조 모니터에서 세로 바가 화면 아래까지 안 차던 문제 수정" },

            new[] { "0.61",
                    "혼합 DPI 듀얼 모니터에서 바가 엉뚱한 자리에 붙던 문제 수정 (200% + 100% 조합)",
                    "붙이기가 실패해도 위젯은 뜬다 — 시작 중 예외로 통째로 안 뜨던 것을 막았다" },

            new[] { "0.60",
                    "시세 갱신 주기에 1초·5초·10초·30초를 추가했다 (1초·5초·10초·30초·1분·5분)",
                    "급등·급락 기준값을 30초 창으로 바꿨다 — 주기를 1초로 두어도 늘 30초 전 값과 비교한다",
                    "날씨 주기는 1분 아래로 내려가지 않는다 - 관측값이 그보다 자주 바뀌지 않는다" },

            new[] { "0.59",
                    "가장자리에 붙이거나 새로고침할 때 딸려 나오던 헛깜빡임 수정 — 20초 이상 벌어진 값만 비교한다",
                    "보안: 한국은행 인증키를 영숫자만 통과시킨다 (URL 경로에 그대로 들어가는 값이다)",
                    "보안: ECOS 국가 코드도 URL에 들어가기 전에 거른다 · 종목 코드에서 점 두 개를 막는다" },

            new[] { "0.58",
                    "단기 급등·급락한 항목을 알린다 — 타일은 10% 커지면서 배경이 붉게 깜빡이고, 목록과 가장자리 바에서는 배경만 깜빡인다",
                    "문턱은 종류별로 다르다 (환율 0.15% · 지수 0.3% · 주식 0.7% · 코인 1% · 금리 0.5%). config 의 surgePercent 로 한 값으로 덮어쓸 수 있다",
                    "접어뒀다가 한참 뒤에 편 경우는 알리지 않는다 - 단기 변동이 아니다" },

            new[] { "0.57",
                    "가장자리 도킹 판정을 커서 기준으로 바꿨다 — 오른쪽·아래에 안 붙던 문제 수정",
                    "카드를 어디를 잡았든 화면 끝까지 밀면 네 방향 모두 똑같이 붙는다" },

            new[] { "0.56",
                    "좌·우에 붙을 때는 폭을 3배(60px)로 넓히고 글자를 눕히지 않는다 — 가로쓰기로 위에서 아래로 쌓는다",
                    "바를 떼는 동작을 화면 안쪽 방향으로만, 문턱도 90px 로 올렸다 (스치기만 해도 떨어지던 것 수정)" },

            new[] { "0.55",
                    "모니터 가장자리로 끌면 20px 얇은 바로 붙는다 (상·하·좌·우)",
                    "작업표시줄처럼 화면 공간을 확보해서 최대화한 창이 바를 덮지 않는다",
                    "듀얼 모니터의 연결지점에는 붙지 않는다 - 거기는 화면 끝이 아니라 통로다",
                    "바를 안쪽으로 끌거나 더블클릭하면 떨어진다 (우클릭 메뉴에도 있다)" },

            new[] { "0.54",
                    "\"n개 더\" 줄을 바로 끌어서 숨은 항목을 꺼낼 수 있다 — 카드 아래 가장자리까지 갈 필요가 없다" },

            new[] { "0.53",
                    "시세를 접으면 날씨처럼 가운데 정렬된 줄 하나만 남는다",
                    "큰 날씨 카드에서 휠을 굴리면 다른 지역으로 넘어간다" },

            new[] { "0.52",
                    "날씨도 목록 ↔ 하나 크게 전환 — 시세의 목록/타일 전환과 같은 도구 자리에 버튼",
                    "크게 볼 때는 아이콘과 온도를 키워 목록 세 줄쯤 되는 높이로 보여준다",
                    "크게 볼 지역은 이름 옆 캐럿으로 고른다 (추가·삭제·정렬은 목록 보기에서)" },

            new[] { "0.51",
                    "공지 줄을 편집 모드에서만 보이도록 변경 — 평소에는 자리도 차지하지 않는다",
                    "정보 창에 '로그' 버튼과 변경 내역 팝업 추가",
                    "정보 창에서 새 버전이 있으면 버전 옆에 같이 표시",
                    "앱 버전을 이 목록에서 끌어오도록 정리 — 버전과 기록이 어긋날 수 없다" },

            new[] { "0.50",
                    "새 버전 공지 줄 추가 — 하루 한 번 확인해 카드 맨 아래에 알린다" },

            new[] { "0.49",
                    "사내 프록시 뒤에서도 동작하도록 수정 — 인증을 요구하는 프록시에 Windows 계정으로 응답한다",
                    "회사 PC 설치 패키지(install.cmd) 추가" },

            new[] { "0.48",
                    "첫 공개 버전 — 시놀로지 저장소에 등록",
                    "정보 창 추가, 스크롤바를 얇고 어둡게" },

            new[] { "0.10 ~ 0.47",
                    "최초 개발 — 환율·주식·코인·금리·날씨·시계를 한 카드에",
                    "편집 모드 — 꾹 눌러 추가·삭제·정렬, Alt+Z 되돌리기",
                    "타일 보기(가로 2~10개), 섹션별 접기(접으면 조회도 멈춘다)",
                    "크기·투명도 조절, Windows 시작 시 자동 실행" },
        };

        /// <summary>
        /// 앱 버전. Changelog 맨 윗줄에서 가져온다.
        /// config.json 에도 기록되므로 지금 도는 위젯이 어느 버전인지 파일만 봐도 알 수 있다.
        /// </summary>
        public static readonly string AppVersion = Changelog[0][0];

        /// <summary>config.json 에 적혀 있던 버전 (없으면 null). 예전 설정을 식별할 때 쓴다.</summary>
        public string FileVersion;

        /// <summary>
        /// 한국은행 ECOS 오픈API 인증키. 금리·물가 같은 한국은행 통계를 쓰려면 필요하다.
        /// ecos.bok.or.kr → 오픈API 에서 무료로 발급받아 여기에 넣는다.
        /// 비워두면 금리 항목만 동작하지 않고 나머지는 그대로 돈다.
        /// </summary>
        public string EcosKey = null;

        /// <summary>
        /// 인증키를 밖에서 넣는다. 어디서 들어오든 같은 검사를 거치게 한다 -
        /// 이 값은 URL 경로에 그대로 붙으므로 영숫자만 통과시킨다.
        /// </summary>
        public void SetEcosKey(string v)
        {
            EcosKey = SafeApiKey(v);
        }

        // 위치
        public double Lat = double.NaN;
        public double Lon = double.NaN;
        public string City = null;
        public string WeatherAreaCode = null;   // 네이버 날씨 지역코드 (예: 01140640)

        // 창
        public double X = double.NaN;
        public double Y = double.NaN;
        public bool Topmost = true;
        public double Scale = DefaultScale;     // 0.80 ~ 1.80 (기본 1.20)
        public double Opacity = 1.0;            // 0.30 ~ 1.00
        public bool Minimized = false;          // 헤더 한 줄만 남기고 접은 상태
        public bool ShowClock = true;           // 날씨 아래 시계
        public bool ShowQuotes = true;          // 시세 섹션 (접으면 시세 조회를 멈춘다)
        public bool ShowWeather = true;         // 날씨 섹션 (접으면 날씨 조회를 멈춘다)

        // 표시 상태
        public string Symbol = null;            // 접었을 때 보여줄 종목의 Key
        public string Bank = "HANA";            // HANA | SHB
        public bool Expanded = false;
        public bool GridView = false;           // 펼침 모드에서 목록 대신 타일로
        public int ListLimit = 0;               // 목록에 보일 개수 (0 = 전부)
        public int GridColumns = 2;             // 타일 가로 개수 (2 ~ 10)

        // 날씨 보기 방식
        public bool WeatherBig = false;         // 지역 하나만 크게 (목록 세 줄쯤 되는 높이)
        public string WeatherMain = null;       // 크게 볼 지역의 Key. 없거나 지워졌으면 목록 첫 번째

        // 모니터 가장자리 도킹
        //   붙어 있는 동안에는 X/Y 가 바의 자리라, 떼었을 때 돌아갈 곳을 따로 기억해 둔다.
        public DockEdge DockedEdge = DockEdge.None;

        // 조각 창들도 각자 가장자리에 붙는다
        public DockEdge WeatherEdge = DockEdge.None;
        public DockEdge AppsEdge = DockEdge.None;
        public DockEdge ClockEdge = DockEdge.None;
        // 가장자리 바의 배율은 카드와 따로 기억한다. 쓰임새가 달라 같이 움직이면 곤란하다.
        public double DockScale = 1.0;
        public double UndockX = double.NaN, UndockY = double.NaN;

        // 급등·급락 알림
        //   SurgePercent 가 0 이면 종류별 기본값을 쓴다. 환율과 코인은 흔들리는 폭이
        //   자릿수부터 달라서 하나의 값으로는 쓸모가 없다.
        public bool SurgeAlert = true;
        public double SurgePercent = 0;
        // 가장자리 바에서 항목이 부풀기 시작하는 변동폭(%). 0 이면 기본값 5 를 쓴다.
        public double SurgeGrowPercent = 0;

        public const int MinColumns = 2;
        public const int MaxColumns = 10;

        /// <summary>표시할 종목 목록. 사용자가 추가·삭제할 수 있다.</summary>
        public List<SymbolDef> Symbols = new List<SymbolDef>();

        /// <summary>표시할 날씨 지역 목록. 시세와 따로 관리한다.</summary>
        public List<SymbolDef> Weathers = new List<SymbolDef>();

        // 즐겨찾기 (바로가기 .lnk 만)
        public bool ShowApps = true;

        // 완전히 닫힘. 접기(Show=false)와 다르다 -
        // 접으면 '펴기' 줄이 남지만 닫으면 그 자리조차 없어진다. 우클릭 메뉴로만 되살린다.
        public bool QuotesClosed, WeatherClosed, AppsClosed, ClockClosed;
        public List<AppDef> Apps = new List<AppDef>();

        /// <summary>
        /// 구분선이 들어가는 자리. "3번째 아이콘 앞에 선" 을 3 으로 적는다.
        ///
        /// ★ 구분선은 즐겨찾기 항목이 아니다 ★
        ///   Apps 안에 가짜 항목으로 끼워 넣지 않는다. 그러면 '.lnk 만 받는다' 는 검사를
        ///   선이 지나가야 하는데, 선은 파일이 아니다. 자리만 따로 적어 두면
        ///   Apps.IsAllowed 는 지금처럼 진짜 바로가기만 보면 된다.
        /// </summary>
        /// <summary>배경을 지운 바의 이름들("날씨"·"시계"). 즐겨찾기는 원래부터 지워져 있다.</summary>
        public List<string> ClearBars = new List<string>();

        /// <summary>
        /// 조각 바가 붙어 있던 모니터(\\.\DISPLAY1 형태).
        ///
        /// ★ 창 위치로 모니터를 되짚으면 안 된다 ★
        ///   저장된 좌표는 '떠 있을 때의 자리' 이기도 하다. 주 모니터에 떠 있다가 보조
        ///   모니터에 붙였으면, 다시 켤 때 그 좌표를 보고 **주 모니터에** 붙어 버린다.
        ///   실제로 날씨가 보조에 안 뜨고 주 모니터 왼쪽만 밀어내는 일이 있었다.
        /// </summary>
        public string WeatherDevice = null;
        public string AppsDevice = null;
        public string ClockDevice = null;

        public List<int> AppSeps = new List<int>();

        // 창 나누기 - 날씨·즐겨찾기·시계를 카드에서 떼어 각자 창으로 띄운다.
        // 떼어낸 창의 자리는 따로 기억한다. 비어 있으면 카드 옆에 알아서 놓는다.
        public bool Separated = false;
        public double WeatherX = double.NaN, WeatherY = double.NaN;
        public double AppsX = double.NaN, AppsY = double.NaN;
        public double ClockX = double.NaN, ClockY = double.NaN;

        // 조각 창별 배율. NaN 이면 카드 배율을 그대로 따른다.
        public double WeatherScale = double.NaN;
        public double AppsScale = double.NaN;
        public double ClockScale = double.NaN;

        public const int MaxSymbols = 24;

        // 갱신 주기(초)
        public int QuoteIntervalSec = 300;
        public int WeatherIntervalSec = 600;

        // 새 버전 알림
        //   updateUrl 이 비어 있으면 기능 자체가 꺼진다 (아무 것도 조회하지 않는다).
        //   기대하는 응답은 { "version": "0.50" } 하나뿐이고, 다른 키가 있어도 무시한다.
        public string UpdateUrl = null;
        public bool NotifyUpdate = true;

        // 0.15 에서 기준 레이아웃을 1/1.2 로 줄이고 기본 배율을 1.2 로 올렸다.
        // 화면에 보이는 크기는 그대로 두면서 100% 아래로도 줄일 수 있게 하기 위해서다.
        public const double DefaultScale = 1.2;
        /// <summary>조각 창 배율. 범위를 벗어나거나 값이 없으면 NaN(= 카드 배율 따르기).</summary>
        private static double PanelScale(double v)
        {
            if (double.IsNaN(v)) return double.NaN;
            if (v < MinScale || v > MaxScale) return double.NaN;
            return v;
        }

        public const double MinScale = 0.8;
        public const double MaxScale = 1.8;
        public const double MinOpacity = 0.3;   // 이보다 흐리면 클릭할 곳을 찾기 어렵다
        // 시세는 1초까지 내릴 수 있다. 다만 종목 수만큼 매 초 요청이 나가므로
        // 상시로 쓸 값은 아니다 (네이버·업비트 쪽에서 막힐 수 있다).
        public const int MinInterval = 1;
        // 날씨는 1분 아래로 내려갈 이유가 없다. 관측값이 그보다 자주 바뀌지 않는다.
        public const int MinWeatherInterval = 60;
        public const int MaxInterval = 21600;   // 6시간

        private readonly string _path;

        public Config(string path) { _path = path; }

        public string Path { get { return _path; } }

        /// <summary>
        /// 파일은 있는데 읽거나 해석하지 못한 경우 true.
        /// 이때 Save 를 하면 사용자의 기존 설정을 기본값으로 덮어쓰게 되므로 저장을 막는다.
        /// (로그온 직후 백신 검사나 클라우드 동기화 때문에 파일이 잠기는 일이 실제로 있다)
        /// </summary>
        public bool LoadFailed { get; private set; }

        public void Load()
        {
            LoadFailed = false;
            if (!File.Exists(_path)) return;   // 파일이 없는 건 정상 - 첫 실행이다

            try
            {
                string text = File.ReadAllText(_path, new UTF8Encoding(false));
                var j = Json.Parse(text);
                if (!j.Exists) { LoadFailed = true; return; }

                FileVersion = Sanitize(j["version"].S, 16);
                EcosKey = SafeApiKey(j["ecosKey"].S);
                Lat = Clamp(j["lat"].D, -90, 90, double.NaN);
                Lon = Clamp(j["lon"].D, -180, 180, double.NaN);
                City = Sanitize(j["city"].S, 24);
                WeatherAreaCode = Digits(j["weatherAreaCode"].S, 12);

                X = j["x"].D;
                Y = j["y"].D;
                if (j["topmost"].Exists) Topmost = j["topmost"].B;
                Scale = Clamp(j["scale"].D, MinScale, MaxScale, DefaultScale);

                // 0.15 이전 설정은 기준이 달랐다. 그대로 쓰면 위젯이 갑자기 작아지므로 환산한다.
                if (VersionOf(FileVersion) < 0.15 && j["scale"].Exists)
                    Scale = Clamp(Scale * DefaultScale, MinScale, MaxScale, DefaultScale);
                Opacity = Clamp(j["opacity"].D, MinOpacity, 1.0, 1.0);
                if (j["minimized"].Exists) Minimized = j["minimized"].B;
                if (j["showClock"].Exists) ShowClock = j["showClock"].B;
                if (j["showQuotes"].Exists) ShowQuotes = j["showQuotes"].B;
                if (j["showWeather"].Exists) ShowWeather = j["showWeather"].B;
                if (j["showApps"].Exists) ShowApps = j["showApps"].B;
                if (j["quotesClosed"].Exists) QuotesClosed = j["quotesClosed"].B;
                if (j["weatherClosed"].Exists) WeatherClosed = j["weatherClosed"].B;
                if (j["appsClosed"].Exists) AppsClosed = j["appsClosed"].B;
                if (j["clockClosed"].Exists) ClockClosed = j["clockClosed"].B;
                if (j["separated"].Exists) Separated = j["separated"].B;
                WeatherX = j["weatherX"].D; WeatherY = j["weatherY"].D;
                AppsX = j["appsX"].D;       AppsY = j["appsY"].D;
                ClockX = j["clockX"].D;     ClockY = j["clockY"].D;
                WeatherScale = PanelScale(j["weatherScale"].D);
                AppsScale = PanelScale(j["appsScale"].D);
                ClockScale = PanelScale(j["clockScale"].D);

                UpdateUrl = SafeHttpsUrl(j["updateUrl"].S);
                if (j["notifyUpdate"].Exists) NotifyUpdate = j["notifyUpdate"].B;

                Symbol = Sanitize(j["symbol"].S, 48);

                string bank = Sanitize(j["bank"].S, 8);
                if (bank == "HANA" || bank == "SHB") Bank = bank;

                if (j["expanded"].Exists) Expanded = j["expanded"].B;
                if (j["gridView"].Exists) GridView = j["gridView"].B;
                ListLimit = (int)Clamp(j["listLimit"].D, 0, MaxSymbols, 0);
                GridColumns = (int)Clamp(j["gridColumns"].D, MinColumns, MaxColumns, 2);
                if (j["weatherBig"].Exists) WeatherBig = j["weatherBig"].B;
                WeatherMain = Sanitize(j["weatherMain"].S, 48);
                DockedEdge = Dock.Parse(Sanitize(j["dockEdge"].S, 8));
                WeatherEdge = Dock.Parse(Sanitize(j["weatherEdge"].S, 8));
                AppsEdge = Dock.Parse(Sanitize(j["appsEdge"].S, 8));
                ClockEdge = Dock.Parse(Sanitize(j["clockEdge"].S, 8));
                DockScale = Clamp(j["dockScale"].D, MinScale, MaxScale, 1.0);
                if (j["surgeAlert"].Exists) SurgeAlert = j["surgeAlert"].B;
                SurgePercent = Clamp(j["surgePercent"].D, 0, 50, 0);
                SurgeGrowPercent = Clamp(j["surgeGrowPercent"].D, 0, 50, 0);
                UndockX = j["undockX"].D;
                UndockY = j["undockY"].D;

                LoadSymbols(j["symbols"]);
                LoadWeathers(j["weathers"]);
                LoadApps(j["apps"]);
                LoadAppSeps(j["appSeps"]);
                LoadClearBars(j["clearBars"]);

                WeatherDevice = SafeDevice(j["weatherDevice"].S);
                AppsDevice = SafeDevice(j["appsDevice"].S);
                ClockDevice = SafeDevice(j["clockDevice"].S);

                QuoteIntervalSec = (int)Clamp(j["quoteIntervalSec"].D, MinInterval, MaxInterval, 300);
                WeatherIntervalSec = (int)Clamp(j["weatherIntervalSec"].D, MinWeatherInterval, MaxInterval, 600);
            }
            catch
            {
                // 읽지 못했다. 기본값으로 동작하되 저장은 막는다.
                LoadFailed = true;
            }
            finally
            {
                if (Symbols.Count == 0) Symbols = Sources.Defaults();
                MigrateWeather();
            }
        }

        /// <summary>
        /// 0.31 이전에는 날씨가 lat/lon/city 한 벌뿐이었다. 그 설정을 목록의 첫 항목으로 옮긴다.
        /// (자동 감지 위치는 계속 lat/lon 에 남아 있고, 첫 실행 때 여기로 들어온다)
        /// </summary>
        private void MigrateWeather()
        {
            if (Weathers.Count > 0) return;
            if (double.IsNaN(Lat) || double.IsNaN(Lon)) return;

            var w = new SymbolDef(SourceKind.Weather, WeatherAreaCode ?? "", City ?? "현재 위치");
            w.Lat = Lat;
            w.Lon = Lon;
            Weathers.Add(w);
        }

        /// <summary>
        /// 즐겨찾기. 바로가기(.lnk) 가 아니거나 없어진 파일은 조용히 버린다.
        /// 설정 파일이 오염돼도 엉뚱한 것이 실행되지 않게 하는 첫 관문이다.
        /// </summary>
        /// <summary>구분선 자리. 앱 개수를 넘거나 겹치는 것은 버린다.</summary>
        /// <summary>
        /// 배경을 지운 바 이름들.
        /// 아는 이름만 받는다 - 설정이 오염돼도 엉뚱한 값이 들어오지 않게 한다.
        /// </summary>
        /// <summary>모니터 이름은 \\.\DISPLAYn 형태만 받는다. 설정이 오염돼도 엉뚱한 값이 안 들어오게.</summary>
        private static string SafeDevice(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s.Length > 40) return null;
            if (!s.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase)) return null;
            return s;
        }

        private void LoadClearBars(JNode arr)
        {
            ClearBars.Clear();
            if (arr == null || !arr.Exists) return;

            for (int i = 0; i < arr.Count && ClearBars.Count < 4; i++)
            {
                string v = arr[i].S;
                if (v != "날씨" && v != "시계") continue;
                if (ClearBars.Contains(v)) continue;
                ClearBars.Add(v);
            }
        }

        private void LoadAppSeps(JNode arr)
        {
            AppSeps.Clear();
            if (!arr.Exists) return;

            for (int i = 0; i < arr.Count && AppSeps.Count < DeskWidget.Apps.MaxApps + 1; i++)
            {
                double d = arr[i].D;
                if (double.IsNaN(d)) continue;

                int v = (int)Math.Round(d);
                if (v < 0 || v > Apps.Count) continue;      // 맨 앞(0)과 맨 뒤(Count)는 허용
                if (AppSeps.Contains(v)) continue;
                AppSeps.Add(v);
            }
            AppSeps.Sort();
        }

        private void LoadApps(JNode arr)
        {
            Apps.Clear();
            if (!arr.Exists) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < arr.Count && Apps.Count < DeskWidget.Apps.MaxApps; i++)
            {
                var it = arr[i];

                // 보관소 안의 파일 이름이 정식이다
                string file = Sanitize(it["file"].S, 120);
                string path = DeskWidget.Apps.PathOf(file);

                // 예전 설정은 바깥 경로를 가리킨다. 그런 것은 보관소로 들여오고 이름으로 바꾼다.
                if (path == null || !DeskWidget.Apps.IsAllowed(path))
                {
                    file = DeskWidget.Apps.Import(Sanitize(it["path"].S, 400));
                    path = DeskWidget.Apps.PathOf(file);
                }
                if (path == null || !DeskWidget.Apps.IsAllowed(path)) continue;

                string label = Sanitize(it["label"].S, 40);
                if (string.IsNullOrEmpty(label)) label = DeskWidget.Apps.NameOf(path);

                var def = new AppDef { Path = path, File = file, Label = label };
                if (!seen.Add(def.Key)) continue;   // 같은 바로가기 두 번은 안 받는다
                Apps.Add(def);
            }
        }

        private void LoadWeathers(JNode arr)
        {
            Weathers.Clear();
            if (!arr.Exists) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < arr.Count && Weathers.Count < MaxSymbols; i++)
            {
                var it = arr[i];
                string label = Sanitize(it["label"].S, 20);
                if (string.IsNullOrEmpty(label)) continue;

                string code = Digits(it["code"].S, 12) ?? "";
                double la = Clamp(it["lat"].D, -90, 90, double.NaN);
                double lo = Clamp(it["lon"].D, -180, 180, double.NaN);
                if (double.IsNaN(la) || double.IsNaN(lo)) continue;

                var def = new SymbolDef(SourceKind.Weather, code, label);
                def.Lat = la;
                def.Lon = lo;

                string key = label + "|" + la.ToString("0.###", CultureInfo.InvariantCulture)
                                   + "," + lo.ToString("0.###", CultureInfo.InvariantCulture);
                if (!seen.Add(key)) continue;
                Weathers.Add(def);
            }
        }

        private void LoadSymbols(JNode arr)
        {
            Symbols.Clear();
            if (!arr.Exists) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < arr.Count && Symbols.Count < MaxSymbols; i++)
            {
                var it = arr[i];
                SourceKind kind;
                if (!SymbolDef.TryParseKind(it["kind"].S, out kind)) continue;

                string code = Sanitize(it["code"].S, 64);
                string label = Sanitize(it["label"].S, 20);
                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(label)) continue;

                // ECOS 는 코드가 통계 항목 이름이라 한글이 들어간다. URL 경로에 넣지 않으므로 예외로 둔다.
                if (kind != SourceKind.Ecos && !IsSafeCode(code)) continue;

                var def = new SymbolDef(kind, code, label);
                if (kind == SourceKind.Weather)
                {
                    // 날씨는 좌표로 조회하므로 좌표가 없으면 쓸모가 없다
                    def.Lat = Clamp(it["lat"].D, -90, 90, double.NaN);
                    def.Lon = Clamp(it["lon"].D, -180, 180, double.NaN);
                    if (double.IsNaN(def.Lat) || double.IsNaN(def.Lon)) continue;
                }
                if (!seen.Add(def.Key)) continue;  // 중복 제거
                Symbols.Add(def);
            }
        }

        /// <summary>
        /// ECOS 인증키는 URL 경로에 그대로 붙는다.
        /// 설정 파일이 오염되면 경로가 통째로 바뀌므로 영숫자만 통과시킨다.
        /// </summary>
        private static string SafeApiKey(string s)
        {
            s = Sanitize(s, 64);
            if (string.IsNullOrEmpty(s)) return null;
            foreach (char c in s)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (!ok) return null;
            }
            return s;
        }

        /// <summary>종목 코드는 URL 경로/쿼리에 들어가므로 안전한 문자만 허용한다.</summary>
        public static bool IsSafeCode(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length > 32) return false;
            // 점 두 개는 URL 경로를 거슬러 올라갈 수 있다. 정상 종목 코드에는 나오지 않는다.
            if (s.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            foreach (char c in s)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                       || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';
                if (!ok) return false;
            }
            return true;
        }

        public void Save()
        {
            if (LoadFailed) return;   // 기존 설정을 날리지 않는다
            try
            {
                var sb = new StringBuilder(512);
                sb.Append("{\n");
                Str(sb, "version", AppVersion); sb.Append(",\n");
                Str(sb, "ecosKey", EcosKey); sb.Append(",\n");
                // lat/lon/city/weatherAreaCode 는 첫 실행 때 위치를 자동 감지해 담아두는 자리다.
                // weathers 목록이 만들어진 뒤에는 쓰이지 않으므로 저장하지 않는다.
                if (Weathers.Count == 0)
                {
                    Num(sb, "lat", Lat, 7); sb.Append(",\n");
                    Num(sb, "lon", Lon, 7); sb.Append(",\n");
                    Str(sb, "city", City); sb.Append(",\n");
                    Str(sb, "weatherAreaCode", WeatherAreaCode); sb.Append(",\n");
                }
                Num(sb, "x", X, 1); sb.Append(",\n");
                Num(sb, "y", Y, 1); sb.Append(",\n");
                sb.Append("  \"topmost\": ").Append(Topmost ? "true" : "false").Append(",\n");
                Num(sb, "scale", Scale, 3); sb.Append(",\n");
                Num(sb, "opacity", Opacity, 2); sb.Append(",\n");
                sb.Append("  \"minimized\": ").Append(Minimized ? "true" : "false").Append(",\n");
                sb.Append("  \"showClock\": ").Append(ShowClock ? "true" : "false").Append(",\n");
                sb.Append("  \"showQuotes\": ").Append(ShowQuotes ? "true" : "false").Append(",\n");
                sb.Append("  \"showWeather\": ").Append(ShowWeather ? "true" : "false").Append(",\n");
                sb.Append("  \"showApps\": ").Append(ShowApps ? "true" : "false").Append(",\n");
                sb.Append("  \"quotesClosed\": ").Append(QuotesClosed ? "true" : "false").Append(",\n");
                sb.Append("  \"weatherClosed\": ").Append(WeatherClosed ? "true" : "false").Append(",\n");
                sb.Append("  \"appsClosed\": ").Append(AppsClosed ? "true" : "false").Append(",\n");
                sb.Append("  \"clockClosed\": ").Append(ClockClosed ? "true" : "false").Append(",\n");
                sb.Append("  \"separated\": ").Append(Separated ? "true" : "false").Append(",\n");
                Num(sb, "weatherX", WeatherX, 1); sb.Append(",\n");
                Num(sb, "weatherY", WeatherY, 1); sb.Append(",\n");
                Num(sb, "appsX", AppsX, 1); sb.Append(",\n");
                Num(sb, "appsY", AppsY, 1); sb.Append(",\n");
                Num(sb, "clockX", ClockX, 1); sb.Append(",\n");
                Num(sb, "clockY", ClockY, 1); sb.Append(",\n");
                Num(sb, "weatherScale", WeatherScale, 3); sb.Append(",\n");
                Num(sb, "appsScale", AppsScale, 3); sb.Append(",\n");
                Num(sb, "clockScale", ClockScale, 3); sb.Append(",\n");
                Str(sb, "symbol", Symbol); sb.Append(",\n");
                Str(sb, "bank", Bank); sb.Append(",\n");
                sb.Append("  \"expanded\": ").Append(Expanded ? "true" : "false").Append(",\n");
                sb.Append("  \"gridView\": ").Append(GridView ? "true" : "false").Append(",\n");
                sb.Append("  \"listLimit\": ").Append(ListLimit).Append(",\n");
                sb.Append("  \"gridColumns\": ").Append(GridColumns).Append(",\n");
                sb.Append("  \"weatherBig\": ").Append(WeatherBig ? "true" : "false").Append(",\n");
                Str(sb, "weatherMain", WeatherMain); sb.Append(",\n");
                Str(sb, "dockEdge", Dock.Name(DockedEdge)); sb.Append(",\n");
                Str(sb, "weatherEdge", Dock.Name(WeatherEdge)); sb.Append(",\n");
                Str(sb, "appsEdge", Dock.Name(AppsEdge)); sb.Append(",\n");
                Str(sb, "clockEdge", Dock.Name(ClockEdge)); sb.Append(",\n");
                Num(sb, "dockScale", DockScale, 3); sb.Append(",\n");
                sb.Append("  \"surgeAlert\": ").Append(SurgeAlert ? "true" : "false").Append(",\n");
                Num(sb, "surgePercent", SurgePercent, 2); sb.Append(",\n");
                Num(sb, "surgeGrowPercent", SurgeGrowPercent, 2); sb.Append(",\n");
                Num(sb, "undockX", UndockX, 1); sb.Append(",\n");
                Num(sb, "undockY", UndockY, 1); sb.Append(",\n");
                sb.Append("  \"quoteIntervalSec\": ").Append(QuoteIntervalSec).Append(",\n");
                sb.Append("  \"weatherIntervalSec\": ").Append(WeatherIntervalSec).Append(",\n");
                Str(sb, "updateUrl", UpdateUrl); sb.Append(",\n");
                sb.Append("  \"notifyUpdate\": ").Append(NotifyUpdate ? "true" : "false").Append(",\n");

                sb.Append("  \"symbols\": [\n");
                for (int i = 0; i < Symbols.Count; i++)
                {
                    var s = Symbols[i];
                    sb.Append("    { \"kind\": \"").Append(SymbolDef.KindName(s.Kind))
                      .Append("\", \"code\": \"").Append(Json.Escape(s.Code))
                      .Append("\", \"label\": \"").Append(Json.Escape(s.Label)).Append('"');
                    if (s.Kind == SourceKind.Weather && !double.IsNaN(s.Lat) && !double.IsNaN(s.Lon))
                    {
                        sb.Append(", \"lat\": ").Append(Math.Round(s.Lat, 5).ToString(CultureInfo.InvariantCulture))
                          .Append(", \"lon\": ").Append(Math.Round(s.Lon, 5).ToString(CultureInfo.InvariantCulture));
                    }
                    sb.Append(" }");
                    if (i < Symbols.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append("  ],\n");

                sb.Append("  \"weathers\": [\n");
                for (int i = 0; i < Weathers.Count; i++)
                {
                    var w = Weathers[i];
                    sb.Append("    { \"code\": \"").Append(Json.Escape(w.Code ?? ""))
                      .Append("\", \"label\": \"").Append(Json.Escape(w.Label))
                      .Append("\", \"lat\": ").Append(Math.Round(w.Lat, 5).ToString(CultureInfo.InvariantCulture))
                      .Append(", \"lon\": ").Append(Math.Round(w.Lon, 5).ToString(CultureInfo.InvariantCulture))
                      .Append(" }");
                    if (i < Weathers.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append("  ],\n");

                Str(sb, "weatherDevice", WeatherDevice); sb.Append(",\n");
                Str(sb, "appsDevice", AppsDevice); sb.Append(",\n");
                Str(sb, "clockDevice", ClockDevice); sb.Append(",\n");

                sb.Append("  \"clearBars\": [");
                for (int i = 0; i < ClearBars.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append('"').Append(Json.Escape(ClearBars[i])).Append('"');
                }
                sb.Append("],\n");

                sb.Append("  \"appSeps\": [");
                for (int i = 0; i < AppSeps.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(AppSeps[i]);
                }
                sb.Append("],\n");

                sb.Append("  \"apps\": [\n");
                for (int i = 0; i < Apps.Count; i++)
                {
                    var a = Apps[i];
                    // 보관소 안의 이름만 남긴다. 전체 경로를 적으면 폴더를 옮겼을 때 끊긴다.
                    sb.Append("    { \"file\": \"").Append(Json.Escape(a.File ?? ""))
                      .Append("\", \"label\": \"").Append(Json.Escape(a.Label ?? ""))
                      .Append("\" }");
                    if (i < Apps.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append("  ]\n");
                sb.Append("}\n");

                var dir = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 임시 파일에 먼저 쓴 뒤 교체한다.
                // Delete 후 Move 는 그 사이에 파일이 사라지는 창이 있어 원자적이지 않다.
                // File.Replace 는 같은 볼륨에서 원자적으로 바꿔치기한다.
                // BOM 을 붙여 저장한다 - 메모장/PowerShell 등 어떤 도구로 열어도 한글이 깨지지 않는다
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
            }
            catch
            {
                // 저장 실패는 무시 (읽기 전용 폴더 등)
            }
        }

        private static void Num(StringBuilder sb, string k, double v, int digits)
        {
            sb.Append("  \"").Append(k).Append("\": ");
            if (double.IsNaN(v) || double.IsInfinity(v)) sb.Append("null");
            else sb.Append(Math.Round(v, digits).ToString(CultureInfo.InvariantCulture));
        }

        private static void Str(StringBuilder sb, string k, string v)
        {
            sb.Append("  \"").Append(k).Append("\": ");
            if (v == null) sb.Append("null");
            else sb.Append('"').Append(Json.Escape(v)).Append('"');
        }

        /// <summary>"0.14" 같은 버전 문자열을 숫자로. 못 읽으면 0 (아주 옛날 설정으로 간주).</summary>
        private static double VersionOf(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double v;
            if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        private static double Clamp(double v, double lo, double hi, double fallback)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return fallback;
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        /// <summary>표시용 문자열 정리 - 길이 제한 + 제어문자 제거.</summary>
        /// <summary>
        /// https 주소만 통과시킨다. 설정 파일이 오염되더라도 엉뚱한 곳을 부르지 않게 한 겹 더 두는 것으로,
        /// Net.GetJsonAsync 안에도 같은 검사가 있다.
        /// </summary>
        private static string SafeHttpsUrl(string s)
        {
            s = Sanitize(s, 300);
            if (string.IsNullOrEmpty(s)) return null;
            Uri u;
            if (!Uri.TryCreate(s, UriKind.Absolute, out u)) return null;
            if (u.Scheme != Uri.UriSchemeHttps) return null;
            if (!string.IsNullOrEmpty(u.UserInfo)) return null;   // user@host 형태 차단
            return s;
        }

        /// <summary>받은 버전이 지금 도는 버전보다 높은가. 0.49 -> 0.50 처럼 소수 두 자리 기준이다.</summary>
        public static bool IsNewer(string version)
        {
            double v = VersionOf(version);
            if (v <= 0) return false;
            // 부동소수 오차로 같은 버전이 새 것처럼 보이지 않게 여유를 둔다
            return v > VersionOf(AppVersion) + 0.0001;
        }
        private static string Sanitize(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var sb = new StringBuilder(Math.Min(s.Length, maxLen));
            foreach (char c in s)
            {
                if (c < ' ') continue;
                sb.Append(c);
                if (sb.Length >= maxLen) break;
            }
            string r = sb.ToString().Trim();
            return r.Length == 0 ? null : r;
        }

        /// <summary>숫자만 남긴다. URL에 끼워 넣는 값이므로 엄격하게 거른다.</summary>
        private static string Digits(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var sb = new StringBuilder(maxLen);
            foreach (char c in s)
            {
                if (c < '0' || c > '9') continue;
                sb.Append(c);
                if (sb.Length >= maxLen) break;
            }
            return sb.Length == 0 ? null : sb.ToString();
        }
    }
}



































