# 작업 지시서: 키 입력 시 마우스 끊김 — 메시지 펌프 결합 제거

작성: 2026-07-15 (Fable 5 분석) / 실행 담당: Sonnet 5

## 1. 배경

Windows 빌드에서 키 입력마다 시스템 전체 마우스가 멈칫거리는 문제가 있다.
지금까지 시도한 것과 결과:

| 시도 | 결과 |
|---|---|
| GC 할당 감소 (데미지 숫자 풀링, HitEffect 스폰 제한, WaitForSeconds 캐싱, 훅 콜백 마샬링 제거) | 체감 변화 없음 |
| 진단 빌드 A: 기본 상태 | 끊김 발생 |
| 진단 빌드 B: `GlobalKeyboardHook.useGlobalHook = false` (전역 훅 미설치) | 끊김 동일 |
| 진단 빌드 C: `TransparentWindowController.useHitTestSubclass = false` (WM_NCHITTEST 서브클래싱 미설치) | 끊김 동일 |

## 2. 재분석 — 세 빌드의 공통 분모

세 빌드 모두에서 바뀌지 않은 것이 진짜 원인일 가능성이 높다. 코드 재점검 결과:

1. **`FpsLimiter`: `vSyncCount = 0` + `Application.targetFrameRate = 30`**
   Unity 스탠드얼론은 윈도우 메시지 펌프를 **프레임당 1회** 돌린다. 30fps면 이 앱의
   메시지 응답 간격이 기본 ~33ms이고, 공격/이펙트로 프레임이 튀면 더 길어진다.

2. **`WH_KEYBOARD_LL` 훅이 Unity 메인 스레드에 설치되어 있다.**
   저수준 키보드 훅 콜백은 "훅을 설치한 스레드의 메시지 루프"를 통해 실행된다.
   콜백 본문이 아무리 가벼워도, **콜백이 실행 시작되기까지 시스템 전체의 해당 키 입력이
   우리 앱의 펌프 간격만큼 블록**된다. Windows는 입력 이벤트를 직렬로 처리하므로
   키 입력이 훅에서 대기하는 동안 마우스 이동/클릭 디스패치도 함께 밀린다.
   → "키 입력마다 마우스 멈칫"과 정확히 일치하는 메커니즘.

3. **클릭 관통이 전적으로 `WM_NCHITTEST` 응답에 의존한다 (`WS_EX_TRANSPARENT` 미사용).**
   커서가 우리 창 영역 위를 지날 때마다 OS가 히트테스트를 우리 창(=30fps 펌프)에 묻고
   응답을 기다린다. 서브클래싱을 꺼도(빌드 C) 창이 히트테스트 대상인 것 자체는 그대로다.
   `WS_EX_TRANSPARENT`가 켜져 있으면 OS가 **창에 묻지 않고** 즉시 통과시킨다.

### 왜 진단 빌드 B에서도 끊겼는가에 대한 해석

- 훅을 꺼도 (2)의 30fps 펌프와 (3)의 히트테스트 경로는 남아 있었다.
- 또한 빌드 B에서 훅이 실제로 꺼졌는지 로그로 검증되지 않았다(Inspector 토글은 씬 저장
  여부에 따라 빌드에 반영 안 될 수 있음). 아래 3.0의 로그 확인을 반드시 수행할 것.

## 3. 작업 내용

### 3.0 (필수 선행) 진단 결과 검증 절차를 결과 보고에 포함

수정 후 테스트 시 아래를 함께 확인해 보고한다.

- Player.log 위치: `%USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>\Player.log`
- 진단 모드가 켜졌다면 다음 경고가 찍힌다(안 찍혔으면 토글이 빌드에 반영 안 된 것):
  - `[GlobalKeyboardHook] useGlobalHook이 꺼져 있어...`
  - `[TransparentWindowController] useHitTestSubclass가 꺼져 있어...`
- 대조 실험: **KeyBuddy 미실행 상태**에서 타이핑+마우스 → 끊김 없음을 확인(베이스라인).
- 커서가 KeyBuddy 창 **위에 있을 때 / 창에서 멀 때** 각각 타이핑해서 차이를 기록.

### 3.1 전역 키보드 훅을 전용 스레드로 분리 (핵심)

대상: `Assets/Scripts/DesktopWindow/GlobalKeyboardHook.cs`

- `SetWindowsHookEx(WH_KEYBOARD_LL, ...)`를 **백그라운드 전용 스레드에서 설치**하고,
  그 스레드는 `GetMessage` 루프만 돈다(저수준 훅은 설치 스레드가 메시지를 펌프해야
  콜백이 디스패치된다). 이러면 콜백 지연이 Unity 프레임레이트와 완전히 분리된다.
- 콜백에서는 지금처럼 플래그만 세팅하되, 스레드 경계를 넘으므로:
  - `AnyKeyDownThisFrame`/`ExcludedKeyDownThisFrame`을 직접 세팅하지 말고,
    내부 카운터/플래그를 `Interlocked`로 올리고 메인 스레드의 `Update`(기존
    `LateUpdate` 리셋 로직 조정)에서 `Interlocked.Exchange`로 읽어 프레임 값으로 변환.
  - 콜백 안에서 Unity API 호출 금지(현재도 없음 — 유지).
  - `ExcludedKey`(KeyCode)는 훅 스레드에서 읽으므로 `KeyCodeToVirtualKey` 변환값을
    메인 스레드에서 미리 계산해 `volatile int`로 캐싱해두고 콜백은 int 비교만 한다.
- 정리(OnDisable/종료) 시: `PostThreadMessage(threadId, WM_QUIT, ...)`로 루프를 끝내고
  `UnhookWindowsHookEx`는 **훅 스레드 안에서**(루프 탈출 직후) 호출한 뒤 `Thread.Join`.
- 델리게이트 인스턴스는 필드로 rooted 유지(GC 수거 방지 — 기존 주석 규칙 동일).
- 훅 스레드는 `IsBackground = true`.
- 에디터/비Windows 경로(Input.anyKeyDown 폴백)와 `useGlobalHook` 진단 토글은 유지.
- 필요한 P/Invoke 추가는 `Win32Interop.cs`에: `GetMessage`, `PostThreadMessage`,
  `GetCurrentThreadId`, `WM_QUIT` 상수 등.

### 3.2 클릭 관통을 OS 레벨(WS_EX_TRANSPARENT)로 복원 + Dock 영역만 동적 해제

대상: `Assets/Scripts/DesktopWindow/TransparentWindowController.cs`

현재의 WM_NCHITTEST 서브클래싱 방식을 다음 하이브리드로 교체한다:

- 기본 상태: `WS_EX_TRANSPARENT` **항상 ON** → 창 전체가 OS 레벨 클릭 관통.
  커서가 창 위를 지나도 우리 창에 히트테스트를 묻지 않으므로 지연 자체가 없다.
- `Update()`에서 `GetCursorPos`로 커서의 화면 좌표를 읽고, 기존
  `RecomputeControlDockScreenRect()`가 계산하는 Dock 화면 사각형과 비교한다:
  - 커서가 Dock 사각형 **안**이면 `WS_EX_TRANSPARENT` OFF (클릭 가능)
  - **밖**이면 다시 ON
  - 상태를 캐싱해서 **변할 때만** `SetWindowLong` 호출(매 프레임 호출 금지).
- 커서 진입 반응이 최대 1프레임(~33ms) 늦는 것은 버튼 클릭 UX상 허용 범위.
- `WM_NCHITTEST` 서브클래싱(`InstallHitTestSubclass`/`CustomWndProc` 등)은 제거한다.
  (`useHitTestSubclass` 진단 토글도 함께 제거. 단 `SetWindowLongPtr`/`CallWindowProc`
  P/Invoke는 다른 용도가 없으면 정리, 있으면 유지.)
- F9 배치 모드는 "isPlacementMode 동안 `WS_EX_TRANSPARENT` 강제 OFF"로 단순화한다.
  모드 종료 시 커서 위치 기준으로 즉시 재평가.
- MoveHandle 드래그(`BeginManualDrag` + `GetAsyncKeyState` 폴링)와 위치/크기 저장,
  tgl_size 리사이즈, 시작 배치 규칙은 그대로 유지한다.

### 3.3 FpsLimiter 보완 (보조)

- 3.1/3.2로 입력 경로가 프레임레이트와 분리되므로 30fps 유지가 원칙이지만,
  검증 편의를 위해 `FpsLimiter`의 `targetFrameRate`를 Inspector에서 60으로 바꾼
  비교 빌드도 한 번 테스트하도록 보고 항목에 포함한다(코드 변경 불필요, 값만).

## 4. 금지/주의 사항

- 훅 콜백과 훅 스레드에서 Unity API(Time, Debug.Log 포함) 호출 금지.
  (Debug.Log가 필요하면 메인 스레드에서 플래그를 보고 출력.)
- 공격 큐/연속 공격 규칙, ExcludedKey(F9가 공격으로 새지 않는 규칙),
  ControlDock 버튼 동작, 창 위치/크기 저장 규칙은 기존 동작 그대로.
- 새 기능 추가 금지. 이 지시서 범위만 수행.

## 5. 완료 기준

1. 30fps 설정 그대로에서, 키 연타 중에도 시스템 마우스가 끊기지 않는다.
2. 커서를 KeyBuddy 창 위에 올려두고 타이핑해도 끊기지 않는다.
3. 투명 영역 클릭이 뒤 프로그램에 즉시 전달된다(지연 체감 없음).
4. ControlDock 버튼 4종(Move/Sound/HUD/Size)이 정상 클릭된다.
5. MoveHandle 드래그·위치 저장·F9 배치 모드·크기 토글이 기존대로 동작한다.
6. 앱 종료/재실행을 반복해도 훅 스레드가 정상 정리된다(Player.log에 예외 없음).
7. 결과 보고에 3.0의 검증 절차 결과(로그 확인 포함)를 명시한다.
