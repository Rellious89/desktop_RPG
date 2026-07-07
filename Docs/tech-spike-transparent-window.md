# 기술 스파이크 1: 투명 창 + 모니터 전체 배치 세팅 가이드

`Assets/Scripts/DesktopWindow/` 스크립트를 실제로 씬에 적용하는 절차. **Win32 API 기반이라 Windows 빌드에서만 동작**하며,
**Editor Play 모드에서는 창 변형이 적용되지 않는다** (`TransparentWindowController`가 플랫폼/에디터 여부를 자동 감지해 스킵함).

> 참고 게임: [데스크탑 아기 동물 목장(Steam)](https://store.steampowered.com/app/3167550) — 화면 하단을 작업 배경으로 쓰는 구조.
> 이 프로젝트는 한 걸음 더 나아가 **하단(건물/마을)뿐 아니라 화면 상단(UI, 이펙트)까지 모니터 전체를 게임 영역으로 사용**하는 것을 목표로 한다.
> 그래서 창을 작은 팝업이 아니라 **주 모니터 전체 크기의 투명 오버레이**로 만든다.

## 개발 환경이 macOS일 때

- 개발은 macOS(Unity 2022.3.62f3)에서 하고, 이 기능 검증은 실제 Windows 머신에서 빌드 실행으로 진행하는 흐름을 전제로 한다.
- macOS에서 Play 모드로 돌리거나 macOS용으로 빌드해도 크래시 없이 "Windows 전용 기능" 경고 로그만 남기고 기본 창으로 정상 실행된다(`UNITY_STANDALONE_WIN`으로 가드).
- **Mac 버전 동시 출시를 고려 중이라면**: user32.dll/dwmapi.dll에 대응하는 범용 API가 macOS엔 없어서, 동일한 투명/보더리스/클릭관통 효과를 내려면 Objective-C/Swift로 별도의 네이티브 플러그인(.bundle)을 만들어 Unity가 사용하는 NSWindow에 접근해야 한다. 글로벌 키보드 후킹도 macOS는 손쉬운 후킹이 아니라 접근성(Accessibility) 권한 승인 UX가 추가로 필요하다. 이 부분은 별도의 기술 스파이크로 분리해서 우선순위를 정할 것.

## 준비된 스크립트

| 스크립트 | 역할 |
|---|---|
| `Win32Interop.cs` | user32.dll / dwmapi.dll P/Invoke 선언 (내부 전용, 직접 사용 안 함) |
| `TransparentWindowController.cs` | 테두리 제거, 투명 창 처리, 주 모니터 전체 배치, 클릭-관통 판정 |
| `FpsLimiter.cs` | 30fps 고정 + VSync 끄기 |
| `AutoFrameTarget.cs` | 더미 오브젝트 바운드에 맞춰 카메라 자동 프레이밍 (수직 앵커로 하단/중앙/상단 배치 조절) |

## 1단계 — 씬 오브젝트 구성

1. Hierarchy에서 빈 GameObject 생성 → 이름을 `DesktopStage`로 변경.
2. `DesktopStage`에 `Transparent Window Controller`와 `Fps Limiter` 컴포넌트를 추가.
3. Inspector에서 `Transparent Window Controller`의 `Target Camera`가 비어 있으면 `Awake()`에서 `Camera.main`을 자동으로 잡으므로 드래그하지 않아도 됨. 다만 씬에 카메라가 둘 이상이면 명시적으로 드래그해서 지정.

## 2단계 — 카메라 & 더미 오브젝트

1. `Main Camera`에 `Auto Frame Target` 컴포넌트 추가 (`[RequireComponent(typeof(Camera))]`라 자동으로 Camera를 찾음).
2. 확보한 더미 3D 에셋(Cube든 캐릭터 프리팹이든)을 씬에 배치하고, `Auto Frame Target`의 `Target` 필드에 드래그.
   - 여러 개(허수아비 + 건물 등)를 한 번에 프레이밍하려면 빈 GameObject로 묶어서 그 부모를 `Target`에 지정 — `GetComponentsInChildren<Renderer>`로 전체 바운드를 계산하므로 자식이 늘어나도 그대로 재사용 가능.
3. `Vertical Anchor` 값으로 타겟을 화면 세로축 어디에 둘지 조절 (0 = 하단, 0.5 = 중앙, 1 = 상단). **하단은 마을/건물, 상단은 UI로 비워둘 계획이므로 0~0.2 사이 값을 권장.**
4. 더미 오브젝트에 **Collider(Box/Capsule 등)를 반드시 추가**할 것 — 클릭-관통 판정이 `Physics.Raycast` 기반이라 Collider가 없으면 항상 클릭이 관통된다.
5. Camera의 `Clear Flags` / `Background` 알파는 `TransparentWindowController`가 런타임에 자동으로 `Solid Color, Alpha 0`으로 세팅하므로 수동 설정은 필요 없음(에디터 미리보기용으로 미리 맞춰둬도 무방).

## 3단계 — Player Settings (File > Build Settings > Player Settings)

- **Resolution and Presentation**
  - Fullscreen Mode: `Windowed` (Fullscreen Window/전체화면 계열 금지 — DWM 합성이 깨짐)
  - Resizable Window: 체크 해제
  - Run In Background: **체크 필수** (창이 비활성 상태에서도 렌더링/로직이 돌아야 함)
- **Other Settings**
  - Graphics API: `Direct3D11`을 최상단으로 (DWM 투명 트릭 검증이 가장 많이 된 조합)
  - Splash Screen 사용 안 함(가능한 라이선스라면 체크 해제) — 시작 시 잠깐이라도 불투명한 검은 창이 뜨는 것을 방지

`TransparentWindowController`는 실행 시점에 `GetSystemMetrics`로 주 모니터 해상도를 읽어 `SetWindowPos`로 창을 `(0, 0)`부터 모니터 전체 크기로 강제 리사이즈한다. 그래서 Player Settings의 기본 해상도가 모니터 해상도와 달라도 상관없지만, 초기 프레임 깜빡임을 줄이려면 기본 해상도도 모니터 해상도에 맞춰두는 것을 권장.

## 4단계 — 빌드 및 검증

1. `File > Build Settings > Build` 로 Windows 실행 파일 생성 (에디터에서 Play 버튼으로는 검증 불가).
2. 실행 후 확인 사항:
   - 창 테두리/타이틀바가 없는지
   - 창이 모니터 전체를 덮고 있는지(작업 표시줄 위까지 포함해서 `HWND_TOPMOST`로 항상 위에 떠 있는지)
   - 더미 오브젝트가 없는 투명 영역(화면 대부분)을 클릭했을 때 뒤에 있는 바탕화면/다른 창이 클릭되는지 (관통)
   - 더미 오브젝트 위에서는 클릭이 이 창에서 캡처되는지
   - 다른 프로그램(에디터, 브라우저 등) 위에서 작업할 때 화면 대부분이 실제로 방해되지 않는지

## 알려진 제약 / 다음 단계로 미룬 것

- DWM(데스크톱 창 관리자) 합성이 꺼져 있으면(레거시 환경) 투명 효과가 동작하지 않음 — Win10/11 기본값에서는 문제 없음.
- 멀티 모니터 + 서로 다른 DPI 배율 조합은 `GetSystemMetrics` 좌표 계산에 오차가 생길 수 있음. 지금은 주 모니터 기준으로만 커버하고, 다른 모니터로 확장할지는 별도 스파이크로 보완.
- 창에 포커스를 줄지(`WS_EX_NOACTIVATE` 적용 여부)는 아직 결정하지 않음 — 캐릭터/건물 클릭 시 사용자의 작업 창 포커스를 뺏을지 여부는 UX 결정 후 반영.
- 화면 전체가 창이 되므로 매 프레임 클릭-관통 판정(`GetCursorPos` + UI/3D 레이캐스트)이 돌아간다. 지금은 30fps 제한과 가벼운 씬 기준으로 문제 없지만, 건물/UI가 많아지면 레이캐스트 대상 레이어를 좁히거나 판정 주기를 낮추는 최적화가 필요할 수 있음.
- 마을 건물처럼 좌우로 넓게 배치되는 콘텐츠는 `AutoFrameTarget`이 자식 렌더러 전체를 한 번에 프레이밍하지만, 카메라가 뒤로 빠지는 만큼 개별 오브젝트가 작아진다. 건물 수가 늘어나면 카메라를 고정 프레이밍 대신 스크롤/줌 가능한 방식으로 바꿀지 여부는 실제 에셋이 들어온 뒤 판단.
