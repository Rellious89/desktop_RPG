# Legacy Keybuddy Character Resource Audit

> 감사일: 2026-08-10
>
> 외부 원본 범위: `/Users/rellious/Desktop/keybuddy/c_character`
>
> 원칙: 이 폴더 밖의 외부 Master 저장소는 없다는 사용자 확인을 기준으로 함

## 목적

제작 패키지 도입 전에 Master, PerfectPixel 출력과 FireAlpaca 수정본이 한 작업 폴더에 섞여 저장되었다. 이
감사는 외부 폴더의 Actor를 현재 프로젝트에 매핑하고, 복구 완료·복구 후보·테스트 전용·확인 필요 상태를
구분한다. 파일명만 보고 정식 Master를 자동 승인하지 않는다.

## 감사 방법

- 최상위 Actor 폴더, `*master*`, `base.png`, `production-base.png`를 전수 조사했다.
- PerfectPixel manifest, sprite sheet, APNG/GIF와 `frames/{motion}` 구조를 확인했다.
- 가능한 경우 외부 PerfectPixel 프레임을 현재 납품 크기로 최근접 축소해 현행 프로젝트 프레임의 알파 형태와
  비교했다.
- 외부 Master와 프로젝트 PNG의 SHA-1을 비교해 동일 파일 여부를 확인했다.
- 크기·형태가 비슷해도 Actor ID나 승인 이력이 불명확하면 자동 복구하지 않았다.

## 결과 요약

| 외부 폴더 | 프로젝트 Actor | Master/기준 이미지 | PerfectPixel 결과 | 판정 |
|---|---|---|---|---|
| `Barbarian` + `sd_Barbarian` | `Barbarian` | `Barbarian/barbarian.png` | Idle, Idle A/B, Attack | **복구 완료** |
| `Cat-knight` | `CatKnight` | 독립 Master 없음 | Idle 4종, Attack, Stab, Spin | 출력 계보만 보존 |
| `c_CatMage` | `CatMage` | `CatMage_master.png` | 일반/Low/Test 출력 | **복구 완료** |
| `c_ElfArcher` | `ElfArcher` | `ElfArcher-master.png` | Idle A/B, Attack A/B 등 | Master 복구 후보 |
| `c_Elfguardian` | `ElfGuardian` | `master.png`, `master_noweapon.png` | Idle A/B, Attack A/B | 프로젝트 Master 계열과 함께 검토 |
| `c_IceMage` | `Test_IceMage` / alias `IceMage` | `base.png` | Idle, Cast | 테스트 기준 보존 |
| `c_Leopard` | `Test_Leopard` / alias `Leopard` | `base.png` | 없음 | 테스트 기준 보존 |
| `c_RabbitHealer` | `RabbitHealer` | `master.png`, `master2.png` | Idle A/B, Attack | 승인 v2 우선, 외부 중간본 복구 금지 |
| `c_ShieldKnight` | `DogShieldWarrior` 확정 | `master.png` | Idle, Attack, Custom 2/3 | **품질 미달 / Hold 유지** |

## 세션별 저장 방식 차이

실제로 세션에 따라 저장 방식이 달랐다.

- Master 이름: `barbarian.png`, `{Actor}-master.png`, `{Actor}_master.png`, `master.png`, `master2.png`,
  `base.png`가 혼재한다.
- 배경: 완전 투명, 녹색 크로마키, 자홍색 크로마키가 혼재한다.
- PerfectPixel 출력: Actor 폴더 1단계부터 같은 이름이 3~4번 중첩된 구조까지 있다.
- 모션 이름: `attack`, `cast`, `custom2`, `attack_t2`, `attack_T2`, `attack-heavy`가 혼재한다.
- 후보 상태: Master, 배경 제거 중간본, 128px production-base와 최종 프레임이 이름만으로 구분되지 않는
  사례가 있다.
- Actor ID: `BlackCatMage/CatMage`, `IceMage/Test_IceMage`, `ShiledWarrior/ShieldKnight`처럼 별칭·오타가
  존재한다.

따라서 향후 복구는 파일명 검색만으로 처리하지 않고 제작 패키지의 canonical ID, 이미지 상태와 런타임 계보를
함께 확인해야 한다.

## Actor별 상세

### Barbarian — 복구 완료

```text
External Master: /Users/rellious/Desktop/keybuddy/c_character/Barbarian/barbarian.png
Canonical Master: Assets/Art/Character/Barbarian/master/Barbarian-master-v1.png
SHA-1: 4403590a0029cebd8fe6c6a047c79cfe50b1d85e
PerfectPixel lineage: sd_Barbarian/Barbarian/
Alpha-shape similarity: 98.11% (Idle 00, nearest 512→128)
```

현행 런타임이 후보정된 결과라 바이트는 다르지만 동일 생산 계보다. 외부 `Barbarian/Barbarian/...`에는 이전
256px 시도와 중첩 출력도 남아 있으므로 최신 납품 기준으로 자동 복사하지 않는다.

### CatKnight — 독립 Master 없음

외부 폴더에는 PerfectPixel 결과와 manifest만 있고 별도 Master 파일이 없다. 현재 프로젝트에는 Tier 1/2와
Idle 변형이 이미 연결되어 있다. 외부 출력과 현행 Idle 00의 알파 형태는 98.68% 일치한다.

Master가 필요한 재설계 시 현행 프레임을 임의로 확대해 Master로 만들지 말고 새 후보를 생성한다.

### CatMage — 복구 완료

```text
External Master: /Users/rellious/Desktop/keybuddy/c_character/c_CatMage/CatMage_master.png
Canonical Master: Assets/Art/Character/CatMage/master/CatMage-master-v1.png
SHA-1: 83363e587625f16f0c6fb02b9df6993c63f12183
PerfectPixel lineage: c_CatMage/low_CatMage/BlackCatMage/
Alpha-shape similarity: 98.02% (Idle 00, nearest 512→128)
```

Master는 녹색 크로마키 배경이 있는 생성 입력본이며 Unity 납품 프레임이 아니다. `BlackCatMage`,
`low_CatMage`, `test` 출력은 서로 다른 시도이므로 일괄 복사하지 않는다.

### ElfArcher — Master 복구 후보

```text
Candidate: /Users/rellious/Desktop/keybuddy/c_character/c_ElfArcher/ElfArcher-master.png
Canvas: 512×512
Background: magenta chroma-key
SHA-1: 81fdc2cf4549983f6eb8f437c5cae7d1571dc2fd
Alpha-shape similarity to current Idle 00: 97.67%
```

현재 프로젝트에 `Assets/Art/Character/ElfArcher/master/`가 없지만 실제 출력 계보는 강하게 확인된다. 다만 이번
요청의 3종 범위를 넘으므로 자동 복구·승인하지 않는다. ElfArcher 패키지를 만들 때 첫 복구 대상으로 사용한다.

### ElfGuardian — 중복 Master 계열

외부에는 1254px 크로마키 `master.png`와 51×84 투명 `master_noweapon.png`가 있다. 프로젝트에는 이미
`LeafGlaiveElf-master-v1`부터 `v6`까지 여러 Master가 존재하며 외부 파일과 체크섬은 일치하지 않는다.
PerfectPixel Idle과 현행 Idle의 알파 형태는 97.94% 일치한다.

외부 파일을 추가 복구하면 승인 계열이 더 혼란스러워지므로, ElfGuardian 패키지 작성 시 프로젝트 v1~v6의
승인 이력을 먼저 정리한다.

### Test_IceMage / Test_Leopard — 테스트 기준

- `c_IceMage/base.png`는 128×128이며 외부 Idle 00과 프로젝트 Test_IceMage Idle 00이 픽셀 단위로 100%
  일치한다.
- `c_Leopard/base.png`는 128×128 테스트 기준 이미지다.
- 두 Actor는 현재 Test lifecycle이므로 정식 Master 폴더로 이동하지 않는다.

### RabbitHealer — 승인 v2 유지

- `c_RabbitHealer/master.png`: 녹색 크로마키가 있는 v2 디자인 원본 계열.
- `c_RabbitHealer/master2.png`: 투명하지만 신체 일부 알파가 크게 누락된 깨진 중간본.
- `character/character.production-base.png`: 128px 생산 보정본이며 고해상도 Master가 아니다.
- 외부 PerfectPixel Idle과 현행 Idle의 알파 형태는 98.78% 일치한다.

정식 Master는 이미 승인된
`Assets/Art/Character/RabbitHealer/master/RabbitHealer-master-v2.png`를 유지하고 외부 두 파일을 복구하지
않는다.

### DogShieldWarrior — 동일 Actor 확정 / Hold

`c_ShieldKnight/master.png`는 중갑과 대형 방패를 든 개 수인이며 출력 내부 ID가 `ShiledWarrior`로 오타가
있다. 사용자가 `ShieldKnight`, `ShiledWarrior`와 `DogShieldWarrior`가 같은 캐릭터임을 2026-08-10
확정했다.

기존 생산 과정에서 원하는 품질이 나오지 않아 보류한 캐릭터다. 외부 `master.png`와 PerfectPixel 결과를
정식 Master·게임 리소스로 복구하지 않고 실패 이력으로 보존한다. 재개 조건은 새 Master 생성과 사용자 승인,
그 Master를 기준으로 한 애니메이션 재생산이다.

## 중첩 보정 자료

`c_RabbitHealer` 아래에는 다음 128px 생산 보정본이 함께 있다.

```text
ElfArcher/ElfArcher.production-base.png
MoleMiner/MoleMiner.production-base.png
RabbitHealer/RabbitHealer.production-base.png
RockGolem/RockGolem.production-base.png
character/character.production-base.png
```

이 파일들은 calibration/scale-profile과 함께 있는 크기 보정 결과이며 Master가 아니다. 해당 Actor 패키지의
상대 크기 검증 자료로만 사용한다.

## 후속 우선순위

1. ElfArcher 제작 패키지를 만들 때 외부 Master를 복구·승인 검토한다.
2. ElfGuardian 프로젝트 v1~v6의 실제 승인본을 먼저 확정한다.
3. DogShieldWarrior는 새 Master 재생성 요청이 있을 때 Hold를 해제하고 기존 결과와 비교한다.
4. CatKnight는 새 애니메이션 추가가 필요할 때 Master 재생성 여부를 결정한다.

외부 폴더는 감사 당시 그대로 두었으며 삭제·이동·이름 변경하지 않았다.
