# Specter — Idle A Motion Brief

> 상태: `Existing runtime documented`
>
> 재생성 기준 Master: `Assets/Art/Enemy/Specter/master/Specter-master-v2.png`

## Existing Runtime Motion

```text
Animation ID: idle_a
Type: Idle Variant
Frames: 4
Animation FPS: 6
Loop: Event clip
Canvas: 128×128 RGBA
Path: Assets/Art/Enemy/Specter/idle_a/
Runtime: MonsterMotionProfile idleEvents[0]
```

## Motion Intent

- 기본 Idle 사이에 드물게 재생되는 별도 유령 대기 변형이다.
- 몸체 중심과 부유 기준을 유지하면서 천과 얼굴의 변화를 작은 범위로 제한한다.
- 이벤트 종료 후 Base Idle 첫 자세로 자연스럽게 돌아갈 수 있어야 한다.

## Historical Note

초기 PerfectPixel 문서는 Base Idle 프레임에 알파만 조절하는 Fade Out/In 연출을 제안했지만 실제 런타임은
별도의 `idle_a` 4프레임을 참조한다. 현재 패키지는 실제 연결을 기준으로 한다. 알파 전용 연출로 되돌리려면
별도 런타임 변경 승인이 필요하다.

## Acceptance Criteria

- Base Idle과 같은 캐릭터 크기, 팔레트, Pivot과 화면 방향을 사용한다.
- 이벤트 전후로 위치가 튀지 않는다.
- 공격, 피격과 사망 모션으로 오해할 정도의 큰 실루엣 변화가 없다.

