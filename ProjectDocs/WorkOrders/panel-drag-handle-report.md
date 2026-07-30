# 패널 타이틀 드래그 이동 완료 보고서

작업일: 2026-07-30
대상: `pn_CharacterSwap`, `pn_Inventory` (두 패널 모두 프리팹)

씬과 프리팹 파일은 건드리지 않았다(Unity 에디터가 프로젝트 락을 잡고 있다). 컴포넌트를 붙이는
작업은 6장 체크리스트로 넘긴다.

## 1. 구현한 것

`Assets/Scripts/Common/PanelDragHandle.cs` (신규) 하나뿐이다. 두 패널이 같은 컴포넌트를 붙여 쓰고,
패널별 드래그 코드는 만들지 않았다. 정적 인스턴스나 특정 패널 전제 없이 동작한다.

`ModalPanel`, `InputBlocker`, 열기/닫기, 클릭 관통 등록은 **한 줄도 바꾸지 않았다.** 드래그 핸들은
`ModalPanel`을 참조하지도 않으므로, 모달이 아닌 UI에도 그대로 붙일 수 있다.

## 2. 에디터에서 확인한 실제 구조

두 프리팹의 상단 구조가 동일했다.

```text
pn_XXX               (600x400, 앵커 0.5/0.5, 부모는 Panel_UI[전체 스트레치])
└ bg                 (Vertical Layout Group)
  └ top              (가로 스트레치, 높이 72, 피벗 위)
    ├ bg_top         (가로 스트레치 -84, 높이 48, Image RaycastTarget=ON)   ← 드래그 핸들
    │ └ lb_title     (TMP RaycastTarget=ON)
    └ btn_close      (오른쪽 앵커 48x48)
```

`bg_top`의 Raycast Target이 이미 켜져 있어 포인터를 받는다. **Raycast Target 설정을 바꿀 필요는 없었다.**

## 3. 닫기 버튼이 제외되는 이유 (설정이 아니라 구조로 보장)

Unity EventSystem은 드래그를 처리할 대상을 "포인터가 누른 오브젝트에서 <b>위로 올라가며</b> 만나는 첫
`IDragHandler`"로 정한다.

- `lb_title`(핸들의 **자식**)을 눌러도 위로 올라가 `bg_top`의 핸들을 만나므로 드래그된다.
  → 장식 텍스트의 Raycast Target을 끌 필요가 없다.
- `btn_close`는 `bg_top`의 **형제**라 위로 올라가도 이 핸들을 만나지 않는다(`top` → `bg` → 패널 →
  Panel_UI 어디에도 IDragHandler가 없다).
  → 닫기 버튼을 끌어도 패널이 움직이지 않고, 클릭은 지금까지대로 동작한다.
- 캐릭터 리스트/교체 버튼/인벤토리 슬롯은 `bg` 아래라 마찬가지로 드래그와 무관하다.
  캐릭터 리스트의 ScrollRect 드래그(세로 스크롤)도 그대로다.

즉 "닫기 버튼 제외"를 코드에서 예외 처리하지 않고, 핸들을 `bg_top`에 두는 것만으로 보장된다.

## 4. 좌표 처리와 이동 규칙

```csharp
RectTransformUtility.ScreenPointToLocalPointInRectangle(
    parentRect, eventData.position, eventData.pressEventCamera, out localPoint);
```

- 기준 좌표계는 **패널 부모(Panel_UI)의 로컬 좌표**다. 월드 좌표나 고정 픽셀 보정을 쓰지 않으므로
  Canvas의 Render Mode / Canvas Scaler 설정이 달라져도 같은 결과가 나온다.
- 이동은 절대 위치 대입이 아니라 **"누른 순간의 anchoredPosition + 그동안 포인터가 움직인 양"** 이다.
  한 프레임에 포인터가 크게 움직여도 그만큼만 따라가므로 패널이 튀지 않고, 누른 지점과 패널 사이의
  간격이 그대로 유지된다.
- `anchoredPosition`의 변화량은 부모 로컬 공간의 이동량과 1:1이라, 앵커가 점 앵커든 스트레치든 같은
  계산이 그대로 맞는다(패널의 크기·앵커 구조를 건드리지 않아도 되는 이유다).
- 마우스를 놓으면 그 자리에 남는다(되돌리기·스냅 없음).
- 패널을 닫았다 다시 열어도 `anchoredPosition`이 그대로라 실행 중 이동 위치가 유지된다 -
  `ModalPanel`은 위치를 초기화하지 않는다. 앱 재실행 시 위치 저장은 범위 밖이라 넣지 않았다.
- 초기 배치는 프리팹/씬에 저장된 값을 그대로 쓴다(코드가 시작 위치를 정하지 않는다).

## 5. 화면 밖 이동 방지

`Keep Inside Rect`(비우면 **핸들의 부모 = `top` 행**)가 루트 Canvas 영역 안에 남도록 `anchoredPosition`만
보정한다. `top` 행에는 타이틀바(`bg_top`)와 닫기 버튼(`btn_close`)이 함께 들어 있으므로, 요구된
"최소한 상단 타이틀바와 닫기 버튼은 화면 안" 조건이 그대로 충족된다.

- 비교는 Canvas 영역과 대상 영역을 **모두 패널 부모의 로컬 좌표로 옮겨서** 한다(이동 계산과 같은 좌표계).
- 패널의 크기나 앵커는 바꾸지 않는다 - 위치만 되돌린다.

## 6. Unity 에디터에서 해야 하는 연결 작업

두 프리팹에 각각 다음을 한다(총 2회, 필드 1개씩).

1. **`pn_CharacterSwap` 프리팹** → `bg/top/bg_top`에 `PanelDragHandle` 추가
   → `Target Panel`에 프리팹 루트 `pn_CharacterSwap` 연결
2. **`pn_Inventory` 프리팹** → `bg/top/bg_top`에 `PanelDragHandle` 추가
   → `Target Panel`에 프리팹 루트 `pn_Inventory` 연결

`Keep Inside Rect`는 비워 두면 `top`이 자동으로 쓰인다. `Target Panel`은 필수이며, 비어 있으면 오류를
남기고 드래그가 동작하지 않는다(조용히 잘못된 오브젝트를 움직이지 않도록 자동 추정하지 않는다).

## 7. 패널 포커스

`OnPointerDown`에서 `targetPanel.SetAsLastSibling()`을 호출한다 - 드래그뿐 아니라 타이틀을 클릭만 해도
맨 앞으로 올라온다.

InputBlocker와의 관계도 확인했다. 차단막은 패널보다 **앞 순서**에 만들어지는 형제라, 패널이
`SetAsLastSibling`으로 맨 뒤(가장 위)로 가도 여전히 패널보다 앞 순서에 남는다 → 계속 패널 뒤에
그려지고 배경 UI 클릭을 막는 동작이 그대로다. 다중 모달 동시 활성화는 지시서대로 이번 범위에서
확장하지 않았다.

## 8. 검증 상태

### 이 환경에서 확인한 것

- `Assets/Scripts` 전체를 Unity와 같은 정의로 Roslyn 컴파일 → **오류 0건**.
- 두 프리팹의 타이틀 영역 계층·RectTransform·Raycast Target 상태를 YAML로 확인
  (`bg_top` Image RaycastTarget=1, `btn_close`가 `bg_top`의 형제).
- `ModalPanel` / `InputBlocker` / 보상·인벤토리 코드에 변경 없음.

### 확인하지 못한 것

- **Play Mode 실행 전체**(에디터가 프로젝트 락을 잡고 있어 실행할 수 없다).

### Windows 빌드에서 한 번 봐야 할 것 (이 환경에서 판단 불가)

이 창은 커서가 "등록된 모달 패널 영역 또는 ControlDock" 밖에 있으면 매 프레임 클릭 관통
(`WS_EX_TRANSPARENT`)으로 전환된다. 드래그 중에는 패널이 커서를 따라오므로 커서는 계속 패널 영역
안에 있지만, **클램프 경계에 닿아 패널이 더 이상 움직이지 않는 상태에서 커서만 더 밖으로 나가면**
그 순간 커서가 패널 영역을 벗어난다.

Win32의 암묵적 마우스 캡처(버튼을 누른 창이 버튼을 뗄 때까지 마우스 메시지를 계속 받는 동작)가
있으면 드래그는 그대로 이어지지만, 이 환경에서는 실제로 확인할 수 없다. Windows 빌드에서
"패널을 화면 가장자리까지 끌고 간 뒤 계속 더 끌어 보기"를 한 번 확인하고, 드래그가 끊긴다면
드래그 중에는 클릭 관통을 잠시 고정하는 처리를 `TransparentWindowController`에 추가하면 된다
(이번에는 실제로 필요한지 확인되지 않은 상태에서 상태 플래그를 늘리지 않으려고 넣지 않았다).

## 9. 구현하지 않은 것

- 별도의 배치 모드/이동 활성화 버튼(지시서대로 만들지 않음)
- 앱 재실행 후 패널 위치 저장
- 다중 모달 동시 활성화를 위한 입력 차단 구조 확장
- 드래그 중 스냅/자석 정렬
