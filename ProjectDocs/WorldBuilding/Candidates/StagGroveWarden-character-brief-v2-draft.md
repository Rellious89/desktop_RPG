# StagGroveWarden — V2 Character Brief Draft

> 가칭: `StagGroveWarden / 수사슴 숲지기`
>
> World ID: `ANIMAL-LAND-01`
>
> 상태: 파일럿 확정 / Master v1 승인 완료
>
> 생산 목적: KeyBuddy V2 신규 수인 캐릭터의 Master·Idle 파이프라인 검증

이 문서는 애니멀랜드 첫 V2 파일럿으로 선택된 `StagGroveWarden`의 기획 초안이다. Master Candidate 01은
2026-08-10 사용자 승인을 받아 Master v1로 잠겼다. 이후 생산 기준은
`ProjectDocs/ArtPipeline/Characters/StagGroveWarden` 패키지를 우선한다.

## 1. 캐릭터 정체성

```text
Actor ID / 가칭: StagGroveWarden / 수사슴 숲지기
Actor Type: Player Character Candidate
출신 세계: ANIMAL-LAND-01 / 애니멀랜드
진영: 평화 진영
종족: 수사슴 수인
성별: 남성
연령 인상: 젊은 성인
직업: 숲지기 / 경계 수호자
용병단 역할: 이동 방해, 밀쳐내기와 지역 제어
한 문장 콘셉트: 큰 나뭇가지형 뿔과 짧은 숲 지팡이를 지닌 침착한 수사슴 수호자.
성격 키워드: 침착함 / 경계심 / 집요함
```

동물 우화의 `신중하고 경계심이 강한 사슴`이라는 첫인상을 사용하되, 겁이 많아 도망치는 캐릭터로 만들지
않는다. 침입자의 흔적을 발견하면 위험을 감수하고 끝까지 추적하는 집요함을 개인 성격으로 부여한다.

## 2. 세계관 역할

- 평화 진영의 숲과 국경 지대를 순찰하는 수호자다.
- 영토를 넓히는 정규군보다 침입 흔적 추적, 주민 대피와 길목 봉쇄를 담당한다.
- 강경 진영의 굴착대가 보호림 아래의 자원을 훔치는 사건을 추적할 수 있다.
- 버그에 오염된 균열을 발견하고 감염된 침입자를 뒤쫓다가 데스크탑에 도착한다.
- 데스크탑에서는 백신의 보호를 받고 용병단과 협력해 감염체의 이동을 제한한다.
- 장기 목표는 감염 경로를 차단하고 애니멀랜드의 숲으로 돌아가는 것이다.

특정 국가명, 왕명과 숲의 정식 지명은 세계 설정이 필요해질 때 추가한다.

## 3. 게임 역할과 전투 표현

### 핵심 역할

- 직접 치유가 아닌 `제어형 보조 전투원`
- 적의 이동 경로를 묶거나 밀어내는 표현
- 지팡이 타격과 뿔 받아치기를 결합한 중거리 근접 전투
- 숲 마법은 덩굴, 잎과 뿌리의 짧은 이펙트로 표현

### 기본 공격 방향

1. 짧은 지팡이를 화면 오른쪽으로 뻗어 적의 움직임을 막는다.
2. 몸을 낮추고 뿔로 짧게 받아쳐 적을 밀어낸다.
3. 강한 공격에서는 지면에서 짧은 뿌리 또는 잎 소용돌이가 솟는다.

Master와 Idle에는 상시 마법 이펙트를 포함하지 않는다. 뿌리와 잎은 공격 이펙트 레이어에서만 사용한다.

## 4. 체형과 실제 스케일

| 항목 | 목표값 |
|---|---|
| 체형 | 늘씬하지만 지나치게 길지 않은 중형 수인 SD 체형 |
| 비율 | 약 2.5등신 |
| 상대 스케일 | 일반 캐릭터보다 약간 큰 `1.05~1.1` 인상, 런타임 Transform 값으로 확정하지 않음 |
| 머리 | 사슴 주둥이와 큰 귀가 읽히는 중대형 머리 |
| 몸통 | 짧고 단정한 상체, 좁은 허리 |
| 팔·다리 | 인간형 관절을 유지하되 가늘고 탄력적인 인상 |
| 발 | 갈라진 발굽이 큰 색면 2개로 읽히는 형태 |
| 꼬리 | 짧은 사슴 꼬리, 몸 뒤에서 작은 밝은 포인트로만 노출 |

뿔 때문에 캐릭터 전체가 지나치게 커 보이지 않게, 신체 높이와 장식 포함 높이를 분리한다.

```text
발 접지점 → 머리 정수리: 약 62~68px 목표
머리 정수리 → 뿔 최고점: 약 14~18px 목표
전체 가시 실루엣: 약 78~86px 목표
```

위 값은 128×128 V2 캔버스에서의 첫 제작 후보이며, 실제 생성 결과와 IceMage 런타임 비교 후 조정한다.

## 5. 얼굴과 종족 특징

- 얼굴은 인간 얼굴에 귀만 붙인 형태가 아니라 짧고 부드러운 사슴 주둥이를 가진다.
- 코는 작은 짙은 갈색 또는 검은색의 단일 덩어리로 읽힌다.
- 눈은 따뜻한 호박색이며, 경계하는 인상이되 사납게 찢어진 눈으로 만들지 않는다.
- 귀는 머리 양옆 위쪽에서 바깥으로 벌어지며 뿔에 완전히 가려지지 않는다.
- 뿔은 좌우가 같은 계열임을 알 수 있어야 하지만 3/4 시점의 원근 차이는 허용한다.
- 뿔 한쪽당 큰 가지 1개와 읽기 쉬운 가지 끝 2~3개만 사용한다.
- 뿔 표면에 잔가지, 잎과 장식을 과도하게 추가하지 않는다.
- 털색은 따뜻한 적갈색 또는 황갈색을 기본으로 하고, 주둥이·목·꼬리 아래에 밝은 크림색을 사용한다.

## 6. 의상과 장비

### 의상

- 짙은 숲색의 짧은 천 튜닉
- 허리까지 오는 갈색 가죽 조끼 또는 가슴 보호대
- 팔과 다리 관절을 가리지 않는 소형 가죽 보호구
- 한쪽 어깨에만 걸친 짧은 올리브색 숲 망토
- 허리의 작은 추적 도구 주머니 1개

긴 로브, 발목까지 오는 망토와 넓은 치마는 사용하지 않는다. 발굽, 다리 자세와 꼬리가 보여야 한다.

### 주 장비

- 캐릭터 신장의 약 `0.75~0.85` 길이인 한손·양손 겸용 짧은 지팡이
- 곧은 목재 몸체 위쪽만 자연스러운 나뭇가지 형태
- 끝부분에 작은 녹색 마법석 또는 잎 문양 결합부 1개
- 지팡이 전체를 잎과 덩굴로 덮지 않는다.

지팡이는 장창이나 글레이브처럼 보이면 안 된다. 날붙이가 없고, 곧은 축과 짧은 나뭇가지 머리로 읽혀야 한다.

## 7. 실루엣 잠금

작은 화면에서 다음 순서로 읽혀야 한다.

1. 좌우로 뻗은 단순한 수사슴 뿔
2. 길고 바깥으로 열린 귀와 짧은 주둥이
3. 세로로 든 짧은 나뭇가지 지팡이
4. 짧은 숲 망토와 갈라진 발굽

실루엣의 좌우 폭이 뿔과 지팡이 때문에 과도하게 넓어지지 않도록 한다. 기본 자세에서는 지팡이를 몸에서
약간 떼어 세우고, 뿔 끝과 지팡이 끝이 서로 겹치지 않게 한다.

## 8. V2 팔레트 방향

정확한 색상값은 승인된 Master에서 추출한다.

| 영역 | 색상 방향 |
|---|---|
| 털 | 따뜻한 적갈색 또는 황갈색 |
| 밝은 털 | 크림색·연한 황토색 |
| 뿔·지팡이 | 중간 갈색과 어두운 적갈색 |
| 튜닉·망토 | 숲 녹색과 올리브색 |
| 가죽 | 짙은 갈색 |
| 금속 결합부 | 저채도 황동색 소량 |
| 눈 | 호박색 |
| 마법 포인트 | 연두색 또는 청록색 중 1개만 선택 |

- 털, 의상과 목재가 모두 같은 갈색 덩어리로 합쳐지지 않게 명도 차이를 둔다.
- 마법 포인트색은 눈보다 먼저 읽힐 정도로 크거나 밝지 않게 한다.
- V2의 작은 표시 크기에서 구분 가능한 제한된 색면을 사용한다.

## 9. V2 생산 규격

현재 `Test_IceMage` 실제 임포트 설정을 V2 파일럿 기준으로 사용한다.

```text
Canvas: 128×128 RGBA
Pixels Per Unit: 50
Filter Mode: Point
Compression: None
Pivot candidate: X 0.5 / Y 0.234
View: 약한 3/4, 화면 오른쪽 방향
Background: 완전 투명
External ground shadow: 없음
```

`Pivot Y 0.234`는 현재 IceMage에서 검증 중인 기준값이다. StagGroveWarden에서도 먼저 같은 접지선을 사용하되,
숫자를 맞추기 위해 발 위치를 억지로 이동하지 않는다. Master 생성 시 발 접지점을 캔버스 아래에서 약 30px
위에 두고, Unity 테스트에서 실제 발 위치가 어긋나면 Actor Origin 규칙에 따라 별도 측정한다.

### V2 캔버스 점유 목표

- 발 접지선: 캔버스 아래에서 약 30px
- 뿔 최고점: 캔버스 위쪽 안전 여백 8px 이상
- 지팡이와 뿔을 포함한 좌우 안전 여백: 각 8px 이상
- 꼬리, 망토와 지팡이가 캔버스 경계에 닿지 않음
- 캐릭터 신체 중심은 X 0.5 근처에 유지

## 10. 변경 불가 요소

- 한눈에 수사슴으로 읽히는 뿔, 긴 귀와 짧은 주둥이
- 따뜻한 갈색 털과 밝은 주둥이·목 털
- 약 2.5등신의 중형 SD 수인 체형
- 짧은 숲 망토와 가죽 장비
- 날이 없는 나뭇가지형 짧은 지팡이
- 화면 오른쪽을 향하는 약한 3/4 전신 자세
- 침착한 수호자 인상
- 뿔, 귀, 지팡이가 서로 분리되어 읽히는 실루엣

## 11. 금지 요소

- 인간 얼굴에 사슴귀와 뿔만 붙인 디자인
- 뿔이 엘크처럼 지나치게 넓거나 수십 갈래로 복잡한 형태
- 뿔에 꽃, 잎, 보석과 장신구를 과도하게 매다는 것
- 활, 화살통, 장창, 글레이브와 검
- 발목까지 내려오는 긴 로브와 대형 망토
- 중판금 갑옷과 현대 장비
- 네 발로 서 있는 일반 사슴 체형
- 켄타우로스 형태
- 상시 발광하는 전신 문양과 대형 마법 오라
- 지나치게 사실적인 사슴 두상 또는 공포스러운 해골사슴 인상
- 바닥 그림자, 배경, 문자와 UI 요소

## 12. Master 이미지 생성용 콘셉트 입력

```text
Create one full-body male anthropomorphic stag forest warden for a cute fantasy desktop companion game.
He has warm reddish-brown fur, a cream muzzle and throat, long deer ears, a short gentle deer muzzle,
amber eyes, split hooves, a small deer tail, and a simple readable pair of antlers with only two or three
large tines per side. Use a compact 2.5-head-tall SD body with a calm, vigilant expression.

He wears a short dark forest-green tunic, a fitted brown leather vest, small leather guards, and a short
olive shoulder cape that does not hide his arms, legs, hooves, or tail. He holds one short straight wooden
warden staff vertically beside his body. Only the top of the staff branches slightly like a twig and contains
one small restrained green magical accent. The staff has no blade.

Show a weak three-quarter full-body view facing screen-right, with both feet readable on one ground-contact
line. Keep the antlers, ears, face, body, staff, and hooves clearly separated in the silhouette. Use the approved
KeyBuddy V2 compact pixel-art density, strong clean stepped outlines, limited color planes, upper-left lighting,
transparent background, no ground shadow, no text, no extra objects, and no persistent magic effect.
```

### 생성 후 Reject 기준

- 수사슴이 아니라 엘프·악마·인간처럼 보임
- 지팡이가 창이나 칼날 무기로 변함
- 뿔·귀·지팡이가 한 덩어리로 겹침
- 뿔 모양이나 가지 수가 좌우에서 무작위로 달라짐
- 긴 망토나 로브가 발굽과 자세를 가림
- 얼굴이 공포스럽거나 과도하게 사실적임
- 캐릭터가 캔버스 안에서 IceMage보다 지나치게 작거나, 뿔이 경계에 닿음
- 배경, 바닥 그림자, 마법 오라 또는 여러 소품이 추가됨

## 13. 승인 이후 제작 순서

```text
1. Character Brief 승인
2. Master 후보 생성
3. 실루엣·뿔·지팡이·체형 검토
4. Master 1종 잠금
5. 128×128 Base 정렬 및 Pivot 후보 검증
6. Idle 4프레임 테스트
7. Unity 런타임에서 IceMage와 상대 크기 비교
8. 필요 시 프레이밍만 조정
9. 공격 모션 및 이펙트 제작
```

## 14. 사용자 개입이 필요한 지점

Master Candidate 01 생성, 128px 프레이밍 검증과 사용자 승인을 완료했다. Idle 4프레임 Motion Brief와
PerfectPixel Input Sheet는 정식 ArtPipeline 패키지에 기록한다. 정식 개인 이름, 국가명, 세력명과 마법의
기원은 지금 결정하지 않아도 제작할 수 있다.
