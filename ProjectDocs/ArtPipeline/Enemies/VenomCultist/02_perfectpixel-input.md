# VenomCultist — PerfectPixel Input Sheet

> 입력 기준 이미지: `Assets/Art/Enemy/VenomCultist/master/VenomCultist-master-v1.png`
>
> PerfectPixel 자유 입력은 아래 영문 문장을 사용한다. 프레임 수, FPS, Repeat와 방향은 설명문에 넣지 않고
> 각 UI 필드에서 설정한다.

## Character & Style

```text
Character name: VenomCultist
Character description: A living human cultist in a deep dark-purple hooded robe, with his face hidden in shadow and a large bone-white skull pendant centered on his chest. He carries one short poison-coated dagger; keep the hood, pendant, robe, and single dagger consistent.
Art style: Pixel Art
Frame cell size: 512 x 512 px
Facing direction dropdown: Not set
```

기준 이미지가 이미 화면 오른쪽을 향하므로 첫 Attempt는 `Not set`으로 만든다. 결과가 반전될 때만
`Right`를 선택한 별도 Attempt를 비교한다.

## 1. Base Idle

```text
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Facing direction dropdown: Not set
Motion description: breathe calmly in place; let poison drip slowly from the dagger; loop smoothly
```

검수 기준:

- 몸통과 어깨가 작게 오르내리는 평범한 호흡 동작이다.
- 후드, 해골 목걸이와 로브 자락은 호흡을 따라 최소한으로 움직인다.
- 독 단검은 앞손에 계속 들고 있어야 한다.
- 단검 끝에서 독성 녹색 액체가 천천히 한두 방울 떨어진다.
- 독 방울 표현이 생성되지 않더라도 캐릭터와 단검 동작이 안정적이면 프레임을 채택하고 독은 셀프 작업한다.
- 전방 디딤발과 Actor Origin `(268, 380)`은 고정한다.

독 표현 보완용 Feedback:

```text
add one small green poison drop falling from the dagger tip; keep the body and weapon steady
```

## 2. Idle A — Prayer

```text
Animation name: IdleA
Frames: 6
FPS: 6
Repeat: Once
Facing direction dropdown: Not set
Motion description: lower the head and clasp both hands in prayer; hold briefly; return to idle
```

게임적 허용:

- 기도를 시작할 때 단검은 손에서 내려놓는 장면 없이 사라진다.
- 기도 동작 전체에서 단검과 독 방울은 보이지 않는다.
- 동작이 끝나 Base Idle로 복귀할 때 단검이 다시 앞손에 나타나는 것은 허용한다.
- 숨긴 단검을 로브나 허리춤에 새로 그리지 않는다.

검수 기준:

- 두 손을 가슴 앞에서 모은다.
- 깊은 후드를 쓴 머리를 아래로 숙인다.
- 해골 목걸이는 모은 손에 일부 가릴 수 있지만 완전히 다른 장식으로 바뀌면 안 된다.
- 발과 몸 전체 위치는 고정하고 상체, 머리와 양팔만 움직인다.
- 기도는 공격 주문이 아니라 조용한 숭배 동작으로 읽혀야 한다.

보완용 Feedback:

```text
hide the dagger completely and clasp both empty hands beneath the bowed hood; keep both feet fixed
```

## 3. Idle B — Dagger Slash

```text
Animation name: IdleB
Frames: 6
FPS: 8
Repeat: Once
Facing direction dropdown: Not set
Motion description: slash the dagger once through empty air; follow through briefly; return to idle
```

검수 기준:

- 앞손의 단검을 허공에 한 번 휘두른다.
- 실제 타격 대상이나 피격 연출은 나타나지 않는다.
- 단검은 한 자루만 유지하고 다른 손에는 새 무기를 만들지 않는다.
- 해골 목걸이와 깊은 후드가 휘두르는 동안에도 유지된다.
- 발은 고정하고 상체와 단검 팔의 회전으로 동작을 표현한다.
- 마지막에는 Base Idle의 단검 위치로 돌아온다.

보완용 Feedback:

```text
make one clear dagger slash with a short follow-through; keep the feet, pendant, and hood unchanged
```

## 4. Hit Reaction

```text
Animation name: Hit
Frames: 2
FPS: 8
Repeat: Once
Facing direction dropdown: Not set
Motion description: drop the dagger as a punch twists the upper body backward; freeze at impact
```

프레임 역할:

```text
Frame 00 / Hold:
펀치를 맞은 것처럼 상반신만 한쪽으로 돌아가며 대각선 뒤로 크게 기울어진다.
단검은 손에서 완전히 빠져 캐릭터 가까운 허공에 떠 있다.
단검은 한 자루만 존재하며 손과 공중에 동시에 중복되면 안 된다.

Frame 01 / Recovery:
비틀린 상반신이 다시 중앙으로 돌아오기 시작한다.
떠 있던 단검은 게임적 허용으로 앞손 근처 또는 Base Idle 위치로 복귀할 준비를 한다.
```

게임적 허용:

- 놓친 단검은 바닥으로 떨어지지 않고 피격 포즈 옆 허공에 정지한다.
- 단검을 떨어뜨리고 다시 줍는 별도 동작은 만들지 않는다.
- Recovery 또는 Base Idle 전환 시 단검이 손으로 즉시 복귀할 수 있다.

검수 기준:

- 전신이 통째로 이동하거나 넘어지는 동작이 아니다.
- 양발과 Actor Origin은 고정하고 상반신만 회전·후경사한다.
- 뒤로 젖히기만 하지 않고 펀치 충격으로 어깨선과 후드 방향이 한쪽으로 비틀려야 한다.
- 손에서 놓친 단검과 빈 앞손이 둘 다 명확하게 보여야 한다.
- 해골 목걸이는 상반신 회전을 따라 움직이되 사라지지 않는다.
- Frame 00은 Hit Hold, Frame 01은 Recovery Sprite로 각각 사용할 수 있어야 한다.

보완용 Feedback:

```text
twist only the upper body sharply backward from a punch; show the single dropped dagger floating nearby
```

단검이 중복된 경우:

```text
remove the dagger from the hand; keep exactly one dagger suspended beside the recoiling body
```

## 출력 경로

```text
Assets/Art/Enemy/VenomCultist/idle/VenomCultist-frame-00.png
Assets/Art/Enemy/VenomCultist/idle/VenomCultist-frame-01.png
Assets/Art/Enemy/VenomCultist/idle/VenomCultist-frame-02.png
Assets/Art/Enemy/VenomCultist/idle/VenomCultist-frame-03.png

Assets/Art/Enemy/VenomCultist/idle_a/VenomCultist-frame-00.png ... frame-05.png
Assets/Art/Enemy/VenomCultist/idle_b/VenomCultist-frame-00.png ... frame-05.png

Assets/Art/Enemy/VenomCultist/hit/VenomCultist-frame-00.png
Assets/Art/Enemy/VenomCultist/hit/VenomCultist-frame-01.png
```

PerfectPixel 출력은 초안이다. 독 방울, 기도 중 단검 제거, 피격 중 단검 분리처럼 생성 결과가 불안정한
요소는 캐릭터 비율과 본체 동작이 승인된 프레임을 우선 확보한 뒤 셀프 작업으로 정리한다.
