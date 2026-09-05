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
            RectTransform content = new GameObject("content", typeof(RectTransform)).GetComponent<RectTransform>(); Track(content.gameObject); content.SetParent(host.transform, false);
            TMP_Text template = NewText(content, "template"); template.fontStyle = FontStyles.Bold; template.color = Color.green; template.gameObject.SetActive(false);
            GameObject check = Track(new GameObject("sp_check", typeof(RectTransform))); check.transform.SetParent(template.transform, false);
            GameObject checkOn = Track(new GameObject("sp_checkOn", typeof(RectTransform))); checkOn.transform.SetParent(check.transform, false); checkOn.SetActive(false);
            GameObject complete = Track(new GameObject("complete")); complete.transform.SetParent(content, false);
            Set(controller, "acquisitionCatalog", acquisitions); Set(controller, "conditionCatalog", conditions);
            Set(controller, "titleText", title); Set(controller, "conditionContent", content); Set(controller, "conditionTemplate", template); Set(controller, "completeRoot", complete);

            CharacterDefinition character = Create<CharacterDefinition>(); Set(character, "characterId", "Barbarian");
            SaveData data = new SaveData { characters = new List<CharacterSaveState> { new CharacterSaveState { characterId = "CatKnight", level = 1 } } };
            controller.BindCharacter(character, data);
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
        private GameObject Track(GameObject value) { created.Add(value); return value; }
        private T Create<T>() where T : ScriptableObject { T value = ScriptableObject.CreateInstance<T>(); created.Add(value); return value; }
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
