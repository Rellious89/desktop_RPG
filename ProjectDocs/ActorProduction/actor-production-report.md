# Actor Production Tracker

> 로컬 파일을 읽어 만든 읽기 전용 스캔 결과입니다. 패키지 완료도는 새 패키지 규칙으로의 이행 진행률이며, 기존 자산이 없다는 뜻이 아닙니다.

스캔 리비전: `b332b864d789` · 갱신: `node Tools/ActorProductionTracker/scan.mjs` 또는 Tracker 열기 도구를 실행하세요.

## 지금 진행하기 좋은 작업

1. **Barbarian** — 필수 애니메이션 프레임 제작; 후속 공백: Attack B (Tier 2) 프레임. (필수 애니메이션 공백: Attack B (Tier 2))
2. **CatMage** — 필수 애니메이션 프레임 제작; 후속 공백: Attack B (Tier 2) 프레임. (필수 애니메이션 공백: Attack B (Tier 2))
3. **RabbitHealer** — 필수 애니메이션 프레임 제작; 후속 공백: Attack B (Tier 2) 프레임. (필수 애니메이션 공백: Attack B (Tier 2))

## 세 가지 진행도

- 설정/패키지 (엄격 규칙): 5/13 Active — index, brief, PerfectPixel 입력, measurements, motion brief, 승인/관찰 master를 모두 포함합니다.
- 리소스 (필수 애니메이션): 10/13 Active — Candidate/Test는 해당 없음, Hold는 표시만 하고 Active 지표에서 제외합니다.
- 게임 연결: 10/13 Active — Player는 MotionProfile + CharacterDefinition + 필수 공격 풀, Enemy는 유효 MotionProfile + table입니다.
- 공격 풀 연결: 3/6 Active Player — Tier 2는 Attack A+B 누적 등록까지 확인합니다.
- 몬스터 런타임: 7/7 Active Enemy · 모션 제작 준비: 7/7 — Defeat가 비어 있으면 정상적인 Fade-only로 판정합니다.

## 제작 단계

- Candidate: 26
- Brief: 7
- Master: 0
- Animation: 3
- Unity: 0
- Ready: 3
- Hold: 2
- Test: 3

## 애니메이션 공백 (Active)

- **Barbarian** — Attack B (Tier 2)
- **CatMage** — Attack B (Tier 2)
- **RabbitHealer** — Attack B (Tier 2)

## 공격 Tier 풀 공백 (Active Player)

- **Barbarian** — Attack A (Tier 1): 연결 · Attack B (Tier 2): 풀 없음
- **CatMage** — Attack A (Tier 1): 연결 · Attack B (Tier 2): 풀 없음
- **RabbitHealer** — Attack A (Tier 1): 연결 · Attack B (Tier 2): 풀 없음

## 몬스터 모션 점검 (Active Enemy)

- **HyenaRaider** — Idle 4f · Hit 2f/복귀 구분 · Defeat Fade-only · 제작 패키지 보강
- **MoleMiner** — Idle 4f · Hit 2f/복귀 구분 · Defeat Fade-only · 제작 패키지 보강
- **RockGolem** — Idle 4f · Hit 2f/복귀 구분 · Defeat Fade-only · 제작 패키지 보강 · 미참조 1f
- **Scarecrow** — Idle 4f · Hit 2f/복귀 구분 · Defeat Fade-only · 제작 패키지 보강
- **Specter** — Idle 4f · Hit 2f/복귀 구분 · Defeat Fade-only · 그대로 사용 가능
- **VenomCultist** — Idle 4f · Hit 2f/복귀 구분 · Defeat Fade-only · 그대로 사용 가능
- **Werewolf** — Idle 4f · Hit 2f/복귀 반복(승인) · Defeat Fade-only · 제작 패키지 보강 · 미참조 1f

### 몬스터 모션 수정 필요

- 없음

## 후보 뱅크 (요약)

- Boss: 3명 — IronjawCrocodileLord, MoleVaultBaron, WhiteTigerWarlord
- Elite: 4명 — CobraHexPriest, HyenaPackMaster, MoleTunnelForeman, RhinoSiegeBreaker
- Normal: 8명 — BaboonStoneSlinger, JackalGraveRaider, MonitorLizardAmbusher, RatLootRunner, SkunkMireBrewer, WeaselCutpurse, WildcatBandit, WolverineRipper
- Player: 2명 — DragonWarrior, FoxArcher
- Player candidate: 9명 — BadgerForgeWarden, BearPawMonk, BoarHammerVanguard, ElephantBellKeeper, HedgehogNeedleDuelist, LionBannerCaptain, RaccoonRelicScout, RamWardCleric, SquirrelSlingScout

## Actor index

- **Barbarian** (Animation) — 패키지 100% · 리소스 67% · 게임 67% · 누락: Attack B (Tier 2)
- **CatKnight** (Brief) — 패키지 0% · 리소스 100% · 게임 100% · 누락: 없음
- **CatMage** (Animation) — 패키지 100% · 리소스 67% · 게임 67% · 누락: Attack B (Tier 2)
- **DogShieldWarrior** (Hold) — 패키지 0% · 리소스 0% · 게임 0% · 누락: Base Idle, Attack A (Tier 1), Attack B (Tier 2)
- **ElfArcher** (Brief) — 패키지 0% · 리소스 100% · 게임 100% · 누락: 없음
- **ElfGuardian** (Ready) — 패키지 50% · 리소스 100% · 게임 100% · 누락: 없음
- **HyenaRaider** (Brief) — 패키지 17% · 리소스 100% · 게임 100% · 누락: 없음
- **MoleMiner** (Brief) — 패키지 17% · 리소스 100% · 게임 100% · 누락: 없음
- **RabbitHealer** (Animation) — 패키지 100% · 리소스 67% · 게임 67% · 누락: Attack B (Tier 2)
- **RockGolem** (Brief) — 패키지 17% · 리소스 100% · 게임 100% · 누락: 없음
- **Scarecrow** (Brief) — 패키지 17% · 리소스 100% · 게임 100% · 누락: 없음
- **Specter** (Ready) — 패키지 100% · 리소스 100% · 게임 100% · 누락: 없음
- **StagGroveWarden** (Hold) — 패키지 100% · 리소스 0% · 게임 0% · 누락: Base Idle, Attack A (Tier 1), Attack B (Tier 2)
- **Test_Gblin** (Test) — 패키지 0% · 리소스 N/A · 게임 100% · 누락: 없음
- **Test_IceMage** (Test) — 패키지 0% · 리소스 N/A · 게임 100% · 누락: 없음
- **Test_Leopard** (Test) — 패키지 0% · 리소스 N/A · 게임 100% · 누락: 없음
- **VenomCultist** (Ready) — 패키지 100% · 리소스 100% · 게임 100% · 누락: 없음
- **Werewolf** (Brief) — 패키지 17% · 리소스 100% · 게임 100% · 누락: 없음
