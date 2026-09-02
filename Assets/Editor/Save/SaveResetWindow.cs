using System;
using System.Collections.Generic;
using Building;
using Character;
using Common;
using Inventory;
using Party;
using Quest;
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
        private const string StoryQuestCatalogPath =
            "Assets/Generated/TableData/CharacterStoryQuest/CharacterStoryQuestCatalog.asset";
        private const string CharacterCatalogPath =
            "Assets/Generated/TableData/Character/CharacterCatalog.asset";
        private const string PartyConfigCatalogPath =
            "Assets/Generated/TableData/PartyConfig/PartyConfigCatalog.asset";

        private SaveResetTargets selection = SaveResetTargets.None;
        private Vector2 scroll;

        // 정의 조회 캐시. 이름/기본 보유 여부를 보여 주기 위한 것이며 초기화 판정에는 쓰지 않는다.
        private Dictionary<string, ItemDefinition> itemsById;
        private Dictionary<string, BuildingDefinition> buildingsById;
        private Dictionary<string, CharacterDefinition> charactersById;
        private CharacterCatalog characterCatalog;
        private PartyConfigCatalog partyConfigCatalog;
        private List<StoryQuestResetDefinition> storyQuestDefinitions = new List<StoryQuestResetDefinition>();

        private string specifiedQuestId = string.Empty;
        private string questResetMessage = string.Empty;
        private MessageType questResetMessageType = MessageType.None;

        // 로컬라이징 이름 조회 캐시. OnGUI는 자주 호출되므로 매 프레임 같은 에디터 에셋을 다시 뒤지지 않는다.
        // 키: 테이블 참조 + 엔트리 참조. 값: 확인된 문자열 또는 조회 실패(null). RefreshDefinitions에서 비운다.
        private readonly Dictionary<string, string> localizedNameCache = new Dictionary<string, string>(StringComparer.Ordinal);

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
                "고른 항목만 초기화합니다. Character는 기본 캐릭터를 초기 상태로 복원하고 체크한 비기본 캐릭터를 삭제합니다.",
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
            DrawQuestSection(data, (selection & SaveResetTargets.Quest) != 0);

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
                    (selection & (SaveResetTargets.Item | SaveResetTargets.Currency | SaveResetTargets.Construction |
                                  SaveResetTargets.Quest)) != 0;
                bool canRun = anyNonCharacter || (selection & SaveResetTargets.Character) != 0;

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

            if (nowUtc < completeUtc) return "건설 중";
            return record.completionNotified ? "완료" : "완료 확인 대기";
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
                        "Character 선택 시 기본 캐릭터는 초기화·복구하고, 체크한 비기본 캐릭터는 삭제합니다.",
                        EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "기본 캐릭터는 삭제할 수 없으며 레벨·EXP·행동력·오염·패시브 회복 진행이 초기값으로 돌아갑니다. " +
                        "저장에 누락돼 있어도 Character Catalog에서 복구합니다.",
                        EditorStyles.wordWrappedMiniLabel);
                }

                if (characters.Count == 0)
                {
                    EditorGUILayout.LabelField("(보유 캐릭터 없음)", EditorStyles.miniLabel);
                }
                else
                {
                    foreach (CharacterSaveState character in characters)
                    {
                        if (character == null) continue;
                        DrawCharacterRow(character, sectionActive);
                    }
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
                $"{name}  ·  Lv.{character.level}  ·  EXP {character.currentExp}  ·  행동력 {character.currentStamina}  ·  오염도 {character.currentCorruption:0.###} / 300";
                EditorGUILayout.LabelField(info);

                if (initiallyOwned)
                {
                    EditorGUILayout.LabelField(
                        sectionActive ? "기본 · 상태 초기화" : "기본 · 삭제 불가",
                        EditorStyles.miniBoldLabel, GUILayout.Width(120f));
                }
            }
        }

        private void DrawQuestSection(SaveData data, bool highlighted)
        {
            using (BeginSection("Quest", highlighted))
            {
                List<CharacterStoryQuestSaveState> states = data.characterStoryQuests ??
                                                           new List<CharacterStoryQuestSaveState>();
                EditorGUILayout.LabelField($"서사 퀘스트 상태 수: {states.Count}");
                if (highlighted)
                {
                    EditorGUILayout.LabelField(
                        "Reset Selected 실행 시 보유 캐릭터 전체를 각 서사의 1단계로 초기화합니다.",
                        EditorStyles.wordWrappedMiniLabel);
                }

                if (states.Count == 0)
                {
                    EditorGUILayout.LabelField("(저장된 서사 퀘스트 상태 없음)", EditorStyles.miniLabel);
                }
                else
                {
                    foreach (CharacterStoryQuestSaveState state in states)
                    {
                        if (state == null) continue;
                        string character = DescribeCharacterName(state.characterId);
                        string active = string.IsNullOrEmpty(state.activeQuestId) ? "(완료/없음)" : state.activeQuestId;
                        int completed = state.completedQuestIds?.Count ?? 0;
                        string suffix = state.graduated ? " · 졸업" : state.readyToComplete ? " · 완료 가능" : string.Empty;
                        EditorGUILayout.LabelField($"• {character} — {active} · 완료 {completed}{suffix}");
                    }
                }

                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("지정 초기화", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "퀘스트 ID를 입력하면 그 퀘스트의 캐릭터만 해당 단계가 막 시작된 상태로 초기화합니다.",
                    EditorStyles.wordWrappedMiniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    specifiedQuestId = EditorGUILayout.TextField("퀘스트 ID", specifiedQuestId);
                    using (new EditorGUI.DisabledScope(EditorApplication.isPlaying ||
                                                       string.IsNullOrWhiteSpace(specifiedQuestId)))
                    {
                        if (GUILayout.Button("적용", GUILayout.Width(64f)))
                        {
                            RunSpecifiedQuestReset();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(questResetMessage))
                {
                    EditorGUILayout.HelpBox(questResetMessage, questResetMessageType);
                }
            }
        }

        // ---- 실행 ----

        private void RunReset()
        {
            SaveData data = SaveSystem.Data;
            SaveResetTargets targets = selection & SaveResetTargets.All;

            if ((targets & SaveResetTargets.Quest) != 0 && storyQuestDefinitions.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "퀘스트 초기화 실패",
                    "캐릭터 서사 퀘스트 카탈로그를 불러오지 못했습니다. Generated 테이블을 확인한 뒤 다시 시도하세요.",
                    "확인");
                return;
            }

            // Character 비트가 켜지면 저장 목록이 아니라 catalog 전체에서 InitiallyOwned 시드를 만든다.
            List<string> toRemove = null;
            List<InitialCharacterResetSeed> initialSeeds = null;
            int partySlotCount = 0;
            if ((targets & SaveResetTargets.Character) != 0)
            {
                toRemove = new List<string>(selectedCharacterIds);
                initialSeeds = BuildInitialCharacterSeeds(characterCatalog);
                partySlotCount = ResolvePartySlotCount(partyConfigCatalog);
                if (initialSeeds.Count == 0 || partySlotCount < 1)
                {
                    EditorUtility.DisplayDialog(
                        "캐릭터 초기화 실패",
                        "Character Catalog의 기본 보유 캐릭터 또는 PartyConfig/default의 유효한 슬롯 수를 " +
                        "불러오지 못했습니다. Generated 테이블을 확인한 뒤 다시 시도하세요.",
                        "확인");
                    return;
                }
            }

            bool anyNonCharacter =
                (targets & (SaveResetTargets.Item | SaveResetTargets.Currency | SaveResetTargets.Construction |
                            SaveResetTargets.Quest)) != 0;
            bool resetCharacters = (targets & SaveResetTargets.Character) != 0;
            if (!anyNonCharacter && !resetCharacters) return;

            string body = DescribeTargets(targets);
            if (resetCharacters)
            {
                body += "\n\n초기화·복구할 기본 캐릭터:\n" +
                        DescribeCharacterList(ConvertSeedIds(initialSeeds));
            }
            if (toRemove != null && toRemove.Count > 0)
            {
                body += "\n\n삭제할 캐릭터:\n" + DescribeCharacterList(toRemove);
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "저장 데이터 초기화",
                $"다음 항목을 초기화합니다:\n\n{body}\n\nCharacter 대상의 파티는 기본 편성으로 복원되고, " +
                "초기화·삭제 캐릭터의 회복/정화 슬롯은 같은 인덱스에서 비워집니다. " +
                "Quest를 선택하지 않으면 기본 캐릭터의 퀘스트 진행은 유지됩니다.\n계속할까요?",
                "초기화",
                "취소");

            if (!confirmed) return;

            SaveResetResult result =
                SaveResetService.Apply(
                    data, targets, toRemove, initialSeeds, partySlotCount, storyQuestDefinitions, SaveSystem.Save);

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
                    if (result.ResetInitialCharacterCount > 0)
                    {
                        done += $"\n초기화·복구한 기본 캐릭터 수: {result.ResetInitialCharacterCount}";
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

                case SaveResetOutcome.InvalidCharacterResetConfiguration:
                    EditorUtility.DisplayDialog(
                        "캐릭터 초기화 실패",
                        "기본 캐릭터 시드 또는 파티 슬롯 계약이 유효하지 않아 아무것도 변경하거나 저장하지 않았습니다.",
                        "확인");
                    break;
            }
        }

        private void RunSpecifiedQuestReset()
        {
            string questId = specifiedQuestId?.Trim() ?? string.Empty;
            StoryQuestResetDefinition? definition = FindQuestDefinition(questId);
            if (!definition.HasValue)
            {
                questResetMessage = "퀘스트 리셋 실패: 퀘스트 테이블에 일치하는 ID가 없습니다. ID를 다시 확인하세요.";
                questResetMessageType = MessageType.Error;
                Repaint();
                return;
            }

            StoryQuestResetDefinition target = definition.Value;
            bool confirmed = EditorUtility.DisplayDialog(
                "지정 퀘스트 초기화",
                $"{DescribeCharacterName(target.CharacterId)}의 서사 퀘스트를\n{target.QuestId}\n단계가 막 시작된 상태로 초기화합니다. 계속할까요?",
                "적용",
                "취소");
            if (!confirmed) return;

            StoryQuestResetOutcome outcome = SaveResetService.ResetStoryQuestTo(
                SaveSystem.Data, target.QuestId, storyQuestDefinitions, SaveSystem.Save);
            switch (outcome)
            {
                case StoryQuestResetOutcome.Success:
                    questResetMessage = $"{DescribeCharacterName(target.CharacterId)}: {target.QuestId} 단계로 초기화했습니다.";
                    questResetMessageType = MessageType.Info;
                    break;
                case StoryQuestResetOutcome.QuestNotFound:
                    questResetMessage = "퀘스트 리셋 실패: 퀘스트 테이블에 일치하는 ID가 없습니다. ID를 다시 확인하세요.";
                    questResetMessageType = MessageType.Error;
                    break;
                case StoryQuestResetOutcome.InvalidQuestChain:
                    questResetMessage = "퀘스트 리셋 실패: 선행 퀘스트 연결이 올바르지 않습니다. 테이블을 확인하세요.";
                    questResetMessageType = MessageType.Error;
                    break;
                case StoryQuestResetOutcome.SaveFailed:
                    questResetMessage = "퀘스트 리셋 저장에 실패해 변경을 되돌렸습니다. Console 로그를 확인하세요.";
                    questResetMessageType = MessageType.Error;
                    break;
            }

            RefreshDefinitions();
            Repaint();
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
                lines.Add("• Character (기본 캐릭터 초기화·복구 + 선택한 비기본 캐릭터 삭제 + 초기 파티 복원)");
            }
            if ((targets & SaveResetTargets.Quest) != 0)
            {
                lines.Add("• Quest (보유 캐릭터 서사 퀘스트를 각 1단계로)");
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

        internal static List<InitialCharacterResetSeed> BuildInitialCharacterSeeds(CharacterCatalog catalog)
        {
            var result = new List<InitialCharacterResetSeed>();
            if (catalog == null) return result;
            foreach (CharacterDefinition definition in catalog.Characters)
            {
                if (definition == null || !definition.InitiallyOwned) continue;
                result.Add(new InitialCharacterResetSeed(definition.CharacterId, definition.BaseCorruption));
            }
            return result;
        }

        internal static int ResolvePartySlotCount(PartyConfigCatalog catalog)
        {
            PartyConfigDefinition config = catalog != null ? catalog.Find(PartyConfigIds.Default) : null;
            return config != null && config.IsValid ? config.BaseCapacity : 0;
        }

        private static List<string> ConvertSeedIds(IReadOnlyList<InitialCharacterResetSeed> seeds)
        {
            var result = new List<string>();
            if (seeds == null) return result;
            foreach (InitialCharacterResetSeed seed in seeds) result.Add(seed.CharacterId);
            return result;
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
            characterCatalog = LoadGeneratedCatalog<CharacterCatalog>(CharacterCatalogPath);
            partyConfigCatalog = LoadGeneratedCatalog<PartyConfigCatalog>(PartyConfigCatalogPath);
            charactersById = BuildCharacterMap(characterCatalog);
            storyQuestDefinitions = BuildStoryQuestDefinitions();

            // 정의를 다시 읽었으니 이전 조회 결과도 버린다(테이블 값이 바뀌었을 수 있다).
            localizedNameCache.Clear();
        }

        private static List<StoryQuestResetDefinition> BuildStoryQuestDefinitions()
        {
            var definitions = new List<StoryQuestResetDefinition>();
            CharacterStoryQuestCatalog catalog =
                AssetDatabase.LoadAssetAtPath<CharacterStoryQuestCatalog>(StoryQuestCatalogPath);
            if (catalog == null)
            {
                // 생성 에셋 경로가 바뀐 개발 중 상태를 위한 읽기 전용 폴백. 여러 카탈로그가 있으면
                // 가장 먼저 발견한 유효 카탈로그만 사용한다.
                string[] guids = AssetDatabase.FindAssets("t:CharacterStoryQuestCatalog");
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    catalog = AssetDatabase.LoadAssetAtPath<CharacterStoryQuestCatalog>(path);
                    if (catalog != null) break;
                }
            }

            if (catalog == null) return definitions;
            foreach (CharacterStoryQuestDefinition quest in catalog.Quests)
            {
                if (quest == null) continue;
                definitions.Add(new StoryQuestResetDefinition(
                    quest.QuestId, quest.CharacterId, quest.PreviousQuestId, quest.Enabled));
            }
            return definitions;
        }

        private StoryQuestResetDefinition? FindQuestDefinition(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId)) return null;
            foreach (StoryQuestResetDefinition definition in storyQuestDefinitions)
            {
                if (definition.IsValid && string.Equals(definition.QuestId, questId, StringComparison.Ordinal))
                {
                    return definition;
                }
            }
            return null;
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

        private static Dictionary<string, CharacterDefinition> BuildCharacterMap(CharacterCatalog catalog)
        {
            var map = new Dictionary<string, CharacterDefinition>(StringComparer.Ordinal);
            if (catalog == null) return map;
            foreach (CharacterDefinition definition in catalog.Characters)
            {
                if (definition == null || string.IsNullOrEmpty(definition.CharacterId)) continue;
                if (!map.ContainsKey(definition.CharacterId)) map.Add(definition.CharacterId, definition);
            }
            return map;
        }

        private static T LoadGeneratedCatalog<T>(string path) where T : UnityEngine.Object
        {
            return AssetDatabase.LoadAssetAtPath<T>(path);
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
            if (buildingId == "1") return "여관 (1)";
            if (buildingId == "2") return "교회 (2)";

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

        /// <summary>편집 모드에서 이름의 ko-KR 로컬라이징 값을 에디터 에셋에서 직접 조회한다. 테이블·엔트리·값이
        /// 없거나 참조가 비어 있으면 조용히 null을 돌려준다 - 이름 표시가 실패해도 창은 계속 떠 있어야 한다.
        /// 런타임 <c>GetLocalizedString</c>·<c>SelectedLocale</c>을 쓰지 않으므로 Edit Mode에서 로케일이
        /// 준비되지 않아도 오류 로그가 나지 않는다. 자주 호출되는 OnGUI를 위해 결과(실패 포함)를 캐시한다.</summary>
        private string ResolveLocalized(LocalizedString reference)
        {
            if (reference == null || reference.IsEmpty) return null;

            string key = LocalizedCacheKey(reference);
            if (localizedNameCache.TryGetValue(key, out string cached)) return cached;

            string value = SaveResetLocalization.Resolve(reference);
            localizedNameCache[key] = value; // null(조회 실패)도 캐시해 매 프레임 재조회를 막는다.
            return value;
        }

        /// <summary>테이블 참조 + 엔트리 참조로 캐시 키를 만든다.</summary>
        private static string LocalizedCacheKey(LocalizedString reference)
        {
            return reference.TableReference + "|" + reference.TableEntryReference;
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
