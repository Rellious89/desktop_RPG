using System;
using System.Collections.Generic;
using CommonEditor;
using Dungeon;
using Inventory;
using UnityEditor;
using UnityEngine;

namespace TableDataEditor
{
    /// <summary>Rebuild 한 번의 결과.</summary>
    public sealed class TableDataRebuildResult
    {
        public TableDataValidationResult Validation;

        /// <summary>실제로 에셋을 쓴 경우에만 true. 검증에서 걸리면 false이고 <b>프로젝트는 한 글자도
        /// 바뀌지 않는다</b>.</summary>
        public bool Wrote;

        public int CreatedCount;
        public int UpdatedCount;
    }

    /// <summary>
    /// 검증을 통과한 스냅샷을 생성 에셋으로 옮긴다.
    ///
    /// <b>쓰기 전에 반드시 전체 Validate를 다시 돌린다.</b> 오류가 하나라도 있으면 폴더 생성,
    /// <c>CreateAsset</c>, <c>SetDirty</c>, <c>SaveAssets</c> 무엇도 호출하지 않고 그대로 끝낸다 -
    /// "일부만 반영된 상태"가 가장 다루기 어려운 상태이기 때문이다. 경로/타입 충돌과 재사용 대상의
    /// 모호함처럼 쓰는 도중에야 드러날 문제도 <see cref="TableDataValidator"/>가 미리 잡아 준다.
    ///
    /// <b>enabled=0 행도 Definition은 만든다.</b> 카탈로그에서만 빠질 뿐, 에셋 자체는 남아 있어야
    /// 나중에 다시 켤 때 GUID와 참조가 그대로 살아난다. 반대로 CSV에서 사라진 생성 에셋은
    /// <b>지우지 않는다</b> - 경고만 남기고 사람이 판단한다.
    ///
    /// <b>같은 ID면 같은 에셋을 다시 쓴다.</b> 파일 이름이 아니라 에셋이 들고 있는 ID로 찾아 재사용하므로
    /// 다시 만들어도 GUID가 보존되고, 씬/프리팹이 그 에셋을 참조하고 있어도 끊기지 않는다.
    /// </summary>
    public static class TableDataRebuilder
    {
        /// <summary>CSV가 아니라 런타임 클래스 쪽 문제일 때 진단의 "파일" 칸에 쓰는 이름.</summary>
        private const string RuntimeSchemaPseudoFile = "(runtime schema)";

        // MonsterDefinition.DropEntry 안쪽 필드 이름. 사전 검사와 쓰기가 같은 상수를 보게 해서
        // 한쪽만 고쳐진 채로 "검사는 통과했는데 쓰다가 죽는" 상태가 생기지 않게 한다.
        private const string DropsField = "drops";
        private const string DropItemField = "item";
        private const string DropChanceField = "chanceBasisPoints";
        private const string DropCountField = "count";

        // 처치 재화 보상 칸. 드롭과 같은 이유로 상수를 한 곳에 둔다. 재화는 <b>id 문자열이 아니라
        // CurrencyDefinition 참조</b>다 - 검증을 통과한 Currency.csv 행으로 만든 에셋만 여기 들어온다.
        private const string CurrencyField = "currency";
        private const string CurrencyAmountMinField = "currencyAmountMin";
        private const string CurrencyAmountMaxField = "currencyAmountMax";

        public static TableDataRebuildResult Rebuild()
        {
            var result = new TableDataRebuildResult
            {
                Validation = TableDataValidator.Validate(),
            };

            if (!result.Validation.CanRebuild) return result;

            // 필드 이름이 런타임 클래스와 어긋나면 절반쯤 쓰고 실패한다. 메모리 안의 임시 인스턴스로
            // 미리 확인하고, 어긋나면 아무것도 쓰지 않고 끝낸다(에셋을 만들지 않는 순수 검사다).
            if (!VerifySerializedLayout(result.Validation.Log)) return result;

            TableDataSnapshot snapshot = result.Validation.Snapshot;

            EnsureFolders();

            var worldAssets = ResolveTargets<WorldDefinition>(
                TableDataPaths.WorldOutputFolder, w => w.WorldId,
                snapshot.Worlds.ConvertAll(r => r.Id), TableDataPaths.WorldAssetPath,
                TableDataPaths.WorldCsvFileName, TableDataColumns.WorldId, result);
            var currencyAssets = ResolveTargets<CurrencyDefinition>(
                TableDataPaths.CurrencyOutputFolder, c => c.CurrencyId,
                snapshot.Currencies.ConvertAll(r => r.Id), TableDataPaths.CurrencyAssetPath,
                TableDataPaths.CurrencyCsvFileName, TableDataColumns.CurrencyId, result);
            var itemAssets = ResolveTargets<ItemDefinition>(
                TableDataPaths.ItemOutputFolder, i => i.ItemId,
                snapshot.Items.ConvertAll(r => r.Id), TableDataPaths.ItemAssetPath,
                TableDataPaths.ItemCsvFileName, TableDataColumns.ItemId, result);
            var monsterAssets = ResolveTargets<MonsterDefinition>(
                TableDataPaths.MonsterOutputFolder, m => m.MonsterId,
                snapshot.Monsters.ConvertAll(r => r.Id), TableDataPaths.MonsterAssetPath,
                TableDataPaths.MonsterCsvFileName, TableDataColumns.MonsterId, result);
            var dungeonAssets = ResolveTargets<DungeonDefinition>(
                TableDataPaths.DungeonOutputFolder, d => d.DungeonId,
                snapshot.Dungeons.ConvertAll(r => r.Id), TableDataPaths.DungeonAssetPath,
                TableDataPaths.DungeonCsvFileName, TableDataColumns.DungeonId, result);

            var worldCatalog = ResolveSingleton<WorldCatalog>(TableDataPaths.WorldCatalogAssetPath, result);
            var currencyCatalog = ResolveSingleton<CurrencyCatalog>(TableDataPaths.CurrencyCatalogAssetPath, result);
            var itemCatalog = ResolveSingleton<ItemCatalog>(TableDataPaths.ItemCatalogAssetPath, result);
            var monsterCatalog = ResolveSingleton<MonsterCatalog>(TableDataPaths.MonsterCatalogAssetPath, result);
            var dungeonCatalog = ResolveSingleton<DungeonCatalog>(TableDataPaths.DungeonCatalogAssetPath, result);

            AssetDatabase.StartAssetEditing();
            try
            {
                // World -> Currency -> Item -> Monster -> Dungeon 순서로 채운다. 뒤의 것이 앞의 것을
                // 참조하므로, 참조할 대상은 이미 메모리에 만들어져 있어야 한다(여기서는 다시 로드하지 않고
                // 위에서 만든 인스턴스를 쓴다). Currency와 Item을 Monster보다 앞에 둔 것은 이후 단계에서
                // 몬스터가 재화/드롭 아이템 에셋을 가리키게 되어도 이 순서를 다시 뒤집지 않게 하기 위함이다.
                foreach (WorldRow row in snapshot.Worlds) WriteWorld(worldAssets[row.Id], row);
                foreach (CurrencyRow row in snapshot.Currencies) WriteCurrency(currencyAssets[row.Id], row);
                foreach (ItemRow row in snapshot.Items) WriteItem(itemAssets[row.Id], row);
                foreach (MonsterRow row in snapshot.Monsters) WriteMonster(monsterAssets[row.Id], row, worldAssets, currencyAssets, itemAssets);
                foreach (DungeonRow row in snapshot.Dungeons) WriteDungeon(dungeonAssets[row.Id], row, worldAssets, monsterAssets, itemAssets);

                WriteCatalog(worldCatalog, "worlds", SortForCatalog(snapshot.Worlds, r => r.Enabled, r => r.DisplayOrder, r => r.Id, worldAssets));
                WriteCatalog(currencyCatalog, "currencies", SortForCatalog(snapshot.Currencies, r => r.Enabled, r => r.DisplayOrder, r => r.Id, currencyAssets));
                WriteCatalog(itemCatalog, "items", SortForCatalog(snapshot.Items, r => r.Enabled, r => r.DisplayOrder, r => r.Id, itemAssets));
                WriteCatalog(monsterCatalog, "monsters", SortForCatalog(snapshot.Monsters, r => r.Enabled, r => r.DisplayOrder, r => r.Id, monsterAssets));
                WriteCatalog(dungeonCatalog, "dungeons", SortForCatalog(snapshot.Dungeons, r => r.Enabled, r => r.DisplayOrder, r => r.Id, dungeonAssets));
            }
            finally
            {
                // 예외가 나도 반드시 풀어 준다 - 여기서 빠져나가지 못하면 에디터가 에셋 변경을 계속
                // 묶어 둔 채로 남아 프로젝트 전체가 이상하게 동작한다.
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 카탈로그는 목록 검사 결과를 캐시하므로, 방금 바꾼 내용으로 다시 검사하게 표시한다.
            worldCatalog.MarkDirty();
            currencyCatalog.MarkDirty();
            itemCatalog.MarkDirty();
            monsterCatalog.MarkDirty();
            dungeonCatalog.MarkDirty();

            result.Wrote = true;
            return result;
        }

        // ---- 대상 에셋 확보 ----

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "Generated");
            EnsureFolder(TableDataPaths.GeneratedRoot, "TableData");
            EnsureFolder(TableDataPaths.OutputRoot, "World");
            EnsureFolder(TableDataPaths.OutputRoot, "Currency");
            EnsureFolder(TableDataPaths.OutputRoot, "Item");
            EnsureFolder(TableDataPaths.OutputRoot, "Monster");
            EnsureFolder(TableDataPaths.OutputRoot, "Dungeon");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }

        /// <summary>
        /// ID마다 쓸 에셋을 확정한다. 같은 ID의 생성 에셋이 있으면 <b>그것을 그 자리에서 다시 쓴다</b> -
        /// 파일을 옮기지 않는 것은 의도적이다. GUID 보존이 파일 이름 규칙보다 우선하고, 이름을 맞추려고
        /// 파일을 옮기다 보면 서로의 자리를 바꿔야 하는 경우가 생겨 <b>중간에 남의 에셋을 덮어쓸</b> 위험이
        /// 생기기 때문이다. 이름이 규칙과 다르면 경고만 남긴다.
        ///
        /// 새로 만들 때 경로가 이미 차 있으면 <see cref="AssetDatabase.GenerateUniqueAssetPath"/>로 비켜
        /// 만든다. 이 상황은 <see cref="TableDataValidator"/>가 이미 오류로 잡으므로 정상 흐름에서는
        /// 오지 않지만, <b>어떤 경우에도 기존 파일을 덮어쓰지 않는다</b>는 것을 코드로 보장해 둔다.
        ///
        /// 생성은 <c>StartAssetEditing</c> 바깥에서 한다 - 묶음 편집 중에 만든 에셋은 아직 데이터베이스에
        /// 등록되기 전이라, 그 안에서 경로로 다시 찾으면 없는 것처럼 보인다.
        /// </summary>
        private static Dictionary<string, T> ResolveTargets<T>(
            string folder, Func<T, string> idSelector, List<string> ids, Func<string, string> pathBuilder,
            string csvFile, string idColumn, TableDataRebuildResult result) where T : ScriptableObject
        {
            var existing = TableDataAssetIndex.LoadGeneratedById(folder, idSelector);
            var targets = new Dictionary<string, T>(StringComparer.Ordinal);
            TableDataDiagnosticLog log = result.Validation.Log;

            foreach (string id in ids)
            {
                string desiredPath = pathBuilder(id);

                if (existing.TryGetValue(id, out List<T> matches) && matches.Count == 1)
                {
                    T reused = matches[0];
                    string currentPath = AssetDatabase.GetAssetPath(reused);

                    if (!string.Equals(currentPath, desiredPath, StringComparison.Ordinal))
                    {
                        log.Warning(csvFile, TableDataDiagnostic.FileLevelRow, idColumn, id,
                            $"생성 에셋이 규칙과 다른 이름('{currentPath}')으로 있어 GUID를 지키려고 그 자리에서 갱신합니다 - " +
                            $"'{desiredPath}' 이름을 쓰려면 기존 파일을 지우고 다시 실행하세요.");
                    }

                    targets[id] = reused;
                    result.UpdatedCount++;
                    continue;
                }

                string createPath = desiredPath;
                if (AssetDatabase.LoadMainAssetAtPath(desiredPath) != null)
                {
                    createPath = AssetDatabase.GenerateUniqueAssetPath(desiredPath);
                    log.Warning(csvFile, TableDataDiagnostic.FileLevelRow, idColumn, id,
                        $"'{desiredPath}'가 이미 다른 에셋에 쓰이고 있어 '{createPath}'에 만듭니다 - " +
                        "기존 파일을 정리한 뒤 다시 실행하세요.");
                }

                var created = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(created, createPath);
                targets[id] = created;
                result.CreatedCount++;
            }

            return targets;
        }

        private static T ResolveSingleton<T>(string path, TableDataRebuildResult result) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                result.UpdatedCount++;
                return existing;
            }

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            result.CreatedCount++;
            return created;
        }

        // ---- 필드 쓰기 ----

        private static void WriteWorld(WorldDefinition asset, WorldRow row)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("worldId").stringValue = row.Id;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            ApplyLocalizedName(serialized.FindProperty("localizedName"), row.Name);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// currency_id도 item_id와 같은 무게의 키라, CSV에 적힌 값을 <b>한 글자도 바꾸지 않고</b> 쓴다
        /// (검증이 다듬지 않은 값을 그대로 통과시켰으므로 그대로 쓴다). 잔액은 여기서 쓰지 않는다 -
        /// <see cref="CurrencyDefinition"/>에는 잔액 칸 자체가 없고, 표가 정하는 것은 표시 정보뿐이다.
        /// </summary>
        private static void WriteCurrency(CurrencyDefinition asset, CurrencyRow row)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("currencyId").stringValue = row.Id;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            serialized.FindProperty("icon").objectReferenceValue = row.Icon;
            ApplyLocalizedName(serialized.FindProperty("localizedName"), row.Name);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// item_id는 <b>저장 파일의 키</b>라 여기서 쓰는 값이 CSV에 적힌 것과 한 글자도 달라서는 안 된다
        /// (검증이 다듬지 않은 값을 그대로 통과시켰으므로 그대로 쓴다). 사람이 손으로 채우던
        /// <c>displayName</c>은 임포터의 관심사가 아니라 건드리지 않는다 - 이름의 원천은 localizedName이다.
        /// </summary>
        private static void WriteItem(ItemDefinition asset, ItemRow row)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("itemId").stringValue = row.Id;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            serialized.FindProperty("icon").objectReferenceValue = row.Icon;
            ApplyLocalizedName(serialized.FindProperty("localizedName"), row.Name);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        /// <summary>
        /// base_monster_id는 <b>문자열 그대로</b> 적는다 - 다른 에셋을 가리키는 참조로 만들지 않는 것은
        /// 의도적이다. 참조가 되는 순간 "base를 따라가면 값이 있다"는 경로가 생기고, 그러면 상속하지
        /// 않는다는 규칙이 코드가 아니라 약속으로만 남는다.
        /// </summary>
        private static void WriteMonster(
            MonsterDefinition asset, MonsterRow row,
            Dictionary<string, WorldDefinition> worlds, Dictionary<string, CurrencyDefinition> currencies,
            Dictionary<string, ItemDefinition> items)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("monsterId").stringValue = row.Id;
            serialized.FindProperty("baseMonsterId").stringValue = row.BaseMonsterId;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            serialized.FindProperty("maxDurability").intValue = row.MaxDurability;
            serialized.FindProperty("motionProfile").objectReferenceValue = row.MotionProfile;
            serialized.FindProperty("previewSprite").objectReferenceValue = row.PreviewSprite;
            serialized.FindProperty("world").objectReferenceValue = Lookup(worlds, row.WorldId);
            ApplyLocalizedName(serialized.FindProperty("localizedName"), row.Name);

            SerializedProperty dropList = serialized.FindProperty(DropsField);
            dropList.arraySize = row.Drops.Count;
            for (int i = 0; i < row.Drops.Count; i++)
            {
                MonsterDropRow drop = row.Drops[i];
                SerializedProperty element = dropList.GetArrayElementAtIndex(i);

                // 배열을 늘리면 Unity가 바로 앞 칸을 복사해 넣기도 하므로 세 칸을 모두 덮어쓴다.
                element.FindPropertyRelative(DropItemField).objectReferenceValue = Lookup(items, drop.ItemId);
                element.FindPropertyRelative(DropChanceField).intValue = drop.ChanceBasisPoints;
                element.FindPropertyRelative(DropCountField).intValue = drop.Count;
            }

            // 재화 보상은 세 칸을 <b>언제나</b> 함께 쓴다. CSV에서 지웠는데 에셋에 예전 참조나 금액이
            // 남아 있으면 "표에 없는 재화"가 계속 지급되므로, 지정이 없는 행에는 빈 참조와 0을 적어 둔다.
            //
            // 참조는 <b>방금 이 Rebuild가 만든 CurrencyDefinition</b>에서 찾는다(프로젝트를 뒤지지 않는다).
            // row.CurrencyId가 Currency.csv에 실재하는 활성 행인지는 Validate가 이미 확인했고 하나라도
            // 어긋나면 여기까지 오지 못하므로, 조회가 빗나가는 것은 정상 흐름에 없다 - 그래도 Lookup이
            // null을 돌려주면 그대로 비워 둔다(있지도 않은 재화를 가리키는 참조를 만들지 않는다).
            serialized.FindProperty(CurrencyField).objectReferenceValue = Lookup(currencies, row.CurrencyId);
            serialized.FindProperty(CurrencyAmountMinField).intValue = row.CurrencyAmountMin;
            serialized.FindProperty(CurrencyAmountMaxField).intValue = row.CurrencyAmountMax;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void WriteDungeon(
            DungeonDefinition asset, DungeonRow row,
            Dictionary<string, WorldDefinition> worlds, Dictionary<string, MonsterDefinition> monsters,
            Dictionary<string, ItemDefinition> items)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("dungeonId").stringValue = row.Id;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            serialized.FindProperty("representativeSprite").objectReferenceValue = row.RepresentativeSprite;
            serialized.FindProperty("world").objectReferenceValue = Lookup(worlds, row.WorldId);
            ApplyLocalizedName(serialized.FindProperty("dungeonName"), row.Name);

            SerializedProperty monsterList = serialized.FindProperty("monsters");
            monsterList.arraySize = row.MonsterIds.Count;
            for (int i = 0; i < row.MonsterIds.Count; i++)
            {
                monsterList.GetArrayElementAtIndex(i).objectReferenceValue = Lookup(monsters, row.MonsterIds[i]);
            }

            SerializedProperty rewardList = serialized.FindProperty("rewardItems");
            rewardList.arraySize = row.RewardItemIds.Count;
            for (int i = 0; i < row.RewardItemIds.Count; i++)
            {
                rewardList.GetArrayElementAtIndex(i).objectReferenceValue = Lookup(items, row.RewardItemIds[i]);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void WriteCatalog<T>(ScriptableObject catalog, string listFieldName, List<T> items)
            where T : ScriptableObject
        {
            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty(listFieldName);
            list.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        /// <summary>
        /// Table GUID + Entry Key ID를 그대로 기록한다. 참조가 해석되지 않은(선택 항목이 빈) 경우에는
        /// <b>비운다</b> - 예전 값이 남아 있으면 CSV를 지웠는데도 이름이 계속 나오게 된다.
        /// </summary>
        private static void ApplyLocalizedName(SerializedProperty property, LocalizedEntryRef entry)
        {
            if (!entry.Resolved)
            {
                LocalizedTextReferenceProperty.Clear(property);
                return;
            }

            LocalizedTextReferenceProperty.FindTableCollectionName(property).stringValue =
                LocalizedTextReferenceProperty.ToGuidString(entry.TableGuid);
            LocalizedTextReferenceProperty.FindKeyId(property).longValue = entry.KeyId;

            // Key ID로 가리키므로 이름 기반 참조는 비워 둔다(CommonEditor의 기존 규칙과 동일).
            LocalizedTextReferenceProperty.FindKeyName(property).stringValue = string.Empty;
        }

        private static TValue Lookup<TValue>(Dictionary<string, TValue> map, string id) where TValue : class
        {
            return !string.IsNullOrEmpty(id) && map.TryGetValue(id, out TValue value) ? value : null;
        }

        /// <summary>
        /// 카탈로그에 담을 항목을 고르고 정렬한다. <b>enabled=1만</b> 담고,
        /// display_order 오름차순 → 같으면 ID Ordinal 오름차순으로 둔다. Ordinal을 쓰는 이유는
        /// 현재 Locale에 따라 순서가 달라지지 않게 하기 위함이다.
        /// </summary>
        private static List<TAsset> SortForCatalog<TRow, TAsset>(
            List<TRow> rows,
            Func<TRow, bool> enabledSelector,
            Func<TRow, int> orderSelector,
            Func<TRow, string> idSelector,
            Dictionary<string, TAsset> assets) where TAsset : ScriptableObject
        {
            var selected = new List<TRow>();
            foreach (TRow row in rows)
            {
                if (enabledSelector(row)) selected.Add(row);
            }

            selected.Sort((a, b) =>
            {
                int byOrder = orderSelector(a).CompareTo(orderSelector(b));
                return byOrder != 0 ? byOrder : string.CompareOrdinal(idSelector(a), idSelector(b));
            });

            var result = new List<TAsset>(selected.Count);
            foreach (TRow row in selected)
            {
                if (assets.TryGetValue(idSelector(row), out TAsset asset) && asset != null) result.Add(asset);
            }

            return result;
        }

        // ---- 사전 점검 ----

        /// <summary>
        /// 런타임 클래스의 직렬화 필드 이름이 임포터가 쓰려는 이름과 일치하는지 메모리에서 확인한다.
        /// 에셋을 만들지 않고 임시 인스턴스만 쓰므로 프로젝트는 바뀌지 않는다.
        /// </summary>
        private static bool VerifySerializedLayout(TableDataDiagnosticLog log)
        {
            bool ok = true;
            ok &= VerifyFields<WorldDefinition>(log, "worldId", "localizedName", "displayOrder");
            ok &= VerifyFields<CurrencyDefinition>(log, "currencyId", "localizedName", "icon", "displayOrder");
            ok &= VerifyFields<ItemDefinition>(log, "itemId", "localizedName", "icon", "displayOrder");
            ok &= VerifyFields<MonsterDefinition>(log, "monsterId", "baseMonsterId", "localizedName", "world",
                "motionProfile", "previewSprite", "maxDurability", DropsField,
                CurrencyField, CurrencyAmountMinField, CurrencyAmountMaxField, "displayOrder");
            ok &= VerifyMonsterCurrencyIsAReference(log);
            ok &= VerifyDropEntryFields(log);
            ok &= VerifyFields<DungeonDefinition>(log, "dungeonId", "dungeonName", "world", "representativeSprite",
                "monsters", "rewardItems", "displayOrder");
            ok &= VerifyFields<WorldCatalog>(log, "worlds");
            ok &= VerifyFields<CurrencyCatalog>(log, "currencies");
            ok &= VerifyFields<ItemCatalog>(log, "items");
            ok &= VerifyFields<MonsterCatalog>(log, "monsters");
            ok &= VerifyFields<DungeonCatalog>(log, "dungeons");
            return ok;
        }

        /// <summary>
        /// 재화 칸이 <b>참조 칸</b>인지 확인한다. 이름만 보는 것으로는 부족하다 - 같은 이름이 문자열
        /// 칸으로 되돌아가 있으면 <c>objectReferenceValue</c>에 넣은 값이 조용히 버려져, 재화를 지정한
        /// 몬스터가 아무것도 주지 않는 에셋으로 만들어진다. 확률 칸의 타입을 보는 것과 같은 이유다.
        /// </summary>
        private static bool VerifyMonsterCurrencyIsAReference(TableDataDiagnosticLog log)
        {
            var probe = ScriptableObject.CreateInstance<MonsterDefinition>();
            try
            {
                SerializedProperty currency = new SerializedObject(probe).FindProperty(CurrencyField);

                // 칸이 없는 경우는 VerifyFields가 이미 보고했다.
                if (currency == null || currency.propertyType == SerializedPropertyType.ObjectReference) return true;

                log.Error(RuntimeSchemaPseudoFile, TableDataDiagnostic.FileLevelRow,
                    CurrencyField, nameof(MonsterDefinition),
                    $"{nameof(MonsterDefinition)}의 '{CurrencyField}'가 참조 칸이 아닙니다({currency.propertyType}) - " +
                    $"재화는 {nameof(CurrencyDefinition)}을 가리키는 참조여야 합니다(id 문자열이 아닙니다).");
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        /// <summary>
        /// 드롭 칸 안쪽 필드 이름까지 확인한다. 목록 필드가 있다는 것만으로는 부족하다 - 안쪽 이름이
        /// 어긋나면 <c>FindPropertyRelative</c>가 null을 돌려주고, 그때는 이미 앞선 몬스터 몇 마리를
        /// 쓴 뒤라 프로젝트가 반쯤 갱신된 상태로 남는다. 임시 인스턴스에서 칸을 하나 늘려 확인하며,
        /// 인스턴스는 곧바로 버리므로 프로젝트에는 아무것도 남지 않는다.
        /// </summary>
        private static bool VerifyDropEntryFields(TableDataDiagnosticLog log)
        {
            var probe = ScriptableObject.CreateInstance<MonsterDefinition>();
            try
            {
                var serialized = new SerializedObject(probe);
                SerializedProperty list = serialized.FindProperty(DropsField);
                if (list == null) return false; // 목록 자체가 없는 경우는 VerifyFields가 이미 보고했다.

                list.arraySize = 1;
                SerializedProperty element = list.GetArrayElementAtIndex(0);

                bool ok = true;
                foreach (string field in new[] { DropItemField, DropChanceField, DropCountField })
                {
                    if (element.FindPropertyRelative(field) != null) continue;

                    log.Error(RuntimeSchemaPseudoFile, TableDataDiagnostic.FileLevelRow,
                        field, nameof(MonsterDefinition.DropEntry),
                        $"{nameof(MonsterDefinition.DropEntry)}에 직렬화 필드 '{field}'가 없습니다 - " +
                        "런타임 클래스가 바뀌었으니 임포터를 함께 고쳐야 합니다.");
                    ok = false;
                }

                // 확률 칸은 이름뿐 아니라 <b>타입</b>까지 본다. 예전처럼 실수 칸으로 되돌아가 있으면
                // 정수를 쓰는 이 임포터가 값을 넣지 못한 채 지나가, 확률이 0인 드롭이 만들어진다.
                SerializedProperty chance = element.FindPropertyRelative(DropChanceField);
                if (chance != null && chance.propertyType != SerializedPropertyType.Integer)
                {
                    log.Error(RuntimeSchemaPseudoFile, TableDataDiagnostic.FileLevelRow,
                        DropChanceField, nameof(MonsterDefinition.DropEntry),
                        $"{nameof(MonsterDefinition.DropEntry)}의 '{DropChanceField}'가 정수 칸이 아닙니다" +
                        $"({chance.propertyType}) - 확률은 만분율 정수여야 합니다.");
                    ok = false;
                }

                return ok;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        private static bool VerifyFields<T>(TableDataDiagnosticLog log, params string[] fields) where T : ScriptableObject
        {
            var probe = ScriptableObject.CreateInstance<T>();
            try
            {
                var serialized = new SerializedObject(probe);
                bool ok = true;

                foreach (string field in fields)
                {
                    if (serialized.FindProperty(field) != null) continue;

                    log.Error(RuntimeSchemaPseudoFile, TableDataDiagnostic.FileLevelRow,
                        field, typeof(T).Name,
                        $"{typeof(T).Name}에 직렬화 필드 '{field}'가 없습니다 - 런타임 클래스가 바뀌었으니 임포터를 함께 고쳐야 합니다.");
                    ok = false;
                }

                return ok;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }
    }
}
