# StagGroveWarden V2 Master Candidate 01

> 상태: Master Candidate 01 사용자 승인 완료 / Master v1 승격
>
> 생성일: 2026-08-10
>
> 생성 방식: Codex built-in ImageGen

## 파일

| 파일 | 용도 |
|---|---|
| `StagGroveWarden-master-candidate-01-chroma.png` | ImageGen 원본 / 단색 마젠타 배경 |
| `StagGroveWarden-master-candidate-01.png` | Soft matte 배경 제거본 / 시각 검토용 |
| `StagGroveWarden-master-candidate-01-hard-alpha-v2.png` | 반투명 픽셀과 마젠타 잔여가 없는 승인 권장 Master 후보 |
| `StagGroveWarden-v2-128-framing-preview-01.png` | 권장 Master의 128×128 프레이밍 검토용 Nearest Neighbor 축소본 |
| `StagGroveWarden-v2-128-preview-01.png` | Soft matte 기준 초기 프리뷰 / 비교 기록 |
| `StagGroveWarden-master-candidate-01-hard-alpha.png` | Hard key 직접 적용 실패 기록 / 배경 잔여로 사용 금지 |

승인 권장본은 2026-08-10 사용자 승인을 받아 아래 정식 Master로 복사했다.

```text
Assets/Art/Character/StagGroveWarden/master/StagGroveWarden-master-v1.png
```

128px 프레이밍 파일은 여전히 Unity 납품본이 아니며 PerfectPixel 출력 검수용 비교 자료로만 사용한다.

## 생성 입력

```text
Use case: stylized-concept
Asset type: KeyBuddy V2 game character Master/base sprite candidate

Create one full-body male anthropomorphic stag forest warden as a new character design. Use the existing
IceMage image only as a sprite-density, scale, framing, and three-quarter-view reference; do not copy its identity.

The stag faces screen-right in a weak three-quarter view. He has warm reddish-brown fur, a cream muzzle and
throat, a short deer muzzle, amber eyes, long outward ears, split hooves, a small tail, and simple antlers with
only two or three large tines per side. Use a compact cute SD body around 2.5 heads tall.

He wears a short dark forest-green tunic, fitted brown leather vest, small leather guards, and a short olive
shoulder cape. He holds one short straight wooden warden staff vertically beside his body. Only the top branches
slightly like a twig and contains one small restrained pale-cyan magical accent. The staff has no blade.

Both hooves are readable on one ground-contact line. Keep antlers, ears, muzzle, torso, staff, hooves, cape and
tail separated. Use compact handcrafted retro pixel art, clean stepped dark outlines, limited large color planes,
upper-left lighting, and no antialiasing or painterly detail.

Generate on a perfectly uniform solid #ff00ff chroma-key background. Do not use magenta in the subject. No floor,
shadow, reflection, text, watermark, extra objects, long robe, heavy armor, bow, sword, spear, giant antlers,
quadruped anatomy, centaur anatomy, skull-deer features or persistent magic effects.
```

## 배경 제거

Codex ImageGen 기본 투명 이미지 절차에 따라 단색 배경을 생성한 뒤 로컬 helper로 RGBA를 만들었다.

```text
Detected key color: #f904f7
Source size: 1254×1254 RGB
Transparent result: 1254×1254 RGBA
Transparent pixels: 1,298,476 / 1,572,516
Partially transparent pixels: 9,889 / 1,572,516
```

Soft matte 결과를 알파 임계값으로 한 번 더 정리한
`StagGroveWarden-master-candidate-01-hard-alpha-v2.png`를 승인 권장본으로 사용한다.

```text
Alpha: 0 또는 255만 사용
Transparent corner alpha: 0 / 0 / 0 / 0
Visible magenta-like pixels: 0
Opaque bounds: (335,148)–(937,1117)
```

Hard key를 원본에 직접 적용한 `StagGroveWarden-master-candidate-01-hard-alpha.png`는 배경의 미세한 색 변화가
캔버스 경계에 남아 불투명 영역이 전체 캔버스로 확장됐다. 이 파일은 실패 기록이며 업로드하지 않는다.

## 128×128 프레이밍

권장 Master의 실제 불투명 영역을 기준으로 높이 86px로 Nearest Neighbor 축소하고, 캔버스 중앙과 접지선에
배치했다. 이 파일은 프레이밍 검토본이며 최종 픽셀 납품본이 아니다.

```text
Canvas: 128×128
Opaque bounds: x 37–89 / y 12–97
Opaque width: 53px
Opaque height: 86px
Ground-contact line: y 98 from top / 30px from bottom
Pivot candidate: X 0.5 / Y 0.234
```

IceMage V2 기준과의 비교:

| Actor | 불투명 영역 | 폭 | 높이 | 아래쪽 접지선 |
|---|---|---:|---:|---:|
| IceMage Base | `(38,26)–(89,98)` | 51px | 72px | 30px |
| StagGroveWarden Framing Preview | `(37,12)–(90,98)` | 53px | 86px | 30px |

수사슴 후보는 IceMage와 폭과 접지선이 사실상 같고, 뿔 때문에 위쪽으로 14px 더 높다. 신체 크기를
무리하게 키우지 않고 종족 장식 높이만 추가한다는 Brief의 목표와 일치한다.

색상 수는 IceMage Base 43색에 비해 프레이밍 프리뷰가 1,888색으로 많다. 따라서 이 128px 프리뷰를 Unity
납품본으로 사용하지 않는다. 승인된 고해상도 Master를 PerfectPixel에 입력하고, 출력 후 V2 팔레트와 픽셀
밀도를 맞추는 용도로만 사용한다.

## 시각 검토

### 통과

- 수사슴의 뿔, 귀, 주둥이와 발굽이 한눈에 읽힘
- 화면 오른쪽을 향한 약한 3/4 전신 자세
- 뿔과 지팡이가 겹치지 않음
- 지팡이에 날이 없고 작은 청록색 포인트만 존재
- 털, 녹색 천, 갈색 가죽과 목재가 분리됨
- 한 캐릭터만 존재하며 배경·그림자·텍스트가 없음
- 128px에서 얼굴, 발굽, 지팡이와 뿔의 핵심 형태가 유지됨
- IceMage와 동일한 발 접지선 및 유사한 화면 폭

### 사용자 판단 필요

- 망토가 Brief의 `짧은 어깨 망토`보다 약간 길다. 다리와 꼬리를 가리지는 않으므로 유지 가능하다.
- 신체가 목표한 2.5등신보다 약간 늘씬하게 느껴질 수 있다. 128px 표시에서는 IceMage와 무리 없이 어울리지만,
  더 귀여운 인상이 필요하면 머리를 키우고 몸통을 짧게 수정할 수 있다.
- 뿔은 읽기 쉽지만 실제 사슴보다 단순화가 강하다. 애니메이션 일관성 측면에서는 현재 형태가 유리하다.

## 다음 단계

Master 승인 이후:

1. `StagGroveWarden-master-v1.png`를 PerfectPixel 기준 이미지로 사용한다.
2. Idle 4프레임 입력은 `02_perfectpixel-input.md`와 `04_motion-idle.md`를 따른다.
3. 프레임마다 뿔 가지 수, 지팡이 길이, 망토 길이와 접지선을 고정한다.
4. 출력 후 128×128 Base의 색상과 프레이밍을 확정한다.
5. Unity Import는 PPU 50, Pivot `(0.5, 0.234)` 후보로 검증한다.

수정이 필요하면 한 번에 한 항목만 바꾼다. 우선순위는 `체형 → 망토 길이 → 세부 색상` 순이다.
