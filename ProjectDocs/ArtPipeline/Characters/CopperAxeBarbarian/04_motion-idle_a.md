# CopperAxeBarbarian — Chest Tap Idle Event Motion Brief

> 상태: PerfectPixel 입력용 1차안
> CatKnight `idle_a`와 동일한 4프레임·6fps를 사용하고 한 번 재생한 뒤 기본 Idle로 복귀한다.

## 기본 설정

- Animation ID: `idle_a`
- Type: `IdleVariant`
- 게임플레이 목적: 쌍도끼 바바리안의 자신감과 묵직함을 짧게 보여주는 대기 이벤트
- 행동 요약: 오른손의 도끼를 놓지 않은 채, 손잡이를 쥔 주먹으로 노출된 윗가슴을 한 번 가볍게 친다
- 프레임 수: 4
- `animationFps`: 6
- 총 재생 시간: 약 0.667초
- Loop: 하지 않음. 03 이후 기본 `idle-00`으로 복귀
- 최종 경로: `Assets/Art/Character/CopperAxeBarbarian/idle_a/CopperAxeBarbarian-idle_a-NN.png`
- Actor Origin: 기본 Idle과 완전히 동일

## 오른손과 접촉 위치 정의

- 별도 수식어 없는 오른쪽/왼쪽은 캐릭터 자신의 해부학적 기준이다.
- 캐릭터의 오른손은 현재 Master Design에서 **화면 왼쪽, 어깨보호대 아래에 있으며 도끼를 세워 든 손**이다.
- 캐릭터의 왼손은 **화면 오른쪽, 도끼를 낮게 든 손**이며 이 이벤트에서는 거의 움직이지 않는다.
- 오른손은 네 프레임 모두 도끼 손잡이를 놓지 않는다. 손잡이를 쥔 주먹의 손등/손가락 마디로 친다.
- 접촉 위치는 흉골에 가까운 노출된 윗가슴이다.
- 사선 가죽끈, 버클, 문신의 핵심 형태를 가리지 않는 빈 피부 면을 사용한다.
- 도끼 손잡이와 날은 오른쪽 팔뚝과 함께 하나의 단단한 단위로 회전한다.
- 가슴 접촉 시 도끼날은 몸통 바깥의 화면 왼쪽 위/왼쪽에 남아 신체를 관통하지 않는다.
- 반대손과 반대쪽 도끼는 균형추 역할만 하며 기본 위치를 거의 유지한다.

## 프레임 설계

| 프레임 | 단계 | 자세 변화 |
|---:|---|---|
| 00 | 시작/중립 | 기본 `idle-00`과 동일한 자세. 이벤트 시작 시 위치 점프가 없어야 함 |
| 01 | 짧은 준비 | 오른쪽 팔꿈치를 굽혀 도끼를 잡은 주먹을 가슴 쪽으로 이동. 상체는 최대 1픽셀만 뒤로 긴장 |
| 02 | 접촉 | 주먹이 노출된 윗가슴에 닿는 단 하나의 접촉 포즈. 어깨가 짧게 안쪽으로 모이고 흉곽이 미세하게 눌림 |
| 03 | 반동/복귀 | 주먹이 가슴에서 떨어져 기본 그립 위치로 돌아가는 중간 자세. 다음 `idle-00`과 자연스럽게 연결 |

한 번만 `00 → 01 → 02 → 03 → idle-00`으로 재생한다. 02에서 여러 차례 튕기거나 두 번 치는 동작을
만들지 않는다. 이 동작에는 `HitPoint`, 공격 판정, Attack Movement와 전투 이펙트가 없다.

## 움직여도 되는 부위

- 오른쪽 위팔, 팔꿈치, 팔뚝과 손
- 오른손 도끼 전체의 제한된 회전과 이동
- 접촉 순간의 오른쪽 어깨와 흉곽 미세 압축
- 상체 움직임을 따라가는 브레이드 끝의 1픽셀 이내 지연

## 고정해야 하는 부위

- 양발, 다리, 골반과 Actor Origin
- 반대쪽 팔의 그립과 반대쪽 도끼
- 오른손의 도끼 그립 위치
- 두 도끼의 크기와 디자인
- 얼굴, 수염, 문신, 피부색, 어깨보호대와 가죽끈 구조
- 캔버스 크기, 팔레트, 외곽선과 광원

## PerfectPixel UI 입력

```text
Animation name: Chest Tap
Frames: 4
FPS: 6
Repeat: Play once / non-loop
Motion description: tap chest once with his right axe hand; keep both axes held; return to idle
Facing direction: Not set
```

PerfectPixel이 `right`를 화면 오른쪽으로 잘못 해석하면 다음 시도에서는 한 줄만 아래처럼 교체한다.

```text
tap chest once with the axe hand below the shoulder guard; keep both axes held; return to idle
```

프레임별 설계, 해부학적 오른손 정의와 도끼 궤적은 PerfectPixel 입력문이 아니라 출력 검수 기준으로 사용한다.

## 합격 기준

- 시작 00이 기본 `idle-00`과 동일하고 종료 03에서 기본 00으로 자연스럽게 복귀한다.
- 오른손이 모든 프레임에서 같은 위치로 도끼 손잡이를 쥐고 있다.
- 접촉은 02에서 정확히 한 번만 읽힌다.
- 오른손 도끼가 몸통·얼굴·가죽끈·반대 도끼를 관통하지 않는다.
- 양발, 골반, 반대쪽 도끼와 Actor Origin이 흔들리지 않는다.
- 공격, 승리 포즈 또는 위협 동작이 아니라 짧은 자기 격려 Idle 이벤트로 읽힌다.
