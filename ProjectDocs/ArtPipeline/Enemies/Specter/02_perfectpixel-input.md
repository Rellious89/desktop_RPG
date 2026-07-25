# Specter — PerfectPixel Input Sheet

> 입력 기준 이미지: `Assets/Art/Enemy/Specter/master/Specter-master-v2.png`
>
> PerfectPixel에서는 Base Idle과 Hit Reaction만 생성한다. `Idle A`는 별도 이미지를 생성하지 않고
> Base Idle 프레임의 알파값을 Unity에서 조절해 만든다.

## Character & Style

```text
Character name: Specter
Character description: Small floating white-sheet ghost with two dark cyan-lit eyes, a small open mouth, raised cloth arms with drooping hands, and three torn lower tails. Keep its compact rounded body and simple minion-like appearance consistent.
Art style: Pixel Art
Frame cell size: 512 x 512 px
Facing direction dropdown: Not set
```

기준 이미지가 이미 화면 오른쪽을 향하므로 첫 Attempt는 `Not set`으로 만든다. 결과가 좌우 반전될 때만
`Right`를 선택한 별도 Attempt를 비교한다.

## 1. Base Idle

```text
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Facing direction dropdown: Not set
Motion description: gently bob in place; sway the cloth, arms, and drooping hands; return smoothly
```

검수 기준:

- 위아래로 작게 둥실거리되 화면상 위치가 크게 이동하지 않는다.
- 양팔과 늘어진 손은 몸체 움직임을 따라 약하게 흔들린다.
- 작은 입과 두 눈의 형태가 프레임마다 유지된다.
- Actor Origin `(256, 360)`은 고정하고, 밑단과 몸체만 기준점 위에서 움직인다.
- 루프 마지막 프레임에서 첫 프레임으로 자연스럽게 연결된다.

## 2. Idle A — Fade Out / Fade In

PerfectPixel 생성 대상이 아니다.

```text
Source frames: Base Idle 4 frames 재사용
Visual effect: 전체 스프라이트가 흐려졌다가 다시 나타남
Implementation: SpriteRenderer alpha 조절
New sprite generation: None
```

연출 의도:

- 기본 Idle이 계속 재생되는 동안 알파값만 낮아졌다가 복구된다.
- 완전히 순간 삭제되는 느낌보다 유령이 잠시 희미해지는 느낌을 우선한다.
- 눈·입·외곽선만 따로 남기지 않고 전체 SpriteRenderer에 같은 알파를 적용한다.
- 정확한 최저 알파, 지속 시간과 반복 확률은 Unity Motion Profile에서 조절한다.

## 3. Hit Reaction

```text
Animation name: Hit
Frames: 2
FPS: 8
Repeat: Once
Facing direction dropdown: Not set
Motion description: burst the sheet and arms outward; squeeze the face in pain; contract toward idle
```

프레임 역할:

```text
Frame 00 / Hold:
피격 순간 천이 고양이 털처럼 사방으로 팍 퍼진다.
양팔과 늘어진 손도 바깥으로 튀며 전체 실루엣이 순간적으로 넓어진다.
두 눈과 작은 입이 중앙으로 눌리거나 가늘어져 찡그린 표정이 읽혀야 한다.

Frame 01 / Recovery:
퍼졌던 천과 양팔이 안쪽으로 수축한다.
찡그린 표정은 조금 남아 있지만 Base Idle 형태로 복귀 가능한 중간 자세여야 한다.
```

검수 기준:

- 천이 퍼지는 변화는 연기 폭발이 아니라 한 장의 천 실루엣이 순간적으로 부풀고 뻗는 형태다.
- 털, 가시와 광선처럼 새로운 재질이 생기면 Reject한다.
- 피격 중에도 흰 천, 눈 2개, 입, 양손과 세 갈래 밑단이 같은 스펙터로 식별돼야 한다.
- Frame 00은 Hit Hold, Frame 01은 Recovery Sprite로 각각 사용할 수 있어야 한다.
- 전체 위치 이동은 금지하며 Actor Origin과 부유 기준점은 유지한다.

## Regenerate Feedback 후보

한 Attempt에서는 하나만 사용한다.

```text
spread the cloth silhouette wider at impact; keep the same face, hands, and three lower tails
```

```text
make the eyes and mouth visibly squint in pain; keep the body pose and palette unchanged
```

```text
reduce the body translation; keep the floor-projected origin fixed throughout the reaction
```

## 출력 경로

```text
Assets/Art/Enemy/Specter/idle/Specter-frame-00.png
Assets/Art/Enemy/Specter/idle/Specter-frame-01.png
Assets/Art/Enemy/Specter/idle/Specter-frame-02.png
Assets/Art/Enemy/Specter/idle/Specter-frame-03.png

Assets/Art/Enemy/Specter/hit/Specter-frame-00.png
Assets/Art/Enemy/Specter/hit/Specter-frame-01.png
```

`idle_a` 전용 PNG 폴더는 만들지 않는다. 런타임 구현상 별도 Sprite 배열이 반드시 필요하다면 Base Idle
배열을 참조하거나 동일 프레임을 재사용하고, 이미지 파일을 복제하지 않는다.
