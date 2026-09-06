using System.Collections.Generic;
using System.Reflection;
using Character;
using CharacterArchive;
using Common;
using NUnit.Framework;
using Recruitment;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace CharacterArchiveEditorTests
{
    public sealed class CharacterUnlockInfoControllerTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object value in created) if (value != null) Object.DestroyImmediate(value);
            created.Clear();
        }

        [Test]
        public void Rebind_PoolsRowsRestoresStyleAndShowsPermanentCompletionAfterCurrentRegression()
        {
            CharacterUnlockConditionDefinition condition = CreateCondition("unlock",
                ("level", "same", "MAX_OWNED_CHARACTER_LEVEL_AT_LEAST", 10),
                ("count", "same", "OWNED_CHARACTER_COUNT_AT_LEAST", 1));
            CharacterAcquisitionCatalog acquisitions = Create<CharacterAcquisitionCatalog>();
            CharacterAcquisitionDefinition acquisition = Create<CharacterAcquisitionDefinition>();
            Set(acquisition, "characterId", "Barbarian"); Set(acquisition, "conditionId", "unlock"); Set(acquisition, "enabled", true);
            Set(acquisitions, "acquisitions", new List<CharacterAcquisitionDefinition> { acquisition }); acquisitions.MarkDirty();
            CharacterUnlockConditionCatalog conditions = Create<CharacterUnlockConditionCatalog>();
            Set(conditions, "conditions", new List<CharacterUnlockConditionDefinition> { condition }); conditions.MarkDirty();

            GameObject host = Track(new GameObject("unlock-info", typeof(RectTransform)));
            CharacterUnlockInfoController controller = host.AddComponent<CharacterUnlockInfoController>();
            TMP_Text title = NewText(host.transform, "title");
            LocalizedTMPText titleLocalizer = title.gameObject.AddComponent<LocalizedTMPText>();
            TMP_Text count = NewText(host.transform, "count");
            LocalizedTMPText countLocalizer = count.gameObject.AddComponent<LocalizedTMPText>(); countLocalizer.enabled = false;
            RectTransform content = new GameObject("content", typeof(RectTransform)).GetComponent<RectTransform>(); Track(content.gameObject); content.SetParent(host.transform, false);
            TMP_Text template = NewText(content, "template"); template.fontStyle = FontStyles.Bold; template.color = Color.green; template.gameObject.SetActive(false);
            GameObject check = Track(new GameObject("sp_check", typeof(RectTransform))); check.transform.SetParent(template.transform, false);
            GameObject checkOn = Track(new GameObject("sp_checkOn", typeof(RectTransform))); checkOn.transform.SetParent(check.transform, false); checkOn.SetActive(false);
            GameObject complete = Track(new GameObject("complete")); complete.transform.SetParent(content, false);
            Set(controller, "acquisitionCatalog", acquisitions); Set(controller, "conditionCatalog", conditions);
            Set(controller, "titleText", title); Set(controller, "countText", count); Set(controller, "countLocalizer", countLocalizer);
            Set(controller, "conditionContent", content); Set(controller, "conditionTemplate", template); Set(controller, "completeRoot", complete);

            CharacterDefinition character = Create<CharacterDefinition>(); Set(character, "characterId", "Barbarian");
            SaveData data = new SaveData { characters = new List<CharacterSaveState> { new CharacterSaveState { characterId = "CatKnight", level = 1 } } };
            controller.BindCharacter(character, data);
            Assert.IsTrue(titleLocalizer.enabled, "정적 제목 로컬라이저는 컨트롤러가 끄면 안 된다.");
            Assert.AreEqual("(1/2)", count.text, "진행 수치는 제목이 아니라 별도 카운트에 표시한다.");
            Assert.AreEqual(2, controller.ActiveLineCount);
            Assert.AreEqual(2, controller.PooledLineCount);
            Assert.IsFalse(complete.activeSelf);
            TMP_Text first = content.GetChild(1).GetComponent<TMP_Text>();
            TMP_Text second = content.GetChild(2).GetComponent<TMP_Text>();
            Assert.AreEqual(FontStyles.Bold, first.fontStyle);
            Assert.AreEqual(Color.green, first.color);
            Assert.AreEqual(FontStyles.Bold, second.fontStyle, "완료 행도 취소선 없이 템플릿 스타일을 유지한다.");
            Assert.IsFalse(first.transform.Find("sp_check/sp_checkOn").gameObject.activeSelf);
            Assert.IsTrue(second.transform.Find("sp_check/sp_checkOn").gameObject.activeSelf);
            Assert.AreSame(first.transform, content.GetChild(1));
            Assert.AreSame(second.transform, content.GetChild(2));
            Assert.AreSame(complete.transform, content.GetChild(3), "완료 안내는 모든 조건 행 뒤에 남아야 한다.");

            data.characters[0].level = 10;
            controller.BindCharacter(character, data);
            AssertRowOrder(content, template, complete, "10/10", "1/1");
            Assert.IsTrue(complete.activeSelf);
            Assert.AreEqual(2, controller.PooledLineCount, "재바인드가 조건 행을 중복 생성하면 안 된다.");
            data.characters[0].level = 1;
            data.unlockedRecruitmentCharacterIds = new List<string> { "Barbarian" };
            controller.BindCharacter(character, data);
            Assert.AreEqual(FontStyles.Bold, first.fontStyle, "미충족으로 돌아온 행은 템플릿 스타일을 복원한다.");
            Assert.AreEqual(Color.green, first.color);
            Assert.IsFalse(first.transform.Find("sp_check/sp_checkOn").gameObject.activeSelf,
                "풀 재사용 뒤 미충족 행의 체크 표시는 반드시 꺼진다.");
            Assert.IsTrue(complete.activeSelf, "영구 모집 자격은 현재 수치 후퇴와 분리된다.");
            AssertRowOrder(content, template, complete, "1/10", "1/1");
        }

        [Test]
        public void RebindAndRefresh_KeepPooledConditionOrderAcrossCharacterAndConditionCountChanges()
        {
            CharacterUnlockConditionDefinition twoConditions = CreateCondition("two",
                ("level", "same", "MAX_OWNED_CHARACTER_LEVEL_AT_LEAST", 4),
                ("count", "same", "OWNED_CHARACTER_COUNT_AT_LEAST", 2));
            CharacterUnlockConditionDefinition oneCondition = CreateCondition("one",
                ("count", "same", "OWNED_CHARACTER_COUNT_AT_LEAST", 3));
            CharacterAcquisitionCatalog acquisitions = Create<CharacterAcquisitionCatalog>();
            CharacterAcquisitionDefinition firstAcquisition = CreateAcquisition("First", "two");
            CharacterAcquisitionDefinition secondAcquisition = CreateAcquisition("Second", "one");
            Set(acquisitions, "acquisitions", new List<CharacterAcquisitionDefinition> { firstAcquisition, secondAcquisition }); acquisitions.MarkDirty();
            CharacterUnlockConditionCatalog conditions = Create<CharacterUnlockConditionCatalog>();
            Set(conditions, "conditions", new List<CharacterUnlockConditionDefinition> { twoConditions, oneCondition }); conditions.MarkDirty();

            CharacterUnlockInfoController controller = CreateController(acquisitions, conditions, out RectTransform content,
                out TMP_Text template, out GameObject complete);
            CharacterDefinition first = Create<CharacterDefinition>(); Set(first, "characterId", "First");
            CharacterDefinition second = Create<CharacterDefinition>(); Set(second, "characterId", "Second");
            SaveData data = new SaveData { characters = new List<CharacterSaveState> { new CharacterSaveState { characterId = "Owned", level = 4 } } };

            controller.BindCharacter(first, data);
            AssertRowOrder(content, template, complete, "4/4", "1/2");
            controller.Refresh();
            AssertRowOrder(content, template, complete, "4/4", "1/2");
            controller.BindCharacter(second, data);
            Assert.AreEqual(1, controller.ActiveLineCount);
            AssertRowOrder(content, template, complete, "1/3");
            data.characters.Add(new CharacterSaveState { characterId = "OwnedTwo", level = 1 });
            controller.BindCharacter(first, data);
            AssertRowOrder(content, template, complete, "4/4", "2/2");
            controller.Refresh();
            AssertRowOrder(content, template, complete, "4/4", "2/2");
        }

        [Test]
        public void CharacterArchivePrefab_RuntimeConditionRowsStayBeforeCompleteAfterRepeatedBind()
        {
            CharacterUnlockConditionDefinition condition = CreateCondition("prefab-two",
                ("level", "same", "MAX_OWNED_CHARACTER_LEVEL_AT_LEAST", 4),
                ("count", "same", "OWNED_CHARACTER_COUNT_AT_LEAST", 2));
            CharacterAcquisitionCatalog acquisitions = Create<CharacterAcquisitionCatalog>();
            Set(acquisitions, "acquisitions", new List<CharacterAcquisitionDefinition> { CreateAcquisition("PrefabCharacter", "prefab-two") }); acquisitions.MarkDirty();
            CharacterUnlockConditionCatalog conditions = Create<CharacterUnlockConditionCatalog>();
            Set(conditions, "conditions", new List<CharacterUnlockConditionDefinition> { condition }); conditions.MarkDirty();
            GameObject root = PrefabUtility.LoadPrefabContents("Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab");
            try
            {
                CharacterUnlockInfoController controller = root.GetComponentInChildren<CharacterUnlockInfoController>(true);
                Assert.NotNull(controller);
                Set(controller, "acquisitionCatalog", acquisitions); Set(controller, "conditionCatalog", conditions);
                RectTransform content = (RectTransform)Get(controller, "conditionContent");
                TMP_Text template = (TMP_Text)Get(controller, "conditionTemplate");
                GameObject complete = (GameObject)Get(controller, "completeRoot");
                CharacterDefinition character = Create<CharacterDefinition>(); Set(character, "characterId", "PrefabCharacter");
                SaveData data = new SaveData { characters = new List<CharacterSaveState> { new CharacterSaveState { characterId = "Owned", level = 4 } } };

                controller.BindCharacter(character, data);
                controller.Refresh();
                AssertPrefabRowOrder(content, template, complete, 2);
                controller.Refresh();
                AssertPrefabRowOrder(content, template, complete, 2);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void ImmediateUnlock_KeepsStaticTitleHidesCountAndShowsCompletion()
        {
            CharacterAcquisitionCatalog acquisitions = Create<CharacterAcquisitionCatalog>();
            Set(acquisitions, "acquisitions", new List<CharacterAcquisitionDefinition>
            {
                CreateAcquisition("Immediate", string.Empty)
            });
            acquisitions.MarkDirty();
            CharacterUnlockConditionCatalog conditions = Create<CharacterUnlockConditionCatalog>();
            Set(conditions, "conditions", new List<CharacterUnlockConditionDefinition>()); conditions.MarkDirty();

            CharacterUnlockInfoController controller = CreateController(acquisitions, conditions, out RectTransform content,
                out TMP_Text template, out GameObject complete);
            TMP_Text title = (TMP_Text)Get(controller, "titleText");
            TMP_Text count = (TMP_Text)Get(controller, "countText");
            LocalizedTMPText titleLocalizer = title.GetComponent<LocalizedTMPText>();
            LocalizedTMPText countLocalizer = (LocalizedTMPText)Get(controller, "countLocalizer");
            title.text = "등장 조건";
            CharacterDefinition character = Create<CharacterDefinition>(); Set(character, "characterId", "Immediate");

            controller.BindCharacter(character, new SaveData());
            Assert.AreEqual("등장 조건", title.text, "정적 제목은 선택 해제/빈 조건에도 지우지 않는다.");
            Assert.IsTrue(titleLocalizer.enabled, "정적 01/98 제목의 로컬라이저는 계속 활성 상태여야 한다.");
            Assert.IsFalse(countLocalizer.enabled, "동적 카운트는 LocalizedTMPText와 중복 갱신하지 않는다.");
            Assert.IsFalse(count.gameObject.activeSelf, "조건이 없으면 카운트 오브젝트를 숨긴다.");
            Assert.AreEqual(string.Empty, count.text);
            Assert.AreEqual(0, controller.ActiveLineCount);
            Assert.IsTrue(complete.activeSelf, "조건 없는 즉시 등장은 완료 안내를 유지한다.");

            typeof(CharacterUnlockInfoController).GetMethod("ApplyCountFormat", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { "완료 {0} / 전체 {1}" });
            Assert.IsFalse(count.gameObject.activeSelf, "locale 변경 콜백도 빈 조건 카운트를 다시 보이면 안 된다.");
            Assert.AreEqual(string.Empty, count.text);
            Assert.AreSame(template.transform, content.GetChild(0));

            controller.BindCharacter(null, new SaveData());
            Assert.AreEqual("등장 조건", title.text, "character가 없어도 정적 제목은 지우지 않는다.");
            Assert.AreEqual(string.Empty, count.text, "character가 없을 때는 동적 카운트만 비운다.");
            Assert.IsFalse(count.gameObject.activeSelf, "character가 없을 때 stale 카운트를 숨긴다.");
        }

        [Test]
        public void Rebind_ConditionCountReactivatesAfterImmediateUnlockHidesIt()
        {
            CharacterUnlockConditionDefinition condition = CreateCondition("one",
                ("count", "same", "OWNED_CHARACTER_COUNT_AT_LEAST", 1));
            CharacterAcquisitionCatalog acquisitions = Create<CharacterAcquisitionCatalog>();
            Set(acquisitions, "acquisitions", new List<CharacterAcquisitionDefinition>
            {
                CreateAcquisition("Conditional", "one"),
                CreateAcquisition("Immediate", string.Empty)
            });
            acquisitions.MarkDirty();
            CharacterUnlockConditionCatalog conditions = Create<CharacterUnlockConditionCatalog>();
            Set(conditions, "conditions", new List<CharacterUnlockConditionDefinition> { condition }); conditions.MarkDirty();
            CharacterUnlockInfoController controller = CreateController(acquisitions, conditions, out _, out _, out _);
            TMP_Text count = (TMP_Text)Get(controller, "countText");
            CharacterDefinition conditional = Create<CharacterDefinition>(); Set(conditional, "characterId", "Conditional");
            CharacterDefinition immediate = Create<CharacterDefinition>(); Set(immediate, "characterId", "Immediate");
            SaveData data = new SaveData();

            controller.BindCharacter(conditional, data);
            Assert.IsTrue(count.gameObject.activeSelf);
            Assert.AreEqual("(0/1)", count.text);
            controller.BindCharacter(immediate, data);
            Assert.IsFalse(count.gameObject.activeSelf);
            Assert.AreEqual(string.Empty, count.text);
            controller.BindCharacter(conditional, data);
            Assert.IsTrue(count.gameObject.activeSelf, "조건이 있는 캐릭터로 재바인드하면 카운트를 다시 보인다.");
            Assert.AreEqual("(0/1)", count.text);
        }

        private CharacterUnlockConditionDefinition CreateCondition(string id, params (string Id, string Group, string Type, int Value)[] entries)
        {
            CharacterUnlockConditionDefinition value = Create<CharacterUnlockConditionDefinition>();
            Set(value, "conditionId", id);
            var serialized = new SerializedObject(value); SerializedProperty list = serialized.FindProperty("entries"); list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("entryId").stringValue = entries[i].Id;
                entry.FindPropertyRelative("groupId").stringValue = entries[i].Group;
                entry.FindPropertyRelative("conditionType").stringValue = entries[i].Type;
                entry.FindPropertyRelative("requiredValue").intValue = entries[i].Value;
                entry.FindPropertyRelative("enabled").boolValue = true;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo(); return value;
        }

        private TMP_Text NewText(Transform parent, string name)
        {
            GameObject value = Track(new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)));
            value.transform.SetParent(parent, false); return value.GetComponent<TMP_Text>();
        }
        private CharacterAcquisitionDefinition CreateAcquisition(string characterId, string conditionId)
        {
            CharacterAcquisitionDefinition value = Create<CharacterAcquisitionDefinition>();
            Set(value, "characterId", characterId); Set(value, "conditionId", conditionId); Set(value, "enabled", true);
            return value;
        }
        private CharacterUnlockInfoController CreateController(CharacterAcquisitionCatalog acquisitions,
            CharacterUnlockConditionCatalog conditions, out RectTransform content, out TMP_Text template, out GameObject complete)
        {
            GameObject host = Track(new GameObject("unlock-info", typeof(RectTransform)));
            CharacterUnlockInfoController controller = host.AddComponent<CharacterUnlockInfoController>();
            TMP_Text title = NewText(host.transform, "title");
            title.gameObject.AddComponent<LocalizedTMPText>();
            TMP_Text count = NewText(host.transform, "count");
            LocalizedTMPText countLocalizer = count.gameObject.AddComponent<LocalizedTMPText>(); countLocalizer.enabled = false;
            content = Track(new GameObject("content", typeof(RectTransform))).GetComponent<RectTransform>(); content.SetParent(host.transform, false);
            template = NewText(content, "template"); template.gameObject.SetActive(false);
            GameObject check = Track(new GameObject("sp_check", typeof(RectTransform))); check.transform.SetParent(template.transform, false);
            GameObject checkOn = Track(new GameObject("sp_checkOn", typeof(RectTransform))); checkOn.transform.SetParent(check.transform, false); checkOn.SetActive(false);
            complete = Track(new GameObject("complete")); complete.transform.SetParent(content, false);
            Set(controller, "acquisitionCatalog", acquisitions); Set(controller, "conditionCatalog", conditions);
            Set(controller, "titleText", title); Set(controller, "countText", count); Set(controller, "countLocalizer", countLocalizer);
            Set(controller, "conditionContent", content); Set(controller, "conditionTemplate", template); Set(controller, "completeRoot", complete);
            return controller;
        }
        private static void AssertRowOrder(RectTransform content, TMP_Text template, GameObject complete, params string[] expectedTexts)
        {
            Assert.AreSame(template.transform, content.GetChild(0));
            for (int i = 0; i < expectedTexts.Length; i++)
            {
                TMP_Text line = content.GetChild(i + 1).GetComponent<TMP_Text>();
                Assert.AreEqual(expectedTexts[i], line.text);
                Assert.AreEqual(i + 1, line.transform.GetSiblingIndex());
            }
            Assert.AreSame(complete.transform, content.GetChild(content.childCount - 1));
            Assert.AreEqual(content.childCount - 1, complete.transform.GetSiblingIndex());
        }
        private static void AssertPrefabRowOrder(RectTransform content, TMP_Text template, GameObject complete, int lineCount)
        {
            Assert.AreSame(template.transform, content.GetChild(0));
            for (int i = 0; i < lineCount; i++)
            {
                Assert.AreNotSame(template.transform, content.GetChild(i + 1));
                Assert.AreEqual(i + 1, content.GetChild(i + 1).GetSiblingIndex());
            }
            Assert.AreSame(complete.transform, content.GetChild(lineCount + 1));
        }
        private GameObject Track(GameObject value) { created.Add(value); return value; }
        private T Create<T>() where T : ScriptableObject { T value = ScriptableObject.CreateInstance<T>(); created.Add(value); return value; }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }
}
