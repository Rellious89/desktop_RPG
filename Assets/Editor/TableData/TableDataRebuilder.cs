using System;
using System.Collections.Generic;
using CommonEditor;
using Dungeon;
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
            var monsterAssets = ResolveTargets<MonsterDefinition>(
                TableDataPaths.MonsterOutputFolder, m => m.MonsterId,
                snapshot.Monsters.ConvertAll(r => r.Id), TableDataPaths.MonsterAssetPath,
                TableDataPaths.MonsterCsvFileName, TableDataColumns.MonsterId, result);
            var dungeonAssets = ResolveTargets<DungeonDefinition>(
                TableDataPaths.DungeonOutputFolder, d => d.DungeonId,
                snapshot.Dungeons.ConvertAll(r => r.Id), TableDataPaths.DungeonAssetPath,
                TableDataPaths.DungeonCsvFileName, TableDataColumns.DungeonId, result);

            var worldCatalog = ResolveSingleton<WorldCatalog>(TableDataPaths.WorldCatalogAssetPath, result);
            var monsterCatalog = ResolveSingleton<MonsterCatalog>(TableDataPaths.MonsterCatalogAssetPath, result);
            var dungeonCatalog = ResolveSingleton<DungeonCatalog>(TableDataPaths.DungeonCatalogAssetPath, result);

            AssetDatabase.StartAssetEditing();
            try
            {
                // World -> Monster -> Dungeon 순서로 채운다. 뒤의 것이 앞의 것을 참조하므로, 참조할 대상은
                // 이미 메모리에 만들어져 있어야 한다(여기서는 다시 로드하지 않고 위에서 만든 인스턴스를 쓴다).
                foreach (WorldRow row in snapshot.Worlds) WriteWorld(worldAssets[row.Id], row);
                foreach (MonsterRow row in snapshot.Monsters) WriteMonster(monsterAssets[row.Id], row, worldAssets);
                foreach (DungeonRow row in snapshot.Dungeons) WriteDungeon(dungeonAssets[row.Id], row, worldAssets, monsterAssets);

                WriteCatalog(worldCatalog, "worlds", SortForCatalog(snapshot.Worlds, r => r.Enabled, r => r.DisplayOrder, r => r.Id, worldAssets));
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

        private static void WriteMonster(
            MonsterDefinition asset, MonsterRow row, Dictionary<string, WorldDefinition> worlds)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("monsterId").stringValue = row.Id;
            serialized.FindProperty("displayOrder").intValue = row.DisplayOrder;
            serialized.FindProperty("maxDurability").intValue = row.MaxDurability;
            serialized.FindProperty("motionProfile").objectReferenceValue = row.MotionProfile;
            serialized.FindProperty("previewSprite").objectReferenceValue = row.PreviewSprite;
            serialized.FindProperty("world").objectReferenceValue = Lookup(worlds, row.WorldId);
            ApplyLocalizedName(serialized.FindProperty("localizedName"), row.Name);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void WriteDungeon(
            DungeonDefinition asset, DungeonRow row,
            Dictionary<string, WorldDefinition> worlds, Dictionary<string, MonsterDefinition> monsters)
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
            rewardList.arraySize = row.RewardItems.Count;
            for (int i = 0; i < row.RewardItems.Count; i++)
            {
                rewardList.GetArrayElementAtIndex(i).objectReferenceValue = row.RewardItems[i];
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
            ok &= VerifyFields<MonsterDefinition>(log, "monsterId", "localizedName", "world", "motionProfile",
                "previewSprite", "maxDurability", "displayOrder");
            ok &= VerifyFields<DungeonDefinition>(log, "dungeonId", "dungeonName", "world", "representativeSprite",
                "monsters", "rewardItems", "displayOrder");
            ok &= VerifyFields<WorldCatalog>(log, "worlds");
            ok &= VerifyFields<MonsterCatalog>(log, "monsters");
            ok &= VerifyFields<DungeonCatalog>(log, "dungeons");
            return ok;
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
