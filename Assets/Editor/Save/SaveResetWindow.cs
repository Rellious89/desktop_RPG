using System;
using System.Collections.Generic;
using Building;
using Character;
using Common;
using Inventory;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;

namespace CommonEditor.Save
{
    /// <summary>
    /// <b>개발용 저장 데이터 초기화 창</b>(Tools &gt; Reset). 현재 실제 계정의 아이템·재화·건축 기록을
    /// 읽기 전용으로 보여 주고, 고른 항목만 초기화한다.
    ///
    /// 초기화 로직 자체는 <see cref="SaveResetService"/>에 있고, 이 창은 거기에
    /// <see cref="SaveSystem.Data"/>와 <see cref="SaveSystem.Save"/>를 넘길 뿐이다 - 저장 계층에
    /// 런타임 Reset API를 만들지 않기 위해서다. 파일을 직접 편집하거나 지우지 않는다.
    ///
    /// 화면 문구는 이 개발 도구에서만 쓰므로 로컬라이징하지 않고 고정 한국어로 둔다.
    /// </summary>
    public sealed class SaveResetWindow : EditorWindow
    {
        private SaveResetTargets selection = SaveResetTargets.None;
        private Vector2 scroll;

        // 정의 조회 캐시. 이름/기본 보유 여부를 보여 주기 위한 것이며 초기화 판정에는 쓰지 않는다.
        private Dictionary<string, ItemDefinition> itemsById;
        private Dictionary<string, BuildingDefinition> buildingsById;
        private Dictionary<string, CharacterDefinition> charactersById;

        // 삭제하려고 체크한 캐릭터 id. Character 비트가 켜질 때 삭제 가능한 캐릭터 전체로 채우고,
        // 개별 해제/재선택은 이 집합을 직접 고친다. 매 프레임 현재 삭제 가능 목록으로 걸러 낸다.
        private HashSet<string> selectedCharacterIds = new HashSet<string>(StringComparer.Ordinal);

        // Character 비트의 이전 상태. 꺼짐 -> 켜짐으로 바뀌는 순간에만 삭제 가능 캐릭터 전체를 고른다.
        private bool prevCharacterSelected;

        private GUIStyle highlightBox;

        [MenuItem("Tools/Reset")]
        public static void ShowWindow()
        {
            SaveResetWindow window = GetWindow<SaveResetWindow>(utility: false, title: "Save Reset");
            window.minSize = new Vector2(420f, 480f);
            window.RefreshDefinitions();
            window.Show();
        }

        private void OnEnable()
        {
            RefreshDefinitions();
        }

        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.LabelField("개발용 저장 데이터 초기화", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "고른 항목만 초기화합니다. Character는 체크한 캐릭터만 삭제하며, 계정 진행 등 나머지는 건드리지 않습니다.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();

            // ---- 상단: 다중 선택 드롭다운 ----
            selection = (SaveResetTargets)EditorGUILayout.EnumFlagsField("초기화 대상", selection);
            selection &= SaveResetTargets.All; // 정의되지 않은 비트는 버린다.

            EditorGUILayout.Space();

            SaveData data = SaveSystem.Data; // 최초 접근에서 실제 계정을 한 번 읽는다(읽기 전용 표시용).

            // Character 비트 상태에 맞춰 체크 목록을 동기화한다(그리기 전에 한다).
            bool characterSelected = (selection & SaveResetTargets.Character) != 0;
            HashSet<string> deletable = GetDeletableIds(data);
            if (characterSelected && !prevCharacterSelected)
            {
                // 꺼짐 -> 켜짐: 삭제 가능한 캐릭터를 모두 고른다.
                selectedCharacterIds = new HashSet<string>(deletable, StringComparer.Ordinal);
            }
            else if (!characterSelected)
            {
                selectedCharacterIds.Clear();
            }

            // 목록이 바뀌었을 수 있으니 지금 삭제 가능한 것만 남긴다(사라진 id는 자동으로 빠진다).
            selectedCharacterIds.IntersectWith(deletable);
            prevCharacterSelected = characterSelected;

            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawItemSection(data, (selection & SaveResetTargets.Item) != 0);
            DrawCurrencySection(data, (selection & SaveResetTargets.Currency) != 0);
            DrawConstructionSection(data, (selection & SaveResetTargets.Construction) != 0);
            DrawCharacterSection(data, characterSelected);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            // ---- 하단: 실행 ----
            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Play Mode 중에는 초기화할 수 없습니다.", MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("새로 고침", GUILayout.Height(24f)))
                {
                    RefreshDefinitions();
                    Repaint();
                }

                bool anyNonCharacter =
                    (selection & (SaveResetTargets.Item | SaveResetTargets.Currency | SaveResetTargets.Construction)) != 0;
                bool anyCharacterToDelete =
                    (selection & SaveResetTargets.Character) != 0 && selectedCharacterIds.Count > 0;
                bool canRun = anyNonCharacter || anyCharacterToDelete;

                using (new EditorGUI.DisabledScope(!canRun || EditorApplication.isPlaying))
                {
                    if (GUILayout.Button("Reset Selected", GUILayout.Height(24f)))
                    {
                        RunReset();
                    }
                }
            }
        }

        // ---- 섹션 그리기 ----

        private void DrawItemSection(SaveData data, bool highlighted)
        {
            using (BeginSection("Item", highlighted))
            {
                List<InventoryItemState> items = data.items ?? new List<InventoryItemState>();
                int totalCount = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] != null) totalCount += items[i].count;
                }

                EditorGUILayout.LabelField($"보유 종류 수: {items.Count}    총수량: {totalCount}");

                if (items.Count == 0)
                {
                    EditorGUILayout.LabelField("(보유 아이템 없음)", EditorStyles.miniLabel);
                    return;
                }

                foreach (InventoryItemState item in items)
                {
                    if (item == null) continue;
                    string label = DescribeItem(item.itemId);
                    EditorGUILayout.LabelField($"• {label} × {item.count}");
                }
            }
        }

        private void DrawCurrencySection(SaveData data, bool highlighted)
        {
            using (BeginSection("Currency", highlighted))
            {
                EditorGUILayout.LabelField($"현재 재화: {data.currency}");
            }
        }

        private void DrawConstructionSection(SaveData data, bool highlighted)
        {
            using (BeginSection("Construction", highlighted))
            {
                List<BuildingConstructionSaveState> records =
                    data.buildingConstructions ?? new List<BuildingConstructionSaveState>();

                EditorGUILayout.LabelField($"건축 기록 수: {records.Count}");

                if (records.Count == 0)
                {
                    EditorGUILayout.LabelField("(건축 기록 없음)", EditorStyles.miniLabel);
                    return;
                }

                DateTime nowUtc = DateTime.UtcNow;
                foreach (BuildingConstructionSaveState record in records)
                {
                    if (record == null) continue;
                    string label = DescribeBuilding(record.buildingId);
                    EditorGUILayout.LabelField($"• {label} — {DescribeConstructionStatus(record, nowUtc)}");
                }
            }
        }

        private static string DescribeConstructionStatus(BuildingConstructionSaveState record, DateTime nowUtc)
        {
            if (!SaveData.TryParseTimestamp(record.completeAtUtc, out DateTime completeUtc))
            {
                return "완성 시각 알 수 없음";
            }

            return nowUtc >= completeUtc ? "완료" : "건설 중";
        }

        private void DrawCharacterSection(SaveData data, bool sectionActive)
        {
            using (BeginSection("Character", sectionActive))
            {
                List<CharacterSaveState> characters = data.characters ?? new List<CharacterSaveState>();
                EditorGUILayout.LabelField($"보유 캐릭터 수: {characters.Count}");

                if (!sectionActive)
                {
                    EditorGUILayout.LabelField(
                        "상단에서 Character를 선택하면 개별 캐릭터를 고를 수 있습니다.", EditorStyles.miniLabel);
                }

                if (characters.Count == 0)
                {
                    EditorGUILayout.LabelField("(보유 캐릭터 없음)", EditorStyles.miniLabel);
                    return;
                }

                foreach (CharacterSaveState character in characters)
                {
                    if (character == null) continue;
                    DrawCharacterRow(character, sectionActive);
                }
            }
        }

        private void DrawCharacterRow(CharacterSaveState character, bool sectionActive)
        {
            string id = character.characterId ?? string.Empty;
            bool hasId = !string.IsNullOrEmpty(id);
            bool initiallyOwned = hasId && IsInitiallyOwned(id);
            bool selectable = sectionActive && hasId && !initiallyOwned;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!selectable))
                {
                    bool isChecked = hasId && selectedCharacterIds.Contains(id);
                    bool newChecked = EditorGUILayout.Toggle(isChecked, GUILayout.Width(18f));
                    if (selectable && newChecked != isChecked)
                    {
                        if (newChecked) selectedCharacterIds.Add(id);
                        else selectedCharacterIds.Remove(id);
                    }
                }

                string name = hasId ? DescribeCharacterName(id) : "(빈 characterId)";
                string info =
                    $"{name}  ·  Lv.{character.level}  ·  EXP {character.currentExp}  ·  행동력 {character.currentStamina}";
                EditorGUILayout.LabelField(info);

                if (initiallyOwned)
                {
                    EditorGUILayout.LabelField(
                        "기본 캐릭터 · 삭제 불가", EditorStyles.miniBoldLabel, GUILayout.Width(120f));
                }
            }
        }

        // ---- 실행 ----

        private void RunReset()
        {
            SaveData data = SaveSystem.Data;
            SaveResetTargets targets = selection & SaveResetTargets.All;

            // 캐릭터 삭제 대상과 보호 집합을 준비한다. Character 비트가 없으면 캐릭터는 건드리지 않는다.
            List<string> toRemove = null;
            List<string> protectedIds = null;
            if ((targets & SaveResetTargets.Character) != 0)
            {
                toRemove = new List<string>(selectedCharacterIds);
                protectedIds = GetProtectedIds(data);
            }

            bool anyNonCharacter =
                (targets & (SaveResetTargets.Item | SaveResetTargets.Currency | SaveResetTargets.Construction)) != 0;
            bool anyCharacterToDelete = toRemove != null && toRemove.Count > 0;
            if (!anyNonCharacter && !anyCharacterToDelete) return;

            string body = DescribeTargets(targets);
            if (anyCharacterToDelete)
            {
                body += "\n\n삭제할 캐릭터:\n" + DescribeCharacterList(toRemove);
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "저장 데이터 초기화",
                $"다음 항목을 초기화합니다:\n\n{body}\n\n선택하지 않은 캐릭터·계정 진행·회복소 등 나머지는 그대로 유지됩니다.\n계속할까요?",
                "초기화",
                "취소");

            if (!confirmed) return;

            SaveResetResult result =
                SaveResetService.Apply(data, targets, toRemove, protectedIds, SaveSystem.Save);

            RefreshDefinitions();
            Repaint();

            switch (result.Outcome)
            {
                case SaveResetOutcome.Success:
                    string done = DescribeTargets(result.AppliedTargets);
                    if (result.RemovedCharacterCount > 0)
                    {
                        done += $"\n\n삭제한 캐릭터 수: {result.RemovedCharacterCount}";
                    }
                    EditorUtility.DisplayDialog("초기화 완료", $"다음 항목을 초기화하고 저장했습니다:\n\n{done}", "확인");
                    break;

                case SaveResetOutcome.SaveFailed:
                    EditorUtility.DisplayDialog(
                        "초기화 실패",
                        "저장에 실패해 변경 사항을 모두 되돌렸습니다. Console 로그를 확인하세요.",
                        "확인");
                    break;

                case SaveResetOutcome.NothingSelected:
                    // 버튼이 비활성화되어 여기 오지 않지만, 방어적으로 아무것도 하지 않는다.
                    break;
            }
        }

        private static string DescribeTargets(SaveResetTargets targets)
        {
            var lines = new List<string>();
            if ((targets & SaveResetTargets.Item) != 0) lines.Add("• Item (보유 아이템 전체)");
            if ((targets & SaveResetTargets.Currency) != 0) lines.Add("• Currency (재화 0으로)");
            if ((targets & SaveResetTargets.Construction) != 0)
            {
                lines.Add("• Construction (건축·모집 주기 기록 전체)");
            }
            if ((targets & SaveResetTargets.Character) != 0)
            {
                lines.Add("• Character (선택한 캐릭터만 삭제)");
            }
            return lines.Count == 0 ? "(없음)" : string.Join("\n", lines);
        }

        // ---- 캐릭터 조회 ----

        /// <summary>지금 삭제할 수 있는 캐릭터 id(저장에 존재하고 기본 보유가 아닌 것).</summary>
        private HashSet<string> GetDeletableIds(SaveData data)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (data.characters == null) return set;

            foreach (CharacterSaveState character in data.characters)
            {
                if (character == null) continue;
                string id = character.characterId;
                if (string.IsNullOrEmpty(id)) continue;
                if (IsInitiallyOwned(id)) continue;
                set.Add(id);
            }

            return set;
        }

        /// <summary>절대 지우면 안 되는 기본 보유 캐릭터 id(저장에 존재하는 것만).</summary>
        private List<string> GetProtectedIds(SaveData data)
        {
            var list = new List<string>();
            if (data.characters == null) return list;

            foreach (CharacterSaveState character in data.characters)
            {
                if (character == null) continue;
                string id = character.characterId;
                if (!string.IsNullOrEmpty(id) && IsInitiallyOwned(id)) list.Add(id);
            }

            return list;
        }

        private bool IsInitiallyOwned(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return false;
            if (charactersById != null && charactersById.TryGetValue(characterId, out CharacterDefinition def) && def != null)
            {
                return def.InitiallyOwned;
            }

            // 정의를 못 찾으면 기본 보유 여부를 알 수 없다 - 보호하지 않는다(개발자가 지우려는 값일 수 있다).
            return false;
        }

        private string DescribeCharacterName(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return "(빈 characterId)";

            if (charactersById != null && charactersById.TryGetValue(characterId, out CharacterDefinition def) && def != null)
            {
                string name = ResolveLocalized(def.LocalizedName) ?? def.DisplayName;
                if (!string.IsNullOrEmpty(name) && !string.Equals(name, characterId, StringComparison.Ordinal))
                {
                    return $"{name} ({characterId})";
                }
            }

            return characterId;
        }

        private string DescribeCharacterList(IReadOnlyList<string> characterIds)
        {
            var lines = new List<string>(characterIds.Count);
            foreach (string id in characterIds) lines.Add("• " + DescribeCharacterName(id));
            return string.Join("\n", lines);
        }

        // ---- 이름 조회 ----

        private void RefreshDefinitions()
        {
            itemsById = BuildMap<ItemDefinition>(def => def.ItemId);
            buildingsById = BuildMap<BuildingDefinition>(def => def.BuildingId);
            charactersById = BuildMap<CharacterDefinition>(def => def.CharacterId);
        }

        private static Dictionary<string, T> BuildMap<T>(Func<T, string> keyOf) where T : UnityEngine.Object
        {
            var map = new Dictionary<string, T>(StringComparer.Ordinal);
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null) continue;

                string key = keyOf(asset);
                if (string.IsNullOrEmpty(key)) continue;

                // id가 겹치면 먼저 찾은 것을 남긴다(카탈로그와 같은 규칙 - 나중 중복이 밀어내지 않는다).
                if (!map.ContainsKey(key)) map[key] = asset;
            }

            return map;
        }

        private string DescribeItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "(빈 itemId)";

            if (itemsById != null && itemsById.TryGetValue(itemId, out ItemDefinition def) && def != null)
            {
                string name = ResolveLocalized(def.LocalizedName) ?? def.DisplayName;
                if (!string.IsNullOrEmpty(name) && !string.Equals(name, itemId, StringComparison.Ordinal))
                {
                    return $"{name} ({itemId})";
                }
            }

            return itemId;
        }

        private string DescribeBuilding(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return "(빈 buildingId)";

            if (buildingsById != null && buildingsById.TryGetValue(buildingId, out BuildingDefinition def) && def != null)
            {
                string name = ResolveLocalized(def.LocalizedName) ?? def.name;
                if (!string.IsNullOrEmpty(name) && !string.Equals(name, buildingId, StringComparison.Ordinal))
                {
                    return $"{name} ({buildingId})";
                }
            }

            return buildingId;
        }

        /// <summary>편집 모드에서 로컬라이징 문자열을 시도해 본다. 로케일/테이블이 준비되지 않았거나
        /// 참조가 비어 있으면 조용히 null을 돌려준다 - 이름 표시가 실패해도 창은 계속 떠 있어야 한다.</summary>
        private static string ResolveLocalized(LocalizedString reference)
        {
            if (reference == null || reference.IsEmpty) return null;

            try
            {
                string value = reference.GetLocalizedString();
                return string.IsNullOrEmpty(value) ? null : value;
            }
            catch
            {
                return null;
            }
        }

        // ---- 강조 스타일 ----

        private void EnsureStyles()
        {
            if (highlightBox != null) return;

            highlightBox = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 8, 8),
            };
        }

        /// <summary>섹션 하나를 상자로 감싼다. 선택된 대상이면 경고색으로 강조한다.</summary>
        private SectionScope BeginSection(string title, bool highlighted)
        {
            Color previous = GUI.backgroundColor;
            if (highlighted)
            {
                // 경고색(주황) 배경 - 지금 초기화 대상임을 한눈에 보이게 한다.
                GUI.backgroundColor = new Color(1f, 0.6f, 0.2f, 1f);
            }

            EditorGUILayout.BeginVertical(highlightBox);
            GUI.backgroundColor = previous;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                if (highlighted)
                {
                    EditorGUILayout.LabelField("초기화 대상", EditorStyles.miniBoldLabel, GUILayout.Width(70f));
                }
            }

            return new SectionScope();
        }

        /// <summary><see cref="EditorGUILayout.EndVertical"/>를 using 블록으로 닫기 위한 얇은 래퍼.</summary>
        private readonly struct SectionScope : IDisposable
        {
            public void Dispose()
            {
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
        }
    }
}
