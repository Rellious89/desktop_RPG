# btn_switching 행동력 전체 충전 버튼 전환 완료 보고서

작업일: 2026-07-29

## 1. 버튼 전환 방식

`btn_switching`에 붙어 있던 `RuntimeCharacterSwitcher`를 **같은 스크립트 파일 자리에서**
`StaminaRefillTestButton`으로 바꿨다.

```text
Assets/Scripts/Common/RuntimeCharacterSwitcher.cs  ->  Assets/Scripts/Common/StaminaRefillTestButton.cs
Assets/Scripts/Common/RuntimeCharacterSwitcher.cs.meta -> .../StaminaRefillTestButton.cs.meta  (guid 유지)
```

`.meta`를 함께 옮겨 guid(`e2b7ba8c…`)를 그대로 유지했으므로, 씬의
`m_Script: {fileID: 11500000, guid: e2b7ba8c…}` 참조가 끊기지 않는다. 즉 **씬을 고칠 필요 없이**
btn_switching의 컴포넌트가 새 역할로 바뀐다. Unity로 포커스를 옮기면 재임포트되고, Inspector의
컴포넌트 이름이 `Stamina Refill Test Button`으로 바뀌어 있으면 정상이다.

이 컴포넌트는 캐릭터 교체를 호출하지 않는다. 클릭 → `CharacterRoster.RefillAllStaminaToMax()` 한 줄이
전부이며, 자체 행동력 데이터를 갖지 않는다.

GameObject 이름(`btn_switching`)은 지시서대로 유지했다 - 이름과 실제 역할이 다르므로 컴포넌트
주석에 그 사실을 명시해 두었다.

## 2. 전체 충전 메서드

`CharacterRoster.RefillAllStamina()` → **`CharacterRoster.RefillAllStaminaToMax()`** 로 이름을 바꾸고
내용을 정리했다. 충전 경로는 이 메서드 하나뿐이며, 다음 두 곳이 공통으로 쓴다.

- ControlDock의 `btn_switching`(`StaminaRefillTestButton`)
- Inspector 컨텍스트 메뉴 `Debug - Refill All Stamina`

동작:

1. 사용 가능한 모든 캐릭터를 순회하며 `currentStamina`를 각자의 `MaxStamina`로 설정한다.
   (현재 소환된 캐릭터, 꺼져 있는 캐릭터, 0인 캐릭터 전부 포함)
2. **값이 실제로 바뀐 캐릭터가 하나도 없으면 그대로 끝낸다** - 저장도 UI 갱신 신호도 보내지 않는다.
3. `SaveSystem.Save()`를 **한 번만** 호출한다(예전에는 캐릭터마다 저장해서 클릭 1회에 파일을 6번 썼다).
4. 바뀐 캐릭터마다 `CharacterStateChanged`를 보낸다.

`MaxStamina`로 "설정"하는 방식이라 여러 번 눌러도 최대치를 넘지 않는다(누적 가산이 아니다).
레벨/경험치/콤보/현재 캐릭터 선택 상태는 건드리지 않는다 - `current`와 `ApplyActiveCharacter`를
호출하는 경로가 이 메서드에 없다.

## 3. 저장 처리

`SaveSystem.Save()`가 성공 여부(`bool`)를 돌려주도록 바꿨다(기존 호출부는 반환값을 무시하므로
동작 변화 없음). 전체 충전은 이 값을 확인해서 실패 시 오류를 남긴다.

```text
저장 실패 시: 기존 저장 파일은 그대로 남는다(File.WriteAllText가 예외로 중단되어 부분 기록이 없다).
              이번 실행 중의 행동력은 이미 충전된 상태이므로, "재실행하면 이전 값으로 돌아간다"는
              사실을 오류 로그로 분명히 남긴다 - 조용히 성공한 것처럼 보이게 하지 않는다.
```

메모리 값을 되돌리는 롤백은 하지 않았다. 사용자가 방금 누른 버튼이 아무 안내 없이 취소된 것처럼
보이는 쪽이 더 나쁜 동작이라고 판단했다.

## 4. UI 갱신

| 대상 | 경로 |
| --- | --- |
| 현재 캐릭터 행동력 HUD | `CharacterStaminaDisplay`가 `CharacterStateChanged`를 구독 - 패널이 닫혀 있어도 갱신된다 |
| 교체 리스트의 모든 행동력바 | `CharacterSwapPanel.HandleCharacterStateChanged`가 캐릭터별로 호출되어 바뀐 항목이 모두 갱신 |
| 행동력 0이었던 캐릭터의 상태 | `RefreshItem`이 `GetSwapBlockReason`을 다시 계산 → `Exhausted` → `Ready` |
| 교체 버튼 선택 가능 여부 | 아래 변경 참고 |
| 패널이 닫혀 있을 때 | 저장 데이터와 HUD는 갱신되고, 리스트는 다음에 열 때 `OnEnable`이 새로 만든다 |

`CharacterSwapPanel.HandleCharacterStateChanged`에서 `UpdateSwapButton()`을 **항상** 호출하도록
바꿨다(예전에는 바뀐 캐릭터가 지금 선택 중인 캐릭터일 때만 호출했다). 여러 캐릭터가 한 번에 바뀌는
전체 충전에서 교체 버튼 판정이 누락되지 않게 하기 위함이다.

행동력바는 기존 `ProgressBarView`를 그대로 쓰고, `PlayerProgress`나 경험치 이벤트는 참조하지 않는다.

## 5. 함께 정리한 것

- `CharacterRoster.SwitchToNext()` **삭제**. 유일한 호출부가 이 버튼이었고, 교체 경로는 이제 캐릭터
  교체 패널의 `TrySwitchTo` 하나뿐이다. 삭제 사유를 코드 주석으로 남겼다.
- `MotionEditorWindow`의 주석에 남아 있던 `RuntimeCharacterSwitcher` 언급을 `CharacterRoster`로 수정.

## 6. 검증 상태

### 이 환경에서 확인한 것

- `Assets/Scripts` 전체 Roslyn 컴파일 → **오류 0건**.
- `RuntimeCharacterSwitcher`를 참조하는 코드가 프로젝트에 하나도 남아 있지 않음을 grep으로 확인.
- 스크립트 guid가 유지되어 씬의 컴포넌트 참조가 그대로임을 확인.

### 확인하지 못한 것

- **Play Mode 실행 전체**(Unity 에디터가 프로젝트 락을 잡고 있어 실행할 수 없다).

## 7. 남은 수동 작업 (Unity 에디터)

1. **버튼 표시 텍스트 변경**: `btn_switching > lb_change`의 텍스트가 아직 `Change`다.
   `REFILL` 또는 `행동력 충전`으로 바꾼다. 이 오브젝트에는 `LocalizedTMPText`가 붙어 있지 않고
   테스트 전용 UI이므로, 로컬라이징 대상에 넣지 않는다(프로젝트 로컬라이징 가이드 10장 마지막 규칙).
2. Unity로 포커스를 옮겨 재임포트한 뒤, `btn_switching`의 컴포넌트가 `Stamina Refill Test Button`으로
   보이는지 확인한다. 혹시 `Missing (Mono Script)`로 표시되면 컴포넌트를 제거하고
   `StaminaRefillTestButton`을 다시 추가하면 된다(직렬화 필드가 없어 잃을 데이터는 없다).

## 8. 검증 항목 7번에 대한 주의 - 지금 구조에서는 그대로 통과하지 않는다

> 7. 패널이 열린 상태에서 버튼을 눌러도 리스트가 즉시 갱신되는지 확인한다.

캐릭터 교체 패널이 열려 있는 동안에는 전체 화면 `InputBlocker`가 ControlDock을 덮는다. 이는 이전
작업 지시서의 "패널이 열린 동안 배경의 전투 UI로 입력이 전달되지 않도록 한다" 요구로 넣은 것이고,
이미 Play Mode 검증을 통과한 동작이다. 따라서 **패널이 열린 상태에서는 btn_switching을 클릭할 수
없다.** 이번 작업에서 이 모달 동작을 임의로 되돌리지 않았다.

갱신 경로 자체(`CharacterStateChanged` → 열려 있는 패널의 항목 갱신)는 정상 동작하므로, 다음 중
하나로 확인할 수 있다.

- Play Mode에서 패널을 열어 둔 채 Inspector의 `CharacterRoster` → 컨텍스트 메뉴
  `Debug - Refill All Stamina` 실행 → 리스트가 그 자리에서 갱신되는지 확인
- 패널을 닫고 버튼을 누른 뒤 다시 열어 확인(이 경우 `OnEnable`의 리스트 재구성 경로)

패널이 열려 있어도 ControlDock은 계속 누를 수 있게 하고 싶다면 별도 판단이 필요하다 -
`CharacterSwapPanel`의 InputBlocker 생성을 끄거나 ControlDock 영역만 예외로 두는 방식이며,
이전 지시서의 입력 차단 요구와 충돌하므로 이번 작업에서는 건드리지 않았다.

## 9. 구현하지 않은 것

- 시간 경과 회복, 회복소, 정식 사용자용 회복 시스템
- 버튼을 통한 캐릭터 교체/소환
- 버튼 아이콘 교체(텍스트 변경만 안내)
