# Actor Production Tracker

로컬 파일을 읽어 만드는 **읽기 전용 제작 작업대**입니다. 원본 Unity 데이터, CSV, 이미지, 제작 패키지는 대시보드에서 수정하지 않습니다.

## 가장 쉬운 사용법

맥의 **응용 프로그램** 폴더에서 `Actor Production Tracker`를 실행하세요. 터미널 창 없이 스캔을 다시 만든 뒤 대시보드를 엽니다. 네트워크나 별도 설치는 필요 없습니다.

저장소에는 재설치·보관용 원본인 `Actor Production Tracker.app`도 같이 남겨둔다.

응용프로그램을 사용할 수 없을 때만 `Open Actor Production Tracker.command`를 대체 실행 방법으로 사용합니다.

수동으로 하려면 프로젝트 루트에서 아래를 실행한 뒤 `Tools/ActorProductionTracker/dashboard.html`을 열면 됩니다.

```sh
node Tools/ActorProductionTracker/scan.mjs
```

## 화면 읽기

- **작업 추천**은 Active 배우만 대상으로, 필수 애니메이션 공백·이어 쓸 수 있는 패키지 자료·인덱스/Import 위험을 계산해 세 가지를 제안합니다.
- **제작 단계**는 Candidate, Brief, Master, Animation, Unity, Ready와 보류/테스트를 분리합니다.
- **전체 목록**에서 검색과 월드·유형·상태·Import·공백 필터를 씁니다. 선택한 보기와 필터는 이 기기의 브라우저에만 저장되며, `필터 초기화`로 되돌릴 수 있습니다.
- 카드의 링크는 실제 문서·이미지·런타임 원본을 열고, `경로 복사`는 검토용 경로를 복사합니다.

## 판정 기준

이 작업대는 스캔 파생 결과입니다. **설정/패키지** 완료는 index, brief, PerfectPixel 입력, measurements, motion brief, 승인/관찰 master의 엄격 체크리스트입니다. 이는 새 패키지 규칙으로의 이행 진행률일 뿐, 기존 자산이 없다는 뜻이 아닙니다.

**리소스**는 프로필별 필수 애니메이션 프레임 충족도입니다. Candidate와 Test는 해당 없음이며, Hold는 보이지만 Active 지표에서 제외됩니다.

Player의 **공격 풀 연결**은 프레임 폴더만 보지 않습니다. MotionProfile이 참조하는 `ComboTierAttackPool`과
각 `AttackMotionDefinition`의 실제 프레임 GUID를 따라가며 다음을 확인합니다.

```text
Tier 1 = Attack A
Tier 2 = Attack A + 새로운 Attack B
Tier 3 = Attack A + Attack B + 새로운 Attack C
```

같은 Attack A를 별도 Definition으로 복제한 경우에도 프레임 배열이 같으면 같은 모션으로 판정합니다. 빈 풀,
미참조 잔여 풀과 하위 모션 누락도 별도로 표시합니다.

Enemy의 **몬스터 모션 점검**은 MotionProfile 안의 실제 Sprite GUID를 따라 Base Idle, Idle Event, Hit Hold,
Hit Recovery와 Defeat를 확인합니다. Hold/Recovery 인덱스가 범위 안인지, 두 슬롯이 서로 다른 Sprite인지,
다른 배우 폴더를 잘못 참조하지 않는지, 모션 폴더에 미참조 프레임이 남았는지도 표시합니다. 현재 런타임
설계상 Enemy는 공격 모션이 없으며, Defeat가 비어 있으면 피격 자세에서 Fade-out하는 정상 상태입니다.
품질상 같은 Hold/Recovery Sprite를 의도적으로 쓰는 경우에는 `tracker.config.json`의 Actor별 승인 예외가
있어야만 제작 준비 완료로 인정합니다.

**게임 연결**은 Player의 MotionProfile + CharacterDefinition + 현재 필수 공격 풀, Enemy의 재생 가능한
MotionProfile + 활성 table 행 + Profile/Portrait 키 연결을 확인합니다. Player table은 N/A입니다.

생성 결과는 `ProjectDocs/ActorProduction/`의 JSON 인덱스, Markdown 보고서, 대시보드 데이터입니다. 스캔 리비전은 내용 해시라서 같은 입력에서는 변하지 않습니다.
