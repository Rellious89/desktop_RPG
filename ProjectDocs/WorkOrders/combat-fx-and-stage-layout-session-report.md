# 이펙트 재생 구조 / 필드 전환 연출 / Stage Layout 도구 세션 보고서

작업일: 2026-08-06 ~ 2026-08-07
커밋: `56cb77cd` (타격 이펙트 재생 시스템과 필드 전환 연출 추가), `a7195e98` (메시지 누락)
대상 씬: `Assets/Scenes/desktopScene_ReSize.unity` (UI 재배치 작업용 사본)

씬 파일과 프리팹 연결은 **건드리지 않았다**(Unity 에디터가 프로젝트 락을 잡고 있다).
코드/셰이더/머티리얼만 작성했고, 씬 배치는 6장 체크리스트로 남긴다.

---

## 0. 한눈에 보기

| # | 작업 | 상태 |
|---|---|---|
| 1 | 발사체 Fade 조사 | 조사만 - 구현 보류 |
| 2 | 타격 이펙트 온전 재생 구조 | 완료 (코드) |
| 3 | 공격별 Hit Effect Jitter 오버라이드 | 완료 (코드) |
| 4 | 몬스터 사망 이펙트 | 완료 (코드) - 프리팹 대기 |
| 5 | 필드 전환 연출(픽셀 디졸브 + 낙하) | 완료 (코드) - 씬 연결 대기 |
| 6 | Stage Layout 에디터 윈도우 | 완료 |
| 7 | 30fps 제한 검토 | 조사만 - 현행 유지 결정 |
| 8 | 캐릭터 아트 파이프라인 문제 규명 | 조사만 - 별도 설계 예정 |

---

## 1. 발사체 Fade 조사 (구현 없음)

`ProjectileSpriteAnimation`의 Fade In/Out이 값을 올려도 반응하지 않는 원인을 규명했다.

```csharp
fadeEnabledForCurrentFlight = (fadeInRatio > 0f || fadeOutRatio > 0f)
                              && flightDuration >= minFadeFlightDuration;   // 0.12초
```

CatMage 기준 비행 시간은 `(hitFrame 1 - castFrame 0) / 18fps = 0.056초`라 **게이트를 넘지 못해 Fade가
통째로 무시되고 있었다.** 값이 저장되지 않은 것이 아니라 적용 자체가 안 된 것이다.

프로젝트 전 공격 모션이 3프레임 @ 18fps로 통일돼 있어, Cast→Hit을 최대로 벌려도 `2/18 = 0.111초`로
게이트를 넘길 수 없다. 렌더 캡이 30fps라 비행 중 실제로 그려지는 횟수도 약 1.7회다.

**결론**: 현재 기본공격 구조에서 Fade는 활용 불가. 스킬 공격처럼 모션이 긴 경우에 다시 검토한다.
코드는 그대로 두었다.

---

## 2. 타격 이펙트 온전 재생 구조

### 문제

6프레임 @ 12fps(0.5초) 폭발 이펙트가 2프레임만 재생되고 사라졌다. `HitEffectSpawner`가
`defaultDuration`(0.15초) 타이머로 일괄 회수하고 있었기 때문이다.

그 0.15초는 아트가 없던 시절 `HitEffectPop`(더미 팝) 연출의 **재생 속도**를 잡으라고 둔 값이라
실제 클립 길이와 맞을 이유가 없었다. 인스펙터 툴팁의 "0.1~0.2 권장"도 그 시절 문구였다.

### 해결 - 이펙트가 자기 길이를 소유한다

신규 `IHitEffectPlayback` 인터페이스를 두고, 스포너는 **완료 통보를 기다리기만** 한다.

```csharp
float Duration { get; }
void Play(float scaleMultiplier, Action<IHitEffectPlayback> onComplete);
Sprite GetFrameAt(float elapsed);   // 프리뷰용 순수 조회
```

| 파일 | 내용 |
|---|---|
| `Assets/Scripts/Common/IHitEffectPlayback.cs` | 신규 - 계약 |
| `Assets/Scripts/Common/HitEffectSpriteAnimation.cs` | 신규 - 스프라이트시트 재생. `frames.Length / fps`로 길이 자동 계산 |
| `Assets/Scripts/Common/HitEffectPop.cs` | 인터페이스 구현으로 이관. 자기 `duration` 필드를 길이로 보고 |
| `Assets/Scripts/Common/HitEffectSpawner.cs` | 타이머 → 완료 콜백. `defaultDuration`은 재생 컴포넌트 없는 prefab 전용 폴백으로 격하 |

`HitEffect_Basic.prefab`의 `duration`이 씬의 `defaultDuration`과 같은 0.15라 **기존 8개 모션의 타격감은
변하지 않는다.**

### 프리팹 전환

`fx_hit_FireExplosin.prefab`의 Animator를 `HitEffectSpriteAnimation`으로 교체하고 `.anim`의
6프레임 / 12fps를 옮겼다. Animator 방식은 모션 에디터가 프레임을 읽을 방법이 없어 프리뷰 지원이
불가능했다. 원본 `.anim`과 `.controller`는 삭제하지 않고 남겨두었다.

### 모션 에디터 프리뷰

기존에는 정확히 Hit Frame인 순간에만 프리팹의 SpriteRenderer 스프라이트 한 장을 그렸다
(= 6프레임 이펙트도 0번 프레임이 한 순간 스쳐 지나갈 뿐이었다).

- 경과 시간 기반 프레임 재생으로 교체. 프레임 선택 규칙은 `GetFrameAt`에 되물어 **런타임과 구조적으로
  어긋날 수 없다**
- Cast 이펙트도 같은 헬퍼로 대칭 처리
- `GetPreviewDuration()`이 이펙트 길이까지 덮도록 확장 - 공격 모션 0.167초보다 이펙트 0.5초가 길어
  타임라인이 먼저 끝나 스크럽이 불가능했다

### 부수 정리

- `minSpawnInterval` **제거** - 50ms 안에 들어온 두 번째 타격의 이펙트가 조용히 생략되던 제한.
  도입 명분(Instantiate 비용)은 같은 커밋의 풀링으로 이미 해소돼 있었다.
  `DamageNumberSpawner`의 동명 필드는 별개 컴포넌트라 손대지 않았다.
- 매 Spawn마다 새로 할당되던 콜백 델리게이트를 한 번만 만들어 재사용

---

## 3. 공격별 Hit Effect Jitter 오버라이드

타격 이펙트가 흩어지는 랜덤 범위(`spawnJitterX/Y`)는 **맞는 몬스터**의 `HitEffectSpawner`에 있어서
어떤 공격으로 때리든 동일했다. 공격별로 조정 가능하게 배관을 이었다.

```
AttackMotionDefinition.OverrideHitEffectJitter / HitEffectJitter
  → AttackHitCue                                    (PlayerCharacterAnimator.cs:887)
  → Spawn(..., jitterOverride: Override ? 값 : null) (TargetCombatController.cs:554)
  → HitEffectSpawner.ResolveJitterRange()
```

`Vector2?`로 넘기는 것이 핵심이다. **`(0,0)`은 "랜덤 없이 정확히 한 점"이라는 정당한 값**이라
"지정 안 함"을 0으로 표현할 수 없다. 그래서 토글 + 값 구조를 택했다.

모션 에디터 Hit Presentation에 `Override Jitter` 체크박스와 `Effect Jitter`를 추가했고, 프리뷰에는
**주황색 점선 사각형**으로 범위를 표시한다. 랜덤값 자체를 그리지 않는 이유는 프리뷰가 결정론적이어야
Offset을 눈으로 맞출 수 있기 때문이다(데미지 숫자 Jitter를 프리뷰에서 항상 0으로 두는 것과 같은 규칙).

`impactPoint`가 없는 경로(Cast Effect용 스포너)에는 지터가 아예 적용되지 않던 구멍도 함께 메웠다.

**기존 공격 에셋 14개는 신규 키가 없어 `false`로 역직렬화되므로 동작이 완전히 동일하다.**

---

## 4. 몬스터 사망 이펙트

`HandleDefeated`는 Defeat 포즈 적용과 알파 페이드 두 가지만 하고 있었다. 이펙트도 사운드도 없었다.

`TargetCombatController`에 `Defeat Presentation` 섹션(Prefab / Offset / Scale)을 추가하고
`HandleDefeated`에서 한 번 생성한다.

```csharp
hitEffectSpawner.Spawn(defeatEffectPrefab,
    offsetOverride: defeatEffectOffset,
    scaleOverride: defeatEffectScale,
    jitterOverride: Vector2.zero);
```

`jitterOverride: Vector2.zero`가 중요하다. 스포너 기본 지터(±0.08)를 그대로 쓰면 죽을 때마다 다른
자리에 떠서 위치를 맞출 수 없다.

**위치가 몬스터를 따라가지 않는다.** `HitEffectSpawner`가 인스턴스를 `CombatFxRoot` 아래에 만들고
생성 시점 위치를 스냅샷으로 쓰기 때문이다 - 2슬롯 교대 방식(죽으면 곧바로 다음 몬스터가 밀려옴)에서
반드시 필요한 성질이라 클래스 주석에 이유를 남겼다.

몬스터별로 달라지면 나중에 `MonsterMotionProfile`로 올린다. 지금은 공용 1종 테스트 단계다.

---

## 5. 필드 전환 연출 (픽셀 디졸브 + 캐릭터 낙하)

마을↔던전 전환이 오브젝트 on/off라 연출이 없었다.

### 아키텍처 - 연출이 상태 전환을 감싼다

`FieldModeManager`는 동기 1프레임 전환기이고 "한 프레임에 전환은 한 번" 잠금이 걸려 있다.
`FieldModeRuntimeController`는 자기 문서에 **전환 연출은 여기서 하지 않는다**고 못박아 두었다.

그래서 신규 `FieldTransitionSequencer`가 바깥에서 시간을 만든다.

```
1. SetCombatEnabled(false)      ← 입력 즉시 차단
2. 디졸브 아웃 (여러 프레임)
3. TryEnterDungeon()            ← 기존 동기 전환 그대로, 손 안 댐
4. 들어오는 그룹 상태 복구
5. SetCombatEnabled(false)      ← 3번에서 켜졌을 수 있음. 같은 프레임이라 입력 샐 틈 없음
6. 낙하 (여러 프레임)
7. CanCombat이면 SetCombatEnabled(true)
```

기존 파일 수정은 **18줄**뿐이다. 진입점 2곳(`FieldModeManager.HandleDungeonEnterRequested`,
`FieldModeUIController.HandleReturnTownClicked`)이 시퀀서가 있으면 맡기고, 없으면 예전처럼 즉시
전환한다(**fail open** - 연출은 이동의 전제 조건이 아니다).

### 신규 파일

| 파일 | 내용 |
|---|---|
| `Assets/Art/Shaders/PixelDitherDissolve.shader` | 디더 디졸브. `clip()`으로 픽셀을 버림(알파 페이드 아님) |
| `Assets/Art/Shaders/PixelDitherDissolve.mat` | 공유 머티리얼 |
| `Assets/Scripts/Field/PixelDissolveGroup.cs` | 루트 아래 SpriteRenderer를 묶어 디졸브. 재생 시점에 머티리얼 교체/복원 |
| `Assets/Scripts/Field/CharacterDropIn.cs` | 머리 위에서 낙하 + 착지 스쿼시 |
| `Assets/Scripts/Field/FieldTransitionSequencer.cs` | 순서 소유자 |

### 설계 결정

- **디더 패턴은 텍스처 공간에 찍는다** - 화면 픽셀 기준으로 찍으면 배율이 바뀔 때마다 방충망 느낌이
  나고 오브젝트가 움직일 때 패턴이 미끄러진다
- **머티리얼은 재생 시점에 교체하고 되돌린다** - 씬/프리팹의 SpriteRenderer를 하나도 손대지 않아도 된다.
  진행도는 `MaterialPropertyBlock`으로 먹여 머티리얼 인스턴스가 새지 않는다
- **`PixelDissolveGroup.OnEnable`이 무조건 원상복구** - 사라진 채로 루트가 다시 켜져 필드가 통째로
  안 보이는 사고를 구조적으로 차단
- **낙하는 전투가 꺼진 상태에서만** - `AttackMovement`가 매 프레임 `localPosition`을 덮어쓰므로,
  `SetCombatEnabled(false)`로 `mode = None`이 된 뒤에야 Transform을 단독으로 쓸 수 있다.
  착지 지점은 계산하지 않고 그 시점의 현재 위치를 그대로 쓴다(배치 규칙이 바뀌어도 이 파일은 무영향)
- **디졸브 인은 만들지 않았다** - 들어오는 연출은 캐릭터 낙하가 담당한다
- **확장 여지**: 캐릭터 사라짐 / UI를 나중에 포함시키려면 그 오브젝트에 `PixelDissolveGroup`을 붙여
  시퀀서의 `Always Dissolve Groups` 리스트에 넣기만 하면 된다. 순서 코드는 그대로

### 사후 수정한 버그 2건

**① 몬스터 X 플립이 풀림** - 머티리얼에 GPU 인스턴싱을 켜두고 `_Flip` 적용을 빠뜨렸다.
Unity의 `Sprites-Default`는 인스턴싱이 켜지면 flipX를 메시에 굽지 않고 `_Flip`으로 넘긴다.
씬에서 `m_FlipX: 1`인 몬스터만 증상이 나온 것이 이 가설과 일치했다.
→ **인스턴싱을 아예 제거**(대상이 수십 개라 이득이 없고, Sprites-Default와 같은 경로만 남김).

**② 디졸브 픽셀이 거대하게 보임** - 원인이 둘이었다.
- `_MainTex`가 `[PerRendererData]`인 경우 **Unity가 `_MainTex_TexelSize`를 갱신하지 않는다.**
  머티리얼에 꽂힌 텍스처(없음 = 더미) 기준의 엉뚱한 크기가 들어와 스프라이트가 몇 개의 거대한
  블록으로 쪼개졌다 → **텍스처 크기를 C#이 스프라이트에서 직접 읽어 전달**하도록 변경
- 아트마다 PPU가 달라(캐릭터 200, 마을 프롭 32) "원본 도트 1픽셀"의 실제 크기가 6배 넘게 차이났다
  → 칸 크기를 **월드 유닛으로 정하고 각 스프라이트의 PPU를 곱해 텍셀 수로 환산**

---

## 6. Stage Layout 에디터 윈도우 (`Tools/KeyBuddy/Stage Layout`)

`StageVisualRootController`는 런타임 전용이라(`[ExecuteAlways]` 없음) 에디터에서 보는 화면이 실제와
달랐다. `workArea`도 Win32에서만 들어와 에디터에는 값 자체가 없었다.

`[ExecuteAlways]`는 매 프레임 Transform을 덮어써서 수동 편집과 충돌하므로, Motion Editor의
"Apply Preview Layout to Open Stage"와 같은 **on-demand 적용** 방식을 택했다.

### 핵심 계산식

```
아트 1픽셀 → 화면 픽셀
  = PlacementBounds.Height × baseVisualScale × userScale / (2 × orthoSize × PPU)
```

`workAreaHeight`가 약분되어 **이 배율은 해상도와 무관하게 일정하다**(해상도는 위치에만 영향).
카메라 `orthographicSize`는 이 식의 분모일 뿐 단독으로는 의미가 없다.

### 기능

- 대상 자동 탐색 + 씬 이름 표시
- 기준 해상도(Game 뷰 자동 가져오기 / 프리셋 / 수동)
- 현재 설정과 적용될 Scale / 스테이지 박스 화면 좌표
- **아트 픽셀 배율 표** - `(PPU, 스테이지 기준 상대 스케일)`로 묶어 50/100/150% 결과를 표시.
  정수 확대뿐 아니라 **정수 분의 1 축소(1/2, 1/3)도 ✓**로 표시(출력 픽셀이 입력 N×N의 평균이라 깨끗)
- **픽셀 밀도 감지** - png를 직접 디코드해 N×N 블록 격자를 찾는다. N>1이면 1/N 무손실 축소 가능
- 화면 크기 토글 단계 추천
- **런타임 배치를 씬에 적용** (Undo 지원) / 초기화
- 프로젝트 전체 Sprite PPU 분포 스캔

### 사후 수정

- Play 모드에서 `MarkSceneDirty` 예외 → OnGUI 중단 → "Invalid GUILayout state"까지 동반.
  세 호출을 가드된 헬퍼로 교체하고 Play 중에는 적용 버튼 대신 안내 표시
- 기준 PPU 필드가 매 프레임 초기화되던 문제
- 오브젝트 Transform 스케일이 배율 계산에서 누락되던 문제
- 프로젝트 스캔의 `t:Sprite` 검색이 텍스처 서브에셋을 잡지 못하던 문제

---

## 7. 조사만 하고 변경하지 않은 것

### 30fps 제한 - 현행 유지

`FpsLimiter`의 30fps는 최초 기술 검증 커밋(`e0ddd722`)에서 "CPU/GPU 점유율 절감" 목적으로 들어왔고,
저장소에 측정치는 없다. 게임 로직은 전부 `Time.deltaTime` 기반이라 60으로 올려도 타이밍은 그대로다.

다만 **이 앱의 비용은 게임이 아니라 창에서 나온다.**

- 창이 모니터 Work Area **전체**를 덮는다(`WS_EX_LAYERED` + `DwmExtendFrameIntoClientArea`)
- 씬은 GameObject 84개 / SpriteRenderer 3개 / `Update()` 12개로 사실상 무시할 수준
- 즉 프레임당 비용이 **해상도로 고정**돼 있고 프레임 수에 선형 비례한다 → 30→60은 지배 항목의 2배

"던전에서만 60" 방식도 검토했으나(`FieldModeManager.CanCombat`이 신호로 적합),
얻는 것 대비 비용이 크다고 판단해 **보류**했다. 코드 변경 없음.

### 캐릭터 아트 파이프라인 문제

`Stage Layout` 도구로 실측한 결과, 캐릭터 아트가 **표시 크기의 약 4배 해상도**로 제작돼 있었다.
런타임 1/4 축소라 밉맵을 켜도 자글거린다(필터 문제가 아니라 정보 손실).

idle 첫 프레임 10종 실측:

| 리소스 | 내용 크기 | 픽셀 밀도 | 무손실 축소 |
|---|---|---|---|
| CatMage | 189×258 | 1×1 | 불가 |
| ElfArcher | 188×246 | 2×2 | 1/2 |
| ElfGuardian | 252×252 | 1×1 | 불가 |
| RabbitHealer | 110×270 | 2×2 | 1/2 |
| HyenaRaider | 282×309 | 1×1 | 불가 |
| MoleMiner | 213×246 | 3×3 | 1/3 |
| RockGolem | 330×320 | 2×2 | 1/2 |
| Scarecrow | 162×258 | 1×1 | 불가 |
| Specter | 147×162 | 1×1 | 불가 |
| VenomCultist | 207×213 | 1×1 | 불가 |

**밀도가 제각각(1×1 6종, 2×2 3종, 3×3 1종)이라 일괄 축소가 불가능하다.** 내용 크기도 110×270 ~
330×320으로 3배 차이나서 캐릭터를 교체하면 화면 크기가 튄다.

검토된 방향: 캔버스 512→128, PPU 200→50. 월드 크기가 `2.56` 유닛으로 보존되어
**게임플레이 좌표(공격 이동 거리, Hit Effect Offset, 발사체 Launch Offset, 슬롯 위치)를 하나도
고치지 않아도 된다.**

**현재 결정**: 기존 리소스는 어차피 폐기 예정이므로 임포트 Max Size만 128로 낮춰 임시 사용한다.
정식 리소스는 파이프라인 확립 후 재제작한다.

미해결 논점: 캐릭터 내용 크기 규격, 생성 단계에서 목표 해상도를 직접 얻는 방법,
애니메이션 프레임 간 기준선(발 위치) 고정 규칙, 배경 프롭(Cainos PPU 32) 처리.

---

## 8. 씬 연결이 필요한 미완료 작업

코드는 전부 들어가 있으나 씬 배치가 남아 있다. **연결하지 않으면 기능이 조용히 비활성 상태다**
(에러는 나지 않는다).

- [ ] `TownFieldRoot` / `DungeonFieldRoot`에 `PixelDissolveGroup` 추가 →
      `Dissolve Material`에 `PixelDitherDissolve.mat` 연결
- [ ] `PlayerCharacter`에 `CharacterDropIn` 추가 → `Attack Movement` 연결
- [ ] 빈 오브젝트에 `FieldTransitionSequencer` 추가 →
      Field Mode Manager / Player Animator / 디졸브 그룹 2개 / Character Drop In 연결
- [ ] `Monster_Current` / `Monster_Standby`의 `Defeat Effect Prefab` 연결 (프리팹 미제작)
- [ ] 사망 이펙트 프리팹 제작 - `SpriteRenderer` + `HitEffectSpriteAnimation`(frames + fps).
      길이는 자동 계산되므로 수명을 따로 적지 않는다

배치를 새로 잡는 중이라면 `windowplacement.json`(`Application.persistentDataPath`)을 지우고
확인해야 한다 - 저장된 배치가 씬 배치를 이긴다.

---

## 9. 검증 방법과 한계

모든 C# 변경은 Unity 2022.3.62f3 어셈블리로 직접 컴파일 검증했다.

```bash
csc -target:library -nostdlib -noconfig -langversion:latest \
    -define:UNITY_STANDALONE_WIN;UNITY_EDITOR;UNITY_2022_3_OR_NEWER \
    -r:<UnityEngine 모듈 전체> -r:<Library/ScriptAssemblies/*> ...
```

| 대상 | 결과 |
|---|---|
| `Assets/Scripts` (134개 파일) | 에러 0 |
| `Assets/Editor` | 에러 0 |
| 픽셀 밀도 감지 알고리즘 | 동일 로직을 Python으로 재현해 실제 아트 10종 실측 |
| 배율/크기 계산식 | 수식 전개 및 수치 검증 |

**컴파일까지가 확인 가능한 범위다.** 아래는 전부 Unity/Windows에서 직접 확인해야 한다.

- 셰이더 컴파일 (HLSL은 Unity 임포트 시점에 컴파일된다)
- 디졸브가 실제로 의도한 굵기·속도로 보이는지
- 낙하 높이/시간이 자연스러운지
- 사망 이펙트가 교대 후에도 죽은 자리에 남는지
- Stage Layout 창의 Game 뷰 해상도 자동 가져오기 (에디터 내부 API 리플렉션)
- 30fps vs 60fps 실제 점유율 (Windows 빌드, `dwm.exe` 포함 측정 필요)

---

## 10. 알아두면 좋은 구조 지식

이번 조사에서 확인된, 앞으로도 반복해서 쓰일 사실들.

- **`StagePlacementBounds`는 캔버스가 아니다.** 필드 3개짜리 데이터 홀더이며, 드래그 한계 /
  클릭 영역(footprint) / 스테이지 배율 계산의 분자로 쓰인다. `baseVisualScale`은 보이는 크기에만
  곱해지고 footprint에는 곱해지지 않지만, **`Height`는 양쪽 모두에 영향을 준다**
- **액터 위치는 런타임에 덮어써진다.** `CombatStageLayout` 슬롯 + 프로필 `ActorOffset`으로 재계산되므로
  씬에서 액터 Transform을 옮겨도 무의미하다. 다만 **중간 루트**(`CommonFieldRoot` 등)를 옮기는 것은
  `localPosition`이 부모 기준이라 그대로 유지된다(단, footprint와 모션 에디터 프리뷰는 따라오지 않는다)
- **UI 캔버스는 `Scale With Screen Size` / 1920×1080 / Match Height.** 1080p에서 스케일 팩터가 정확히
  1.0이라 지금 에디터에서 보이는 크기가 그대로 나온다 - UI가 화면을 꽉 채우는 것은 스케일러 설정이
  아니라 authoring 자체의 문제다
