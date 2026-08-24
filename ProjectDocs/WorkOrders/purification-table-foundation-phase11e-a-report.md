# KeyBuddy 11E-A — 교회·정화 테이블 런타임 기반

- 구현 커밋: `27330051` — `Add purification table runtime foundation`
- PurificationConfig `church_prayer` Definition/Catalog을 생성하고 전용 Rebuild 범위를 추가했다.
- CSV 값: required building `2`, interval `60`, value `1`, base slots `1`, enabled를 생성 에셋에 반영했다.
- Building Rebuild로 교회 `Building_2.asset`과 Catalog 항목을 추가했고, 여관 GUID는 보존했다. 여관 비용은 CSV 권위값 `2000`으로 갱신됐다.
- 집중 EditMode Building/CorruptionConfig: 62/62 통과, C# 컴파일 오류 0.
- SaveData v5 유지. 씬·프리팹·CSV·Localization은 변경하지 않았다. Generated 변경은 Building과 PurificationConfig 도메인뿐이다.
- `git diff --check` 통과, 실제 persistentDataPath 및 원격 푸시 미사용.
