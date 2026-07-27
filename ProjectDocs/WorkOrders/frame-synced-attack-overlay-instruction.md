# 프레임 동기화 공격 오버레이 구현 지시서

## 1. 목표

캐릭터 공격 스프라이트와 같은 프레임 인덱스 및 FPS를 사용하는 픽셀 이펙트 오버레이 트랙을 추가한다.

첫 검증 대상은 `RabbitHealer_Attack`이다. 현재 공격은 18fps, Hit Frame 1이며 연타 중 Frame 0과
Frame 1을 반복하고 입력이 끝날 때 Frame 2 Recovery를 보여준다. 오버레이도 별도 시간 계산 없이 이
공격 프레임 전환을 그대로 따라야 한다.

## 2. 확정 설계

### 2.1 기존 Presentation과 역할을 분리한다

- **Frame-synced Overlay**: 시전자 캐릭터 위에 붙고, 공격 본체와 같은 프레임 인덱스를 사용한다.
  검 궤적, 지팡이 잔상, 손끝 마력처럼 캐릭터 자세와 정확히 맞아야 하는 효과가 대상이다.
- **Cast Effect Prefab**: 기존처럼 `castFrameIndex`에서 한 번 생성되는 시전자 기준 일회성 효과다.
- **Hit Effect Prefab**: 기존처럼 `hitFrameIndex`에서 한 번 생성되는 피격 지점 기준 일회성 효과다.

Frame-synced Overlay를 Cast/Hit prefab으로 흉내내지 않는다. 반대로 기존 Cast/Hit prefab 동작도
변경하거나 제거하지 않는다.

### 2.2 오버레이는 독립 FPS를 갖지 않는다

- `AttackMotionDefinition.frames[index]`와 `overlayFrames[index]`가 한 쌍이다.
- `overlayFrames`에는 별도의 FPS, 시작 프레임, 지속시간과 반복 설정을 추가하지 않는다.
- 공격 본체가 Frame 0이면 Overlay 0, Frame 1이면 Overlay 1을 표시한다.
- 오버레이가 필요 없는 프레임은 배열 요소를 `null`로 둔다.
- 오버레이 배열이 비었으면 기존 공격과 완전히 동일하게 동작한다.

## 3. 데이터 변경

대상:

- `Assets/Scripts/Character/AttackMotionDefinition.cs`
- `Assets/Scripts/Character/IAttackMotion.cs`
- `Assets/Scripts/Character/PlayerCharacterAnimator.cs` 안의 레거시 `AttackAnimation`

### 3.1 필드와 인터페이스

`AttackMotionDefinition`에 직렬화 필드를 추가한다.

```csharp
[Header("Frame-synced Overlay")]
[Tooltip("공격 본체 frames와 같은 인덱스를 사용하는 오버레이 스프라이트. 비어 있거나 해당 요소가 null이면 그 프레임에는 오버레이가 없다.")]
[SerializeField] private Sprite[] overlayFrames = Array.Empty<Sprite>();

public Sprite[] OverlayFrames => overlayFrames ?? Array.Empty<Sprite>();
```

`IAttackMotion`에도 `Sprite[] OverlayFrames { get; }`를 추가하고, 레거시 `AttackAnimation`에도 같은
필드와 프로퍼티를 추가한다. 기존 에셋은 배열이 비어 있는 상태로 역직렬화되어야 하며 동작 변화가 없어야 한다.

### 3.2 길이 불일치 안전 규칙

- 런타임은 `overlayFrames.Length == frames.Length`를 전제로 크래시하면 안 된다.
- 현재 공격 프레임이 오버레이 범위 밖이면 `null`로 처리한다.
- Motion Editor에서는 배열이 비어 있지 않은데 본체 프레임 수와 다르면 경고한다.
- 에디터에 `Match Overlay Length` 또는 같은 의미의 버튼을 제공해 본체 프레임 수에 맞춰 배열을
  늘리거나 줄일 수 있게 한다. 늘어난 요소는 `null`이다.

## 4. 런타임 렌더링

### 4.1 재사용 SpriteRenderer 한 개

`PlayerCharacterAnimator`가 캐릭터별로 프레임 오버레이용 자식 `SpriteRenderer` 한 개를 소유한다.

- 씬에 수동 배치가 없어도 안전하게 동작하도록, 직렬화 참조가 비어 있으면 `Awake`에서
  `AttackFrameOverlay` 자식 오브젝트와 `SpriteRenderer`를 한 번 생성한다.
- 로컬 위치 `Vector3.zero`, 회전 `Quaternion.identity`, 스케일 `Vector3.one`을 사용한다.
- 본체 `SpriteRenderer`와 같은 Sorting Layer를 사용하고 Sorting Order는 본체보다 1 높게 둔다.
- 본체의 material, flipX/flipY 등 정렬에 필요한 값을 복사한다.
- 공격마다 Instantiate/Destroy하지 않는다.

### 4.2 프레임 적용

- `ApplyAttackFrame()`에서 본체 프레임을 적용한 직후 같은 `attackFrame` 인덱스의 오버레이를 적용한다.
- 오버레이 인덱스가 없거나 요소가 `null`이면 오버레이 renderer의 sprite를 `null`로 만든다.
- Idle 복귀, 공격 종료, 컴포넌트 비활성화 시 오버레이 sprite를 반드시 지운다.
- 콤보에서 `StartWindup()`이 새 공격 모션을 선택하면 새 모션의 OverlayFrames를 즉시 사용한다.
- 기존 공격 큐, HitPoint, Cast Cue, AttackMovement, 데미지와 사운드 로직은 변경하지 않는다.

## 5. Motion Editor 변경

대상: `Assets/Editor/MotionEditor/MotionEditorWindow.cs`

### 5.1 편집 UI

Attack 탭의 기존 Sprite Frames 목록에서 각 인덱스의 본체와 오버레이 관계를 한눈에 볼 수 있게 한다.

권장 행 구성:

```text
#0 / CAST   | Actor Sprite thumbnail + field | Overlay Sprite thumbnail + field
#1 / HIT    | Actor Sprite thumbnail + field | Overlay Sprite thumbnail + field
#2          | Actor Sprite thumbnail + field | Overlay Sprite thumbnail + field
```

- 별도 오버레이 FPS 필드를 만들지 않는다.
- 본체 프레임 추가·삭제·재정렬 시 오버레이 배열에도 같은 인덱스 작업을 적용해 쌍이 어긋나지 않게 한다.
- 본체 프레임 Drop Zone과 별도로 `Drop Overlay Sprites Here`를 제공해 순서대로 한 번에 등록할 수 있게 한다.
- 오버레이만 제거해도 본체 프레임은 유지되어야 한다.
- 현재 선택 프레임과 CAST/HIT 태그 동작은 유지한다.

평행 배열을 사용하되 에디터가 인덱스 정합성을 책임진다. 기존 `frames`를 `AttackFrame` 구조체 배열로
마이그레이션하는 대규모 데이터 변경은 이번 작업에서 하지 않는다.

### 5.2 Preview

- 공격 Preview의 현재 본체 프레임 인덱스를 그대로 사용해 오버레이 프레임을 고른다.
- 그리기 순서는 `Monster → Character → Frame Overlay → Cast/Hit Presentation`으로 한다.
- 오버레이는 캐릭터와 동일한 anchor, preview zoom, character scale을 사용한다.
- 오버레이 제작 규격상 본체와 캔버스·Pivot·PPU가 같으므로 별도 Offset/Scale 필드는 추가하지 않는다.
- 타임라인 스크럽, 한 프레임 이동과 Play 모두 같은 결과를 보여야 한다.

### 5.3 저장 전 변경 감지

Motion Editor의 `AttackSnapshot`과 unsaved changes 비교에 OverlayFrames를 포함한다. 오버레이만 바꾼 뒤
리소스나 탭을 이동할 때 변경 경고가 누락되면 안 된다.

## 6. RabbitHealer 리소스 연결

대상 에셋:

- `Assets/Data/MotionProfiles/Characters/RabbitHealer/RabbitHealer_Attack.asset`

연결할 스프라이트:

- `Assets/Art/Effects/RabbitHealer/AttackPrototype/v1/RabbitHealer-attack-vfx-00.png`
- `Assets/Art/Effects/RabbitHealer/AttackPrototype/v1/RabbitHealer-attack-vfx-01.png`
- `Assets/Art/Effects/RabbitHealer/AttackPrototype/v1/RabbitHealer-attack-vfx-02.png`

### 6.1 Import 설정

세 오버레이의 TextureImporter 설정은 RabbitHealer 공격 본체 프레임과 일치시킨다.

- Texture Type: Sprite (2D and UI), Sprite Mode: Single
- 512×512 원본 크기 유지, Mip Map 비활성화, Alpha Is Transparency 활성화
- Pixels Per Unit: 200
- Custom Pivot: `(0.5, 0.078)`
- Filter Mode, Compression, Mesh Type 등 나머지 표시 관련 설정도 본체 공격 프레임을 기준으로 복사한다.

오버레이와 본체가 같은 캔버스·Pivot·PPU라는 전제를 코드 Offset으로 보정하지 않는다.

## 7. 검증 항목

1. 기존 캐릭터와 기존 공격 에셋은 OverlayFrames가 비어 있어도 이전과 동일하게 재생된다.
2. Motion Editor에서 RabbitHealer Frame 0/1/2를 스크럽하면 각 오버레이가 정확히 겹친다.
3. Motion Editor 18fps Play에서 Frame 0 → 1 → 2가 본체와 이펙트 모두 같은 인덱스로 재생된다.
4. 실제 연타에서는 기존 규칙대로 Frame 0 ↔ Frame 1이 반복되고 오버레이도 정확히 0 ↔ 1을 반복한다.
5. 입력이 끝나 Recovery Frame 2로 넘어갈 때 Overlay 2가 표시되고 Idle 복귀 즉시 사라진다.
6. Overlay 요소 하나를 `null`로 두면 해당 프레임만 효과가 표시되지 않는다.
7. 공격 중 캐릭터 전환·비활성화가 발생해도 이전 오버레이가 남지 않는다.
8. Stage 크기 50%/100%/150%에서 본체와 오버레이의 상대 정렬이 유지된다.
9. 기존 Cast Effect, Hit Effect, Cast/Hit Sound와 타이밍이 변하지 않는다.
10. 연타 중 오버레이 인스턴스 생성이나 GC 할당이 반복되지 않는다.

## 8. 이번 작업에서 제외

- 독립적으로 날아가며 목표를 추적하는 Projectile 시스템
- 여러 개의 동시 오버레이 트랙
- 오버레이 전용 FPS, 반복 모드, 색상 애니메이션과 파티클
- 기존 HitEffectSpawner 또는 Cast/Hit prefab 구조 개편

현재 RabbitHealer의 노란 발사체 표현도 우선 Frame 1 오버레이에 포함해 빠른 공격 템포의 가독성을
검증한다. 실제 이동하는 발사체가 필요하다고 판단될 때 별도 Projectile 시스템으로 분리한다.
