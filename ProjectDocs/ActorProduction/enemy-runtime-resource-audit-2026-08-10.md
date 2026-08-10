# Enemy Runtime & Resource Audit

> 감사일: 2026-08-10
>
> 대상: 현재 Active Enemy 7종
>
> 변경 범위: 읽기 전용 감사, 현황 도구와 문서만 갱신. Unity 에셋과 CSV는 수정하지 않음.

## 현재 몬스터 구조

현재 Enemy는 플레이어를 직접 공격하는 전투원이 아니라 피격되는 전투 대상이다. `MonsterMotionProfile`의
런타임 모션 범위는 다음과 같다.

```text
Base Idle + 선택적 Idle Event + Hit Hold/Recovery + 선택적 Defeat
```

- 공격 모션: 현재 구조에는 없음. 누락이 아니라 해당 없음.
- Defeat: 프레임이 비어 있어도 정상. 처치 순간 피격 자세를 유지하며 Fade-out한다.
- 런타임 최소 조건: Base Idle과 Hit에 재생 가능한 Sprite가 있고 활성 Monster 테이블 행이 연결되어야 한다.
- 제작 준비 조건: Hit Hold와 Recovery가 유효해야 한다. 기본은 서로 다른 Sprite지만 품질상 의도된 반복은
  Actor별 명시적 예외가 있을 때만 허용한다.

## 요약

| Actor | World | Idle | Idle Event | Hit | Defeat | Table | 제작 분류 |
|---|---|---:|---:|---|---|---|---|
| HyenaRaider | 애니멀랜드 | 4f | 4f + 4f | 2f / 정상 | Fade-only | 정상 | 제작 패키지 보강 |
| MoleMiner | 애니멀랜드 | 4f | 6f | 2f / 정상 | Fade-only | 정상 | 제작 패키지 보강 |
| RockGolem | 판타지아 | 4f | 없음 | 2f / 정상 | Fade-only | 정상 | 제작 패키지 보강 |
| Scarecrow | 판타지아 | 4f | 없음 | 2f / 정상 | Fade-only | 정상 | 제작 패키지 보강 |
| Specter | 망자의 도시 | 4f | 4f | 2f / 정상 | Fade-only | 정상 | 그대로 사용 가능 |
| VenomCultist | 판타지아 | 4f | 6f + 6f | 2f / 정상 | Fade-only | 정상 | 그대로 사용 가능 |
| Werewolf | 애니멀랜드 | 4f | 6f + 4f | 2슬롯 / 의도된 반복 | Fade-only | 정상 | 제작 패키지 보강 |

기술적 런타임 연결과 제작 모션 기준은 모두 7/7이다. Specter와 VenomCultist는 제작 패키지까지 보강되어
`그대로 사용 가능`으로 분류한다. 나머지 5종은 런타임 문제가 아니라 새 패키지 규칙으로의 문서 이행이 남았다.

## 핵심 발견

### Werewolf — 의도된 Hit Recovery 반복

`Werewolf_MotionProfile`의 Hit 배열은 두 슬롯 모두
`Werewolf-frame-01.png`를 참조한다. `holdFrame: 0`, `recoveryFrame: 1`이므로 인덱스는 유효하지만 실제
표시 이미지는 같다. 사용자가 2026-08-10 리소스 퀄리티 문제 때문에 같은 프레임을 반복 배치한 의도된
현상임을 확정했다.

따라서 이 Actor는 명시적 예외로 제작 준비 완료 처리하며 `Werewolf-frame-00.png`를 자동 연결하지 않는다.
같은 폴더의 미참조 파일은 삭제하지 않고 제작 이력으로 유지한다.

### 미참조 모션 프레임

- `Assets/Art/Enemy/RockGolem/hit/RockGolem-frame-00.png`
- `Assets/Art/Enemy/Werewolf/hit/Werewolf-frame-00.png`

RockGolem은 `frame-01`, `frame-02`를 Hit로 사용한다. `frame-00`은 잔여 후보지만 현재 동작에는 문제가 없다.
두 파일 모두 삭제 대상이 아니라 검토 대상으로만 기록한다.

### 테이블과 명칭

7종 모두 다음 조건을 충족한다.

- `Monster.csv` 행 활성화
- `motion_profile_key`가 실제 Profile 에셋 이름과 일치
- `preview_sprite_key`가 실제 Portrait와 일치
- World ID와 World 이름 연결 정상

`HyenaRaider`의 한국어 표기만 `하이에나라이더`로 되어 있다. Raider와 Rider 중 어느 설정이 맞는지는 기획
판단 영역이므로 자동 수정하지 않았다.

## 제작 패키지 이행 분류

### 그대로 사용 가능

- Specter, VenomCultist: Brief, PerfectPixel 입력, 측정값, Master, 패키지 인덱스와 런타임 모션 문서가
  모두 준비됐다.

### 기존 자료를 이어서 보강

- HyenaRaider, MoleMiner, RockGolem, Scarecrow: Master와 런타임 리소스는 있으나 새 패키지 문서가 없다.
  기존 결과를 관찰값으로 삼아 패키지를 역작성할 수 있다.
- Werewolf: 의도된 Hit 반복은 승인됐으며 새 패키지 문서만 역작성하면 된다.

Scarecrow는 Master 변형이 특히 많아 패키지 작성 시 현재 런타임 외형과 대응하는 기준 Master를 먼저
지정해야 한다. 이 선택은 품질 판단이므로 감사 단계에서는 확정하지 않는다.

## 현황 도구 반영

Actor Production Tracker가 Enemy에 대해 다음을 자동으로 검사한다.

- Profile의 Base Idle, Idle Event, Hit, Defeat 실제 Sprite GUID와 프레임 수
- Hit Hold/Recovery 인덱스 범위, 서로 다른 Sprite 사용 여부와 Actor별 승인 예외
- Profile의 `resourceFolderPath`와 실제 배우 폴더 일치 여부
- 다른 배우 폴더의 프레임을 잘못 참조하는지
- 모션 폴더에 있지만 Profile에서 사용하지 않는 프레임
- 활성 Monster 테이블 행, Profile 키, Portrait 키 연결
- `몬스터 모션 점검 필요` 필터

Defeat가 비어 있는 것은 경고나 공백으로 표시하지 않으며 `Fade-only`로 명시한다.

## 권장 후속 순서

1. HyenaRaider, MoleMiner, RockGolem, Scarecrow와 Werewolf의 기존 런타임 리소스를 기준으로 제작 패키지를 역작성한다.
2. RockGolem의 미참조 Hit 프레임과 Scarecrow의 기준 Master는 별도 승인 후 정리한다.
3. Specter와 VenomCultist는 V2 재생산 또는 추가 모션 요청 전까지 현재 패키지를 기준본으로 유지한다.
