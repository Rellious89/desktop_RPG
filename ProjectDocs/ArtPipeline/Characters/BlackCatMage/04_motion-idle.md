# BlackCatMage — Base Idle Motion Brief

> 상태: `Legacy runtime documented / regeneration blocked`
>
> 기준 Master: `Assets/Art/Character/CatMage/master/CatMage-master-v1.png`

## Existing Runtime Motion

```text
Animation ID: idle
Type: Base Idle
Frames: 4
Animation FPS: 6
Loop: Yes
Canvas: 128×128 RGBA
Runtime path: Assets/Art/Character/CatMage/idle/
Pivot: (0.5, 0.1)
```

현재 4프레임은 게임에 연결되어 있다. 이 문서는 현행 동작을 보존하기 위한 이관 기록이며, 복구된 Master v1로
새 Attempt를 만들 때도 같은 동작 제약을 사용한다.

## Motion Intent

- 양발을 고정한 편안한 호흡 Idle이다.
- 로브와 모자는 몸의 호흡을 따라 작게 움직인다.
- 꼬리는 실루엣을 잃지 않는 범위에서 느리게 반응한다.
- 지팡이는 손의 호흡을 따라 최소한만 움직이며 길이와 마법석 위치가 변하지 않는다.
- 공격 시전, 주문 축적과 승리 포즈로 읽히지 않는다.

## Locked Parts

- 양발, 접지선과 전체 스케일
- 얼굴, 양쪽 귀와 밝은 눈
- 모자의 크기와 끝 방향
- 지팡이 길이, 그립과 붉은 마법석 1개
- 로브 구조, 꼬리 길이와 화면 방향

## Historical PerfectPixel Input

```text
Animation name: Idle
Frames: 4
FPS: 6
Repeat: Loop
Facing direction dropdown: Not set
Motion description: gentle breathing idle; slight robe, hat, tail, and staff movement; keep both feet planted
```

새 Attempt는 기존 런타임 프레임을 덮어쓰지 않고 별도 Prototype 폴더에 기록한다.

## Acceptance Criteria

- 6fps 반복에서 느린 호흡으로 읽힌다.
- 발과 접지선이 네 프레임에서 고정된다.
- 귀, 꼬리, 모자와 지팡이가 사라지거나 추가되지 않는다.
- 검은 털과 갈색 복식의 색 구분이 유지된다.
- 붉은 마법석의 개수, 크기와 위치가 동일하다.
- 화면 방향과 프레임별 캐릭터 크기가 바뀌지 않는다.
