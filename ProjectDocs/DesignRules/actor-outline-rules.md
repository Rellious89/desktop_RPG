# 캐릭터/몬스터 외곽선 규칙

투명 데스크톱 윈도우에서는 캐릭터 뒤에 어떤 화면이 오는지 알 수 없다. 밝은 웹페이지 위에서는 밝은 픽셀이, 어두운 배경화면 위에서는 어두운 픽셀이 묻혀 실루엣을 놓치기 쉬우므로, 본체 바깥에 얇은 외곽선을 그려 식별성을 확보한다.

이 문서는 프로토타입 단계의 규칙이며, 실효성 검증 결과에 따라 바뀔 수 있다. 그림자·블러·글로우·배경 분석 기반 동적 색상은 이번 범위에 없다.

## 구성 요소

| 파일 | 역할 |
| --- | --- |
| `Assets/Shaders/ActorOuterOutline.shader` | Built-in RP용 Sprite 외곽선 셰이더(`KeyBuddy/Actor Outer Outline`) |
| `Assets/Materials/ActorOuterOutline.mat` | 모든 액터가 **공유**하는 Material |
| `Assets/Scripts/Common/ActorOutlineSettings.cs` | 씬에 하나뿐인 **전역 설정**(On/Off, 색상, 두께, Material). `StageVisualRoot`에 배치 |
| `Assets/Scripts/Common/ActorOutlineController.cs` | 본체 SpriteRenderer에 붙어 전역 설정을 자기 MaterialPropertyBlock에 적용 |

## 외곽선 계산 방식

셰이더는 배경을 전혀 읽지 않는다(GrabPass 금지). 스프라이트 자신의 알파만 본다.

1. 현재 픽셀이 불투명하면(`alpha >= 0.999`) 주변 검사를 건너뛰고 원본 Sprite를 `Sprites/Default`와 완전히 동일하게 출력한다.
2. 투명한 픽셀이면 주변 8방향의 알파를 읽어 최댓값이 `_OutlineAlphaCutoff`(기본 0.1) 이상이면 외곽선 색을 출력한다.
3. 주변까지 전부 투명하면 완전히 투명하게 출력한다.

- 두께 1은 3x3 이웃 8칸만 본다.
- 두께 2는 3x3 안쪽 링 8칸 + 5x5 바깥 테두리 16칸을 합쳐 **5x5 영역 24칸을 빠짐없이** 검사한다. 바깥 테두리에는 축 `(±2,0)/(0,±2)`, 모서리 `(±2,±2)`뿐 아니라 `(±2,±1)`, `(±1,±2)` 8칸도 포함된다 — 이 8칸이 빠지면 그 방향으로만 이웃한 얇거나 비스듬한 실루엣에서 외곽선에 구멍이 생긴다.
- 판정은 `step()` 임계값이라 출력이 항상 "외곽선 색" 아니면 "없음"이다 — 블러 진 그라데이션이 생기지 않는다.
- 반투명 픽셀은 원본을 외곽선 **위에** 얹는 프리멀티플라이드 Over 합성으로 처리한다. 불투명 영역은 `1 - c.a`가 0이 되어 외곽선 기여가 정확히 0이므로 **원본 색을 절대 덮지 않는다.**
- 주변 샘플링은 `tex2D`가 아니라 `tex2Dlod`를 쓴다. 이 샘플링이 "이 픽셀이 투명한가"로 갈라지는 분기 안에 있어서, 밉 레벨 미분값이 정의되지 않는 문제를 피하기 위함이다(스프라이트는 밉맵이 없어 LOD 0 고정이 정확히 같은 결과다).
- 블렌딩은 `Blend One OneMinusSrcAlpha`로 `Sprites/Default`와 동일하다 — 투명 윈도우 합성 결과가 기존 스프라이트와 같아야 하므로 바꾸지 않는다.

## Sprite Mesh Type = Full Rect (필수 전제)

외곽선은 Sprite 메시가 실제로 래스터화하는 영역 안에서만 그릴 수 있다. 임포트 설정이 **Mesh Type = Tight**면 메시가 알파 실루엣에 딱 붙어 있어 바깥쪽 외곽선이 잘려 거의 보이지 않는다.

그래서 캐릭터/몬스터 **프레임** 스프라이트의 임포트 설정을 `spriteMeshType: 1`(Tight) → `0`(Full Rect)으로 바꿨다. PNG 원본 이미지는 손대지 않았고, `.meta`의 임포트 옵션만 바뀐다.

- 대상: `Assets/Art/Character/**`, `Assets/Art/Enemy/**` 중 프레임 폴더(idle/idle_a/idle_b/attack/hit 등) — 164개
- 제외: `master/`, `concept/` 원본 시트, `Assets/Art/Effects`, `Assets/Art/UI`
- Sprite가 텍스처 가장자리에 붙어 있으면 그쪽 외곽선은 그려질 자리가 없어 잘린다. 현재 캐릭터/몬스터 프레임은 투명 여백이 충분해 문제되지 않으며, 프로토타입을 위해 원본 이미지에 별도 패딩을 추가하지 않았다.

## 전역 설정 (`ActorOutlineSettings`)

On/Off·색상·두께는 액터마다 따로 두지 않고 씬에 하나뿐인 `ActorOutlineSettings`에서 관리한다. `StageVisualRoot`에 배치돼 있고, **캐릭터와 몬스터가 완전히 같은 값을 쓴다**(액터별 Override는 두지 않는다).

- 각 `ActorOutlineController`는 값을 소유하지 않고 이 컴포넌트에서 읽어 자기 SpriteRenderer의 프로퍼티 블록에 넣는다.
- 값이 바뀌면 `ActorOutlineSettings.Changed`(정적 이벤트)로 알린다. 컨트롤러는 `OnEnable`에서 구독하고 `OnDisable`에서 해지하므로, Play 모드에서 Inspector를 만지면 **활성 상태인 모든 캐릭터와 몬스터에 즉시** 반영된다.
- Edit 모드에서는 컨트롤러의 `OnEnable`이 돌지 않아 구독자가 없다. 그래서 `OnValidate`가 씬의 활성 컨트롤러를 직접 찾아 `Refresh()`를 호출한다. 같은 이유로 `ActorOutlineSettings.Active`는 Edit 모드에서만 `FindObjectOfType`으로 인스턴스를 보충한다.
- 런타임 진입점으로 `SetOutlineEnabled/SetOutlineColor/SetOutlineWidth`가 있다(향후 사용자 설정 UI용). 셋 다 변경 즉시 전체에 반영된다.
- 설정 컴포넌트가 없거나 꺼져 있거나 Material이 비어 있으면 `Active`가 null이 되고, 모든 컨트롤러는 외곽선 적용 이전 상태로 돌아간다.

## Material 인스턴스 정책

- Material은 **공유**한다. `ActorOutlineController`가 `sharedMaterial`에 대입할 뿐이며, `renderer.material`은 절대 쓰지 않는다 — 그 프로퍼티를 읽는 순간 캐릭터마다 Material 사본이 생긴다.
- 액터별로 달라질 수 있는 값(On/Off, 색상, 두께)은 `MaterialPropertyBlock`으로 덮어쓴다. 프로퍼티 블록은 렌더러에 붙는 값이라 Material을 복제하지 않는다.
- SpriteRenderer가 내부적으로 넘기는 `_RendererColor`/`_Flip`은 엔진 전용 상수 버퍼(`UnityPerDrawSprite`)에 있어 사용자 프로퍼티 블록과 충돌하지 않는다 — 즉 블록을 설정해도 `SpriteRenderer.color`(Flash/Fade)와 `flipX`/`flipY`가 그대로 동작한다.
- 전역 설정이 없거나 꺼져 있거나 `outlineMaterial`이 비어 있으면 직렬화해둔 원래 Material로 되돌린다 — 외곽선 적용 전과 완전히 동일한 표시가 된다.
- `Initialize()`는 "한 번 했다"는 플래그와 무관하게 매번 `spriteRenderer`/`propertyBlock`의 null을 확인해 복구한다. 에디터 재컴파일/도메인 리로드 뒤에는 직렬화되지 않는 캐시 필드가 비워진 채로 `OnValidate → ApplyFromEditor`가 먼저 불릴 수 있는데, 그때 `GetPropertyBlock(null)`로 예외가 나는 것을 막기 위함이다. 원본 Material 기록만 별도 플래그로 딱 한 번 시도한다(이미 외곽선이 적용된 뒤에 기록하면 안 되므로).
- 컴포넌트가 비활성화될 때(`OnDisable`)도 원래 `sharedMaterial`을 복원하고 프로퍼티 블록을 지운다. 캐릭터 교체(`RuntimeCharacterSwitcher`의 `SetActive`)로 꺼진 캐릭터가 외곽선 Material과 프로퍼티를 물고 있지 않게 하기 위함이다. 다시 켜지면 `OnEnable`의 `Apply()`가 처음부터 다시 적용한다. 단 `OnDisable`에서는 fallback Material을 새로 만들지 않는다 — 씬 언로드/도메인 리로드 중 오브젝트 생성은 Unity가 정리 누락 경고를 내므로, 기록해둔 원본이 있을 때만 되돌리고 없으면 프로퍼티 블록만 정리한다.

## Flash / Fade 호환

본체 `SpriteRenderer.color`는 `FlashOnCue`(공격/피격 플래시), 몬스터 처치 Fade-out, 리젠 Fade-in이 함께 쓴다. 셰이더는 이 값을 `IN.color`(= 정점 색 × `_Color` × `_RendererColor`)로 받는다.

- **본체**: `Sprites/Default`와 동일하게 `tex * IN.color`라 기존 색 변화가 그대로 재현된다.
- **외곽선 RGB**: `_OutlineColor.rgb`를 그대로 쓴다 — Flash로 본체가 물들어도 외곽선 색은 설정값을 유지한다.
- **외곽선 알파**: `_OutlineColor.a × IN.color.a` — SpriteRenderer 전체 알파를 따라간다. 몬스터 Fade-out 시 본체만 사라지고 외곽선이 남는 일이 없고, 리젠 시 함께 나타난다.

## 적용 범위

**적용**: 캐릭터 본체 SpriteRenderer, 몬스터 본체 SpriteRenderer.

**적용 제외**: `AttackFrameOverlay`(캐스팅 섬광·무기 잔상), 발사체, 피격 이펙트, 데미지 숫자, UI.

`PlayerCharacterAnimator.EnsureAttackFrameOverlay()`는 예전에 본체 `sharedMaterial`을 오버레이에 복사했는데, 본체에 외곽선 Material이 붙으면 캐스팅·잔상까지 외곽선 처리되므로 **Material 복사를 제거했다.** 오버레이는 자기 Material을 유지한다(런타임에 만든 SpriteRenderer는 `Sprites/Default`). `attackOverlayMaterial`을 채워두면 그 값으로 명시 지정한다. 오버레이가 본체에서 물려받는 값은 Sorting Layer, Sorting Order +1, flipX, flipY, Transform뿐이다.

## 기본값

`StageVisualRoot`의 `ActorOutlineSettings`에서 조정한다(Material에도 같은 값이 기본으로 들어 있어, 설정 컴포넌트 없이 Material만 붙여도 같은 모양이 나온다).

| 항목 | 값 |
| --- | --- |
| Outline Enabled | On |
| Outline Width | 1 텍스처 픽셀 (범위 1~2) |
| Outline Color | `(0.863, 0.933, 1.0)` 옅은 하늘색 |
| Outline Alpha | 0.85 |
| Source Alpha Cutoff | 0.1 |

Stage 50%에서 1픽셀이 보이지 않으면 2픽셀과 비교한다. 반대로 150%에서 2픽셀이 너무 두꺼우면 1픽셀로 되돌린다.

## 알려진 제약

- 액터 스프라이트의 Filter Mode가 Bilinear(`filterMode: 1`)라 알파 경계가 부드럽게 번진다. 셰이더가 임계값으로 잘라내므로 출력 색은 블러 지지 않지만, 외곽선 경계 위치는 확대 배율에 따라 반 픽셀 정도 흔들릴 수 있다. 완전한 픽셀 퍼펙트가 필요하면 Filter Mode를 Point로 바꿔야 하는데, 이는 기존 아트 전체의 화풍을 바꾸는 결정이라 이번 범위에서 제외했다.
- 외곽선은 Sprite 텍스처의 투명 여백 안에서만 그려진다(위 Full Rect 항목 참고).
