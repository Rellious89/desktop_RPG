# 기술 스파이크 1: 투명 창 + 우하단 배치 세팅 가이드

`Assets/Scripts/DesktopWindow/` 스크립트를 실제로 씬에 적용하는 절차. Windows 스탠드얼론 빌드에서만 동작하며,
**Editor Play 모드에서는 창 변형이 적용되지 않는다** (`TransparentWindowController`가 자동으로 감지해 스킵함).

## 준비된 스크립트

| 스크립트 | 역할 |
|---|---|
| `Win32Interop.cs` | user32.dll / dwmapi.dll P/Invoke 선언 (내부 전용, 직접 사용 안 함) |
| `TransparentWindowController.cs` | 테두리 제거, 투명 창 처리, 우하단 배치, 클릭-관통 판정 |
| `FpsLimiter.cs` | 30fps 고정 + VSync 끄기 |
| `AutoFrameTarget.cs` | 더미 오브젝트 바운드에 맞춰 카메라 자동 프레이밍 |

## 1단계 — 씬 오브젝트 구성

1. Hierarchy에서 빈 GameObject 생성 → 이름을 `DesktopStage`로 변경.
2. `DesktopStage`에 `Transparent Window Controller`와 `Fps Limiter` 컴포넌트를 추가.
3. Inspector에서 `Transparent Window Controller`의 `Target Camera`가 비어 있으면 `Awake()`에서 `Camera.main`을 자동으로 잡으므로 드래그하지 않아도 됨. 다만 씬에 카메라가 둘 이상이면 명시적으로 드래그해서 지정.

## 2단계 — 카메라 & 더미 오브젝트

1. `Main Camera`에 `Auto Frame Target` 컴포넌트 추가 (`[RequireComponent(typeof(Camera))]`라 자동으로 Camera를 찾음).
2. 확보한 더미 3D 에셋(Cube든 캐릭터 프리팹이든)을 씬에 배치하고, `Auto Frame Target`의 `Target` 필드에 드래그.
3. 더미 오브젝트에 **Collider(Box/Capsule 등)를 반드시 추가**할 것 — 클릭-관통 판정이 `Physics.Raycast` 기반이라 Collider가 없으면 항상 클릭이 관통된다.
4. Camera의 `Clear Flags` / `Background` 알파는 `TransparentWindowController`가 런타임에 자동으로 `Solid Color, Alpha 0`으로 세팅하므로 수동 설정은 필요 없음(에디터 미리보기용으로 미리 맞춰둬도 무방).

## 3단계 — Player Settings (File > Build Settings > Player Settings)

- **Resolution and Presentation**
  - Fullscreen Mode: `Windowed` (Fullscreen Window/전체화면 계열 금지 — DWM 합성이 깨짐)
  - Resizable Window: 체크 해제
  - Run In Background: **체크 필수** (창이 비활성 상태에서도 렌더링/로직이 돌아야 함)
- **Other Settings**
  - Graphics API: `Direct3D11`을 최상단으로 (DWM 투명 트릭 검증이 가장 많이 된 조합)
  - Splash Screen 사용 안 함(가능한 라이선스라면 체크 해제) — 시작 시 잠깐이라도 불투명한 검은 창이 뜨는 것을 방지

`TransparentWindowController`의 `Window Width/Height` 필드(기본 480x480)는 실행 중 `SetWindowPos`로 강제 리사이즈하므로 Player Settings의 기본 해상도와 정확히 일치하지 않아도 되지만, 초기 프레임 깜빡임을 줄이려면 맞춰두는 것을 권장.

## 4단계 — 빌드 및 검증

1. `File > Build Settings > Build` 로 Windows 실행 파일 생성 (에디터에서 Play 버튼으로는 검증 불가).
2. 실행 후 확인 사항:
   - 창 테두리/타이틀바가 없는지
   - 모니터 우하단 구석에 창이 붙어 있는지
   - 더미 오브젝트가 없는 투명 영역을 클릭했을 때 뒤에 있는 바탕화면/다른 창이 클릭되는지 (관통)
   - 더미 오브젝트 위에서는 클릭이 이 창에서 캡처되는지

## 알려진 제약 / 다음 단계로 미룬 것

- DWM(데스크톱 창 관리자) 합성이 꺼져 있으면(레거시 환경) 투명 효과가 동작하지 않음 — Win10/11 기본값에서는 문제 없음.
- 멀티 모니터 + 서로 다른 DPI 배율 조합은 `GetSystemMetrics` 좌표 계산에 오차가 생길 수 있음. 프로토타입 단계에서는 주 모니터 기준으로만 검증하고, 실제 배포 전 별도 스파이크로 보완.
- 창에 포커스를 줄지(`WS_EX_NOACTIVATE` 적용 여부)는 아직 결정하지 않음 — 캐릭터 클릭 시 사용자의 작업 창 포커스를 뺏을지 여부는 UX 결정 후 반영.
