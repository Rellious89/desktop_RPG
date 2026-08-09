# StagGroveWarden Master Measurements

> 상태: Approved / Master v1
>
> 승인일: 2026-08-10
>
> 생산 프로필: KeyBuddy V2 Pilot

## 승인 파일

```text
Assets/Art/Character/StagGroveWarden/master/StagGroveWarden-master-v1.png
```

승인본은 Codex built-in ImageGen의 1254×1254 소스에서 단색 배경을 제거하고, soft matte 결과의 알파를
0 또는 255로 정리한 투명 RGBA 이미지다. 화면상 디자인과 색은 양자화하지 않았다.

## Master 측정

```text
Master canvas: 1254×1254 RGBA
Opaque bounds: x 335–936 / y 148–1116
Occupied width: 602px
Occupied height: 969px
Facing: screen-right
Approved view: slight three-quarter
Transparent corner alpha: 0 / 0 / 0 / 0
Partially transparent pixels: 0
Visible magenta-like pixels: 0
Light direction: upper-left
External shadow: none
```

Master는 PerfectPixel 참조 입력이며 1254px 점유값을 Unity 크기로 직접 사용하지 않는다.

## V2 프레이밍 기준

승인 Master를 128×128 캔버스에서 높이 86px로 프레이밍한 검토본:

```text
ProjectDocs/ArtPipeline/Characters/StagGroveWarden/Prototypes/v2-master-01/
StagGroveWarden-v2-128-framing-preview-01.png
```

```text
Preview canvas: 128×128 RGBA
Opaque bounds: x 37–89 / y 12–97
Occupied width: 53px
Occupied height: 86px
Left safe margin: 37px
Right safe margin: 38px
Top safe margin: 12px
Ground-contact line: y 98 from top / 30px from bottom
V2 pivot candidate: X 0.5 / Y 0.234
```

IceMage V2 비교:

| Actor | 불투명 영역 | 폭 | 높이 | 접지선 아래 여백 |
|---|---|---:|---:|---:|
| IceMage Base | `(38,26)–(89,97)` | 51px | 72px | 30px |
| StagGroveWarden | `(37,12)–(89,97)` | 53px | 86px | 30px |

수사슴은 IceMage와 폭과 접지선이 같고, 뿔로 인해 위쪽으로 14px 더 높다. 몸통을 과도하게 키우지 않고 종족
장식만 추가한다는 목표와 일치한다.

## 최종 V2 출력 목표

```text
Canvas: 128×128
Body height excluding antlers: approximately 68–72px
Total height including antlers: approximately 84–88px
Approved target: 86px
Opaque palette target: approximately 32–48 colors
PPU: 50
Pivot: (0.5, 0.234) candidate
Filter: Point
Compression: None
```

현재 128px 프레이밍 검토본은 1,888색이므로 최종 Unity 납품본이 아니다. 승인 고해상도 Master를
PerfectPixel에 입력한 뒤 픽셀 밀도, 팔레트, 접지선과 뿔 높이를 검수한다.

## Master 잠금 요소

- 뿔 한쪽당 큰 가지 1개와 가지 끝 2~3개의 단순 구조
- 화면 왼쪽 큰 귀와 반대편 귀의 3/4 원근 관계
- 적갈색 털, 크림색 얼굴·목 털과 호박색 눈
- 짙은 녹색 튜닉, 갈색 가죽 조끼와 올리브색 한쪽 망토
- 황동색 원형 망토 고정핀
- 화면 오른쪽 손의 세로형 목재 지팡이
- 지팡이 끝의 작은 청록색 마법석 1개
- 양발의 갈라진 발굽과 동일 접지선

PerfectPixel 출력에서 위 요소가 프레임마다 달라지면 수정 또는 Reject한다.
