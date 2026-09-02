using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using CharacterArchive;
using Common;
using NUnit.Framework;
using Skill;
using SkillEditor.Tests;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

namespace CharacterArchiveEditorTests
{
    public sealed class CharacterInfoControllerTests : SkillUnlockTestBase
    {
        private const string UiTableGuid = "32fd067a20b754a50b20446b9c78d2ae";

        [Test]
        public void SkillRows_CountValidRelations_FilterLocks_AndUseRuntimeOrder()
        {
            CharacterDefinition character = CharacterAsset("CatKnight");
            CharacterCatalog characters = CharacterList(character);
            SkillDefinition zeta = SkillAsset("zeta");
            SkillDefinition omega = SkillAsset("omega");
            SkillDefinition alpha = SkillAsset("alpha");
            SkillCatalog skills = SkillList(zeta, omega, alpha);
            CharacterSkillDefinition later = OrderedRelation(character, zeta, requiredLevel: 3, displayOrder: 20);
            CharacterSkillDefinition sameLaterId = OrderedRelation(character, omega, requiredLevel: 2, displayOrder: 10);
            CharacterSkillDefinition sameEarlierId = OrderedRelation(character, alpha, requiredLevel: 1, displayOrder: 10);
            CharacterSkillDefinition broken = Relation("CatKnight", "ghost", characterRef: character, linkSkill: false);
            CharacterSkillCatalog relations = RelationList(later, sameLaterId, broken, sameEarlierId);

            IReadOnlyList<CharacterInfoController.SkillRow> rows = CharacterInfoController.BuildSkillRows(
                character, characters, skills, relations, Document(State("CatKnight", level: 2)));

            Assert.AreEqual(3, rows.Count, "깨진 관계는 분모에 포함하지 않습니다.");
            CollectionAssert.AreEqual(new[] { "alpha", "omega", "zeta" }, SkillIds(rows));
            CollectionAssert.AreEqual(new[] { true, true, false }, Unlocks(rows));
        }

        [Test]
        public void UnownedCharacter_KeepsRegisteredDenominator_ButUnlocksNothing()
        {
            CharacterDefinition character = CharacterAsset("CatKnight");
            CharacterCatalog characters = CharacterList(character);
            SkillDefinition skill = SkillAsset("slash");
            CharacterSkillCatalog relations = RelationList(OrderedRelation(character, skill, 1, 0));

            IReadOnlyList<CharacterInfoController.SkillRow> rows = CharacterInfoController.BuildSkillRows(
                character, characters, SkillList(skill), relations, Document());

            Assert.AreEqual(1, rows.Count);
            Assert.IsFalse(rows[0].Unlocked);
        }

        [Test]
        public void ResolveLevel_UsesFirstExactState_ClampsLow_AndFallsBackToOne()
        {
            Assert.AreEqual(1, CharacterInfoController.ResolveLevel(null, "CatKnight"));
            Assert.AreEqual(1, CharacterInfoController.ResolveLevel(Document(), "CatKnight"));
            Assert.AreEqual(1, CharacterInfoController.ResolveLevel(
                Document(State("CatKnight", 0), State("CatKnight", 99)), "CatKnight"));
            Assert.AreEqual(int.MaxValue, CharacterInfoController.ResolveLevel(
                Document(State("CatKnight", int.MaxValue)), "CatKnight"));
        }

        [TestCase(10f, "10s")]
        [TestCase(1.25f, "1.25s")]
        [TestCase(0.1259f, "0.126s")]
        public void CooldownFormat_PreservesUsefulFractions(float value, string expected)
        {
            Assert.AreEqual(expected, SkillListItemView.FormatCooldown(value));
        }

        [Test]
        public void SkillItem_NullIconRestoresPlaceholder_AndLocaleCallbacksRefreshBothTexts()
        {
            Sprite placeholder = NewSprite();
            Sprite previous = NewSprite();
            SkillDefinition definition = SkillAsset("localized_skill");
            Set(definition, "cooldownSeconds", 2.5f);
            SetReference(definition, "localizedName", Reference(95));
            SetReference(definition, "localizedDescription", Reference(96));

            GameObject host = Track(new GameObject("skill-row"));
            SkillListItemView view = host.AddComponent<SkillListItemView>();
            Image icon = NewImage(host.transform, "icon");
            icon.sprite = previous;
            TextMeshProUGUI name = NewText(host.transform, "name");
            TextMeshProUGUI description = NewText(host.transform, "description");
            TextMeshProUGUI cooldown = NewText(host.transform, "cooldown");
            SetView(view, icon, name, description, cooldown, placeholder);

            view.Bind(definition);

            Assert.AreSame(placeholder, icon.sprite, "null 아이콘은 이전 행 아이콘 대신 프리팹 임시 아이콘을 유지해야 합니다.");
            Assert.AreNotEqual("95", name.text, "로컬라이즈 숫자 키를 사용자에게 그대로 노출하지 않습니다.");
            Assert.AreNotEqual("96", description.text, "설명도 로컬라이즈 숫자 키를 그대로 노출하지 않습니다.");
            Assert.AreEqual("2.5s", cooldown.text);
            Assert.IsTrue(view.HasNameSubscription);
            Assert.IsTrue(view.HasDescriptionSubscription);

            Deliver(view, "ApplyName", "갱신 이름");
            Deliver(view, "ApplyDescription", "갱신 설명");
            Assert.AreEqual("갱신 이름", name.text);
            Assert.AreEqual("갱신 설명", description.text);

            view.Unbind();
            Assert.IsFalse(view.HasNameSubscription);
            Assert.IsFalse(view.HasDescriptionSubscription);
        }

        [Test]
        public void Preview_AdvancesLoops_ResetsOnCharacterChange_AndHidesBrokenData()
        {
            Sprite first = NewSprite();
            Sprite second = NewSprite();
            Sprite replacement = NewSprite();
            CharacterDefinition one = CharacterWithFrames("one", 2f, first, second);
            CharacterDefinition two = CharacterWithFrames("two", 4f, replacement);
            CharacterDefinition broken = CharacterWithFrames("broken", 2f, first, null);

            GameObject host = Track(new GameObject("preview-controller"));
            host.SetActive(false);
            CharacterInfoController controller = host.AddComponent<CharacterInfoController>();
            Image image = NewImage(host.transform, "model");
            Set(controller, "characterModelImage", image);
            host.SetActive(true);
            controller.BindCharacter(one, Document(State("one")));

            Assert.AreSame(first, image.sprite);
            Assert.AreEqual(0, controller.PreviewFrameIndex);
            controller.AdvancePreview(.5f);
            Assert.AreSame(second, image.sprite);
            host.SetActive(false);
            controller.AdvancePreview(.5f);
            Assert.AreSame(second, image.sprite, "비활성 페이지에서는 프리뷰를 진행하지 않습니다.");
            host.SetActive(true);
            controller.BindCharacter(one, Document(State("one")));
            controller.AdvancePreview(.5f);
            Assert.AreSame(first, image.sprite, "마지막 다음에는 첫 프레임으로 루프합니다.");

            controller.BindCharacter(two, Document(State("two")));
            Assert.AreSame(replacement, image.sprite);
            Assert.AreEqual(0, controller.PreviewFrameIndex);

            controller.BindCharacter(broken, Document(State("broken")));
            Assert.IsFalse(image.enabled);
            Assert.IsNull(image.sprite);
            Assert.IsFalse(controller.IsPreviewPlaying);
        }

        [Test]
        public void Rebinding_ReusesRows_AndZeroUnlocksShowsOnlyEmptyState()
        {
            CharacterDefinition character = CharacterAsset("CatKnight");
            CharacterCatalog characters = CharacterList(character);
            SkillDefinition first = SkillAsset("first");
            SkillDefinition second = SkillAsset("second");
            CharacterSkillCatalog relations = RelationList(
                OrderedRelation(character, first, 2, 10), OrderedRelation(character, second, 3, 20));
            ControllerFixture fixture = NewControllerFixture(characters, SkillList(first, second), relations);

            fixture.Root.SetActive(true);
            fixture.Controller.BindCharacter(character, Document(State("CatKnight", 3)));
            Assert.AreEqual(2, fixture.Controller.ActiveItemCount);
            Assert.AreEqual(2, fixture.Controller.PooledItemCount);
            Assert.IsFalse(fixture.Empty.activeSelf);
            Assert.AreEqual("Skills (2/2)", fixture.Title.text);

            fixture.Controller.BindCharacter(character, Document(State("CatKnight", 3)));
            Assert.AreEqual(2, fixture.Controller.ActiveItemCount);
            Assert.AreEqual(2, fixture.Controller.PooledItemCount, "같은 내용을 다시 그려도 클론을 누적하지 않습니다.");

            fixture.Controller.BindCharacter(character, Document(State("CatKnight", 1)));
            Assert.AreEqual(0, fixture.Controller.ActiveItemCount);
            Assert.AreEqual(2, fixture.Controller.PooledItemCount);
            Assert.IsTrue(fixture.Empty.activeSelf);
            Assert.AreEqual("Skills (0/2)", fixture.Title.text);
            Assert.IsFalse(fixture.Template.gameObject.activeSelf, "샘플은 런타임 한 건으로 집계하지 않습니다.");
        }

        [Test]
        public void DisableAndReenable_RestoresOneLocalizationContractWithoutStaleRows()
        {
            CharacterDefinition character = CharacterAsset("CatKnight");
            SetReference(character, "localizedName", Reference(95));
            SetOriginWorld(character, Reference(96));
            CharacterCatalog characters = CharacterList(character);
            SkillDefinition skill = SkillAsset("localized_skill");
            SetReference(skill, "localizedName", Reference(95));
            SetReference(skill, "localizedDescription", Reference(96));
            ControllerFixture fixture = NewControllerFixture(characters, SkillList(skill),
                RelationList(OrderedRelation(character, skill, 1, 0)));
            fixture.Root.SetActive(true);
            fixture.Controller.BindCharacter(character, Document(State("CatKnight", 1)));

            Assert.IsTrue(fixture.Controller.HasOriginWorldSubscription);
            Assert.IsTrue(fixture.Controller.HasTitleSubscription);
            SkillListItemView runtime = RuntimeRow(fixture.Controller);
            Assert.IsTrue(runtime.HasNameSubscription);
            Assert.IsTrue(runtime.HasDescriptionSubscription);

            Deliver(fixture.Controller, "ApplyOriginWorld", "갱신 소속");
            Deliver(fixture.Controller, "ApplySkillTitleFormat", "스킬 정보 ({0}/{1})");
            Assert.AreEqual("갱신 소속", fixture.Origin.text);
            Assert.AreEqual("스킬 정보 (1/1)", fixture.Title.text);

            InvokeLifecycle(fixture.Controller, "OnDisable");
            Assert.IsFalse(fixture.Controller.HasOriginWorldSubscription);
            Assert.IsFalse(fixture.Controller.HasTitleSubscription);
            Assert.IsFalse(runtime.HasNameSubscription);
            Assert.IsFalse(runtime.HasDescriptionSubscription);

            InvokeLifecycle(fixture.Controller, "OnEnable");
            Assert.IsTrue(fixture.Controller.HasOriginWorldSubscription);
            Assert.IsTrue(fixture.Controller.HasTitleSubscription);
            Assert.AreEqual(1, fixture.Controller.ActiveItemCount);
            Assert.AreEqual(1, fixture.Controller.PooledItemCount);
        }

        private ControllerFixture NewControllerFixture(
            CharacterCatalog characters, SkillCatalog skills, CharacterSkillCatalog relations)
        {
            GameObject root = Track(new GameObject("character-info"));
            root.SetActive(false);
            CharacterInfoController controller = root.AddComponent<CharacterInfoController>();
            Image model = NewImage(root.transform, "model");
            TextMeshProUGUI characterName = NewText(root.transform, "character-name");
            TextMeshProUGUI level = NewText(root.transform, "level");
            TextMeshProUGUI origin = NewText(root.transform, "origin");
            TextMeshProUGUI title = NewText(root.transform, "title");
            LocalizedTMPText titleLocalizer = title.gameObject.AddComponent<LocalizedTMPText>();
            Set(titleLocalizer, "target", title);
            SetReference(titleLocalizer, "text", Reference(95));
            titleLocalizer.enabled = false;
            GameObject empty = new GameObject("empty");
            empty.transform.SetParent(root.transform, false);
            RectTransform content = (RectTransform)new GameObject("content", typeof(RectTransform)).transform;
            content.SetParent(root.transform, false);
            GameObject templateObject = new GameObject("template");
            templateObject.transform.SetParent(content, false);
            templateObject.SetActive(false);
            SkillListItemView template = templateObject.AddComponent<SkillListItemView>();
            SetView(template,
                NewImage(templateObject.transform, "icon"),
                NewText(templateObject.transform, "name"),
                NewText(templateObject.transform, "description"),
                NewText(templateObject.transform, "cooldown"), null);

            Set(controller, "characterCatalog", characters);
            Set(controller, "skillCatalog", skills);
            Set(controller, "characterSkillCatalog", relations);
            Set(controller, "characterModelImage", model);
            Set(controller, "characterNameText", characterName);
            Set(controller, "levelText", level);
            Set(controller, "originWorldText", origin);
            Set(controller, "skillTitleText", title);
            Set(controller, "skillTitleLocalizer", titleLocalizer);
            Set(controller, "emptyState", empty);
            Set(controller, "skillContent", content);
            Set(controller, "skillTemplate", template);
            return new ControllerFixture(root, controller, empty, template, origin, title);
        }

        private CharacterCatalog CharacterList(params CharacterDefinition[] definitions)
        {
            CharacterCatalog catalog = Track(ScriptableObject.CreateInstance<CharacterCatalog>());
            SetList(catalog, "characters", definitions);
            catalog.MarkDirty();
            return catalog;
        }

        private CharacterSkillDefinition OrderedRelation(
            CharacterDefinition character, SkillDefinition skill, int requiredLevel, int displayOrder)
        {
            CharacterSkillDefinition relation = Relation(character.CharacterId, skill.SkillId, requiredLevel, character, skill);
            Set(relation, "displayOrder", displayOrder);
            return relation;
        }

        private CharacterDefinition CharacterWithFrames(string id, float fps, params Sprite[] frames)
        {
            CharacterDefinition definition = CharacterAsset(id);
            CharacterMotionProfile profile = Track(ScriptableObject.CreateInstance<CharacterMotionProfile>());
            SerializedObject profileSerialized = new SerializedObject(profile);
            SerializedProperty idle = profileSerialized.FindProperty("baseIdle");
            idle.FindPropertyRelative("animationFps").floatValue = fps;
            SerializedProperty array = idle.FindPropertyRelative("frames");
            array.arraySize = frames.Length;
            for (int i = 0; i < frames.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
            profileSerialized.ApplyModifiedPropertiesWithoutUndo();
            Set(definition, "motionProfile", profile);
            return definition;
        }

        private void SetOriginWorld(CharacterDefinition character, LocalizedTextReference name)
        {
            Dungeon.WorldDefinition world = Track(ScriptableObject.CreateInstance<Dungeon.WorldDefinition>());
            SetReference(world, "localizedName", name);
            Set(character, "originWorld", world);
        }

        private Sprite NewSprite()
        {
            Texture2D texture = Track(new Texture2D(2, 2));
            return Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f)));
        }

        private static Image NewImage(Transform parent, string name)
        {
            GameObject child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            child.transform.SetParent(parent, false);
            return child.GetComponent<Image>();
        }

        private static TextMeshProUGUI NewText(Transform parent, string name)
        {
            GameObject child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            child.transform.SetParent(parent, false);
            return child.GetComponent<TextMeshProUGUI>();
        }

        private static void SetView(SkillListItemView view, Image icon, TMP_Text name, TMP_Text description,
            TMP_Text cooldown, Sprite placeholder)
        {
            Set(view, "iconImage", icon);
            Set(view, "nameText", name);
            Set(view, "descriptionText", description);
            Set(view, "cooldownText", cooldown);
            Set(view, "placeholderIcon", placeholder);
        }

        private static LocalizedTextReference Reference(int key) =>
            new LocalizedTextReference((TableReference)new Guid(UiTableGuid), key.ToString());

        private static void SetReference(object target, string field, LocalizedTextReference value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, target.GetType().Name + "." + field);
            info.SetValue(target, value);
        }

        private static void SetList(ScriptableObject target, string field, UnityEngine.Object[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(UnityEngine.Object target, string field, float value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(field).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(UnityEngine.Object target, string field, int value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Deliver(object target, string method, string value)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, method);
            info.Invoke(target, new object[] { value });
        }

        private static void InvokeLifecycle(object target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, method);
            info.Invoke(target, null);
        }

        private static string[] SkillIds(IReadOnlyList<CharacterInfoController.SkillRow> rows)
        {
            string[] values = new string[rows.Count];
            for (int i = 0; i < rows.Count; i++) values[i] = rows[i].Skill.SkillId;
            return values;
        }

        private static bool[] Unlocks(IReadOnlyList<CharacterInfoController.SkillRow> rows)
        {
            bool[] values = new bool[rows.Count];
            for (int i = 0; i < rows.Count; i++) values[i] = rows[i].Unlocked;
            return values;
        }

        private static SkillListItemView RuntimeRow(CharacterInfoController controller)
        {
            var pool = (List<SkillListItemView>)typeof(CharacterInfoController)
                .GetField("itemPool", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
            return pool[0];
        }

        private readonly struct ControllerFixture
        {
            public ControllerFixture(GameObject root, CharacterInfoController controller, GameObject empty,
                SkillListItemView template, TMP_Text origin, TMP_Text title)
            {
                Root = root;
                Controller = controller;
                Empty = empty;
                Template = template;
                Origin = origin;
                Title = title;
            }

            public GameObject Root { get; }
            public CharacterInfoController Controller { get; }
            public GameObject Empty { get; }
            public SkillListItemView Template { get; }
            public TMP_Text Origin { get; }
            public TMP_Text Title { get; }
        }
    }
}
