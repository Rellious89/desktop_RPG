# CopperAxeBarbarian — Canvas Occupancy Experiment

> 상태: 가설 검증 중. 공통 규칙으로 확정하지 않는다.

## 관찰

- PerfectPixel 최대 Frame cell size는 512×512다.
- 현재 바바리안 기준 이미지는 캔버스를 크게 점유한다.
- Chest Tap 생성에서 일부 프레임의 캐릭터 크기가 달라졌다.
- 공격처럼 팔다리와 무기가 멀리 뻗는 동작은 캔버스 안에 맞추기 위해 전체 캐릭터가 축소될 가능성이 있다.

현재 기준 이미지에서 배경을 제외한 대략적인 바운딩 박스 점유율:

| 기준 이미지 | 너비 점유율 | 높이 점유율 |
|---|---:|---:|
| CatKnight `idle-00` | 약 29.9% | 약 41.6% |
| CopperAxeBarbarian 현재 후보 | 약 84.2% | 약 73.1% |

바바리안은 CatKnight보다 머리끝~발바닥 높이를 약 115%로 설정했다. CatKnight 높이 점유율을 기준으로
계산하면 바바리안 목표 높이는 `41.6% × 1.15 ≈ 47.8%`다. 현재 73.1%는 이 목표보다 상당히 크다.

위 수치는 현재 후보의 크로마키 배경을 색상으로 제외해 측정한 근삿값이다. 투명 Master Design이 확정되면
알파 바운딩 박스로 다시 측정한다.

## 해상도와 점유율은 별개의 변수다

256×256 출력을 단순히 nearest-neighbor 2배로 확대해 512×512로 만들면 캐릭터와 여백이 함께 2배가
된다. 원본이 캔버스를 80% 점유했다면 확대 후에도 80%이므로 공격 여백 문제가 해결되지 않는다.

256×256 프레임을 확대하지 않고 512×512 중앙에 배치하면 512 출력 대비 월드 표시 크기는 줄어든다.
그러나 이번 512 출력의 캐릭터 높이가 406px로 지나치게 컸고, 같은 79.3% 점유율이 256 셀에도 적용되면
약 203px가 된다. 이는 CatKnight 높이 213px와 가까우므로 오히려 현재 프로젝트 크기에 적합할 가능성이
있다. 실제 256 출력으로 확인하기 전에는 확정하지 않는다.

## 가설

초기 가설은 업로드 기준 이미지의 점유율을 줄이면 출력 크기도 줄어든다는 것이었다. Test B에서 입력 높이
245px가 출력 높이 406px로 약 1.66배 정규화되었으므로 **512 입력 패딩으로 출력 크기를 통제하는 가설은
기각한다.** PerfectPixel은 투명 패딩보다 캐릭터 콘텐츠를 기준으로 Frame cell size에 맞춰 재확대한다.

## 권장 목표값 — 첫 실험용

- 캔버스: 512×512
- 머리끝~발바닥 높이: 약 245px (`512 × 47.8%`)
- 두 도끼를 포함한 현재 비율 예상 너비: 약 280~290px
- Idle 전체 바운딩 박스 목표: 너비 55~60%, 높이 48~52%
- 최소 여백 목표: 좌우 각 80px 이상, 상단 80px 이상
- 전방 디딤발 접촉점과 Pivot은 모든 비교 이미지에서 동일하게 기록

도끼 공격 궤적이 주로 한쪽으로 길게 뻗는다면 캐릭터를 캔버스 정중앙에 놓는 대신 Actor Origin을
기준으로 반대쪽에 여백을 덜 두고 공격 방향에 더 많은 여백을 배분할 수 있다. 이 배치는 공격 콘셉트가
정해진 뒤 확정한다.

## 실험 순서

### Test A — 512 / 현재 기준 이미지

- 기존 생성 결과를 기준 데이터로 사용한다.
- Idle, Chest Tap의 프레임별 알파 바운딩 박스와 PerfectPixel 경고를 기록한다.

### Test B — 512 / 축소·패딩 기준 이미지

- 캐릭터 외형은 바꾸지 않고 바운딩 박스 높이를 약 48~52%로 축소한다.
- 투명 512×512 캔버스와 Actor Origin을 유지한다.
- Test A와 동일한 Character description, Motion description, Frames, FPS, Repeat, Facing을 사용한다.
- PerfectPixel이 입력 여백과 캐릭터 크기를 보존하는지 확인한다.

준비된 입력 이미지:

```text
ProjectDocs/ArtPipeline/Characters/CopperAxeBarbarian/References/CopperAxeBarbarian-master-input-v2-padded.png
```

측정값:

```text
Canvas: 512x512 RGBA
Alpha bounding box: x=115, y=227, width=282, height=245
Width occupancy: 55.1%
Height occupancy: 47.9%
Transparent padding: left=115, right=115, top=227, bottom=40
```

최신 브레이드/가슴 가죽끈 디자인 후보에서 크로마키를 제거하고 nearest-neighbor로 축소했다. 캐릭터를
새로 생성하거나 외형을 다시 그리지 않았다.

Test B 결과:

```text
PerfectPixel Quality: 68
Output bbox: 422x406 (frame 00)
Output occupancy: width 82.4%, height 79.3%
Input-to-output height scale: about 1.66x
Result: transparent padding was not preserved
```

Base Idle 4프레임은 모두 높이 406px, y=64~469를 유지했다. 따라서 이 세트에서 관찰된 문제는 프레임별
축척 불일치가 아니라 전체 Actor가 지나치게 크게 정규화된 것이다.

### Test C — 256 Frame cell size

- Test B에서 입력 패딩이 무시되었으므로 다음 우선 실험으로 실행한다.
- 256 입력/출력에서의 정규화 점유율, 픽셀 품질과 프레임 일관성을 측정한다.
- 1차 비교는 확대하지 않고 투명 512×512 중앙에 배치한다.
- 같은 약 79.3% 높이 점유율이면 예상 Actor 높이는 약 203px다.
- 필요할 때만 `2배 확대`도 비교하되, 여백 문제는 해결하지 못한다는 점을 기록한다.
- Unity 표시 크기, PPU 200 유지 가능 여부와 CatKnight 대비 체격을 함께 검증한다.

Test C 결과:

| Frame | Alpha bbox | Width occupancy | Height occupancy |
|---:|---|---:|---:|
| 00 | x24 y28, 214×208 | 83.6% | 81.3% |
| 01 | x36 y20, 202×216 | 78.9% | 84.4% |

- 두 프레임의 bbox bottom은 y=236으로 동일하다.
- Frame 01 높이는 Frame 00보다 약 3.85% 크다.
- Frame 00은 도끼의 수평 범위가 넓어 Actor 전체가 더 작게 정규화됐다.
- Frame 01은 수평 범위가 좁아지면서 발바닥 하단을 기준으로 Actor 전체가 확대됐다.
- 256 출력은 1×1 픽셀 단위까지 내려가 CatKnight의 약 3×3 픽셀 밀도와 시각적으로 맞지 않는다.
  직접 최종 리소스로 사용하는 것은 보류하되, PPU 200에서의 게임 표시 크기 프로토타입으로는 유지한다.
- 결론: Frame cell size를 줄여도 프레임별 content-fit 축척 문제는 해결되지 않는다.

Unity 크기 검증:

```text
CatKnight reference height: about 213px
256 Barbarian frame 00: 208px
256 Barbarian frame 01: 216px
Import scale adjustment: none
PPU: 200
Result: perceived world size is approximately equal
```

따라서 256 출력이 PPU 200에서 절반 크기로 보일 것이라는 초기 우려는 기각한다. 256 셀에 약 80%로
정규화된 Actor의 실제 픽셀 높이가 CatKnight의 현재 높이와 우연히 거의 일치한다. 남은 문제는 월드 크기가
아니라 1×1/3×3 픽셀 밀도 차이와 Frame 00/01 사이의 3.85% 축척 변화다.

### Test D — 512 출력 후 공통 축소

- Attempt 02의 모든 프레임을 동일 배율 `245 ÷ 406 ≈ 0.603`으로 nearest-neighbor 축소한다.
- 프레임마다 개별 자동 맞춤하지 않고 전체 세트에 같은 배율을 적용한다.
- 축소 후 512×512 투명 캔버스에 Actor Origin 기준으로 재배치한다.
- 256 원본을 그대로 배치한 Test C와 픽셀 가독성, 디테일 손실, Unity 표시 크기를 비교한다.

Test C 결과로 256의 직접 최종 품질은 맞지 않으므로, 이후 정식 생산 방향은 512 출력을 유지한 상태에서
신체 기준 축척, 논리 픽셀 밀도와 Actor Origin을 후가공으로 통일하는 쪽을 우선한다. 전체 알파 bbox는 무기
동작에 따라 변하므로 축척 기준으로 사용하지 않는다. 머리끝~발바닥, 골반 폭, 흉곽 높이처럼 무기와 무관한
신체 랜드마크를 사용한다.

### Test D-2 — 논리 픽셀 밀도 통일 후보

- 최종 게임 표시 높이를 약 208~216px 범위로 맞춘다.
- CatKnight의 약 3×3 픽셀 단위를 기준으로 바바리안 프레임을 저해상도 논리 그리드로 축소한다.
- 논리 그리드 결과를 nearest-neighbor 3배로 확대해 3×3 블록을 만든다.
- 이 과정에서 문신, 눈, 도끼날과 가죽끈이 식별 가능한지 확인한다.
- 프레임마다 다른 content-fit 축척을 먼저 신체 랜드마크로 통일한 뒤 픽셀 밀도 변환을 수행한다.
- 실제 샘플 비교 전에는 공통 후가공 규칙으로 확정하지 않는다.

### Test E — 최종 캔버스 확장 후보

- PerfectPixel 생성은 품질을 위해 512×512를 사용한다.
- 후가공에서 Actor의 신체 높이를 Idle 기준에 맞춘 뒤 더 큰 투명 캔버스에 배치하는 방식을 검토한다.
- 후보 캔버스는 768×512 또는 1024×512/1024×1024이며 PPU 200은 유지한다.
- 캔버스만 확장하면 월드 표시 크기는 바뀌지 않지만, PerfectPixel 생성 단계에서 이미 축소된 Actor는
  신체 랜드마크 기준으로 다시 확대해야 한다.
- Unity에서 메모리, Pivot, 공격 이펙트와 프레임 교체 안정성을 확인하기 전에는 공통 규칙으로 확정하지 않는다.

## 프레임별 측정 항목

```text
Frame:
Canvas size:
Alpha bounding box x/y/w/h:
Width occupancy %:
Height occupancy %:
Forward-foot contact x/y:
Scale change from frame 00 %:
PerfectPixel warning:
```

## 임시 판정 기준

- 같은 애니메이션의 프레임별 높이 차이: 2% 이내 권장
- 같은 애니메이션의 프레임별 몸통 핵심 치수 차이: 논리 픽셀 1~2개 이내
- 공격 동작의 팔다리/무기 확장 때문에 바운딩 박스가 커지는 것은 허용
- 몸통, 머리와 무기 자체가 함께 축소되어 여백을 만드는 프레임은 수정 또는 폐기

실제 PerfectPixel 출력 2~3세트를 측정한 뒤 목표 점유율과 허용 오차를 공통 규칙으로 승격할지 결정한다.

## Scale-lock Feedback 실험 결과

다음 피드백으로 전체 세트를 재생성했다.

```text
keep the body, head, and outline scale identical across all frames; do not resize the character to fit axe movement
```

| Frame | Alpha bbox | 높이 | 무기 상태 |
|---:|---|---:|---|
| 00 | x58 y42, 348×428 | 428px | 양손 각각 한 자루가 유지되지 않음 |
| 01 | x46 y42, 354×428 | 428px | 양손 각각 한 자루가 유지되지 않음 |
| 02 | x18 y42, 386×428 | 428px | 양손 각각 한 자루가 유지되지 않음 |
| 03 | x38 y86, 428×384 | 384px | 양손에 한 자루씩 들지만 전체 Actor 축소 |

- 모든 프레임의 bbox bottom은 y=470으로 동일했다.
- Frame 03은 앞 프레임보다 높이가 약 10.3% 작고 상단이 44px 내려갔다.
- 생성 단계에서 무기를 생략·중첩해 콘텐츠 폭을 줄였지만, 양손 도끼가 나타난 Frame 03에서는 다시
  content-fit 축소가 발생했다.
- 결론: Feedback 문장은 생성 내용에는 영향을 주지만 PerfectPixel의 후단 프레임 맞춤 로직을 끌 수 없다.
- 절대 축척과 무기 개수를 동시에 프롬프트로 보장하는 방식은 생산 규칙으로 사용할 수 없다.

## 현재 결론

PerfectPixel은 다음 용도로 제한한다.

- 512×512에서 캐릭터 디자인과 키포즈/동작 후보 생성
- 프레임 간 흐름 참고

다음은 외부 후가공 책임으로 둔다.

- 신체 랜드마크 기준 절대 축척 통일
- 전방 디딤발과 Actor Origin 정렬
- 양손 무기 개수·형태·그립 복원
- 실제 512 출력과 게임 표시 결과를 기준으로 한 픽셀 밀도 선택
- 공격 동작용 최종 캔버스 확장

추가로 컨셉 입력 이미지는 48/64px 논리 해상도로 강제 축소하지 않는다. PerfectPixel은 입력 이미지의
패딩과 픽셀 밀도를 그대로 보존하는 도구가 아니므로, 가장 선명한 승인 컨셉 원본을 입력하고 출력 결과에서
생산 밀도를 판단한다. 캐릭터 간 상대 체격은 입력 점유율이 아니라 별도의 목표 표시 배율로 관리한다.
