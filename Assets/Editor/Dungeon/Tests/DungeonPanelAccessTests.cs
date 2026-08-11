using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Character;
using Common;
using Dungeon;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace DungeonEditor.Tests
{
    public sealed class DungeonPanelAccessTests
    {
        private const string PrefabPath = "Assets/Art/UI/Prefab/Dungeon/item_dungeonList.prefab";
        private const string PrefabGuid = "3660717675ad041de8f30d0fd0390aeb";

        private static readonly FieldInfo DataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo LoadResultField =
            typeof(SaveSystem).GetField("loadResult", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo ConfigureMethod =
            typeof(SaveSystem).GetMethod("ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo SetAccessServiceMethod =
            typeof(DungeonEntryService).GetMethod("SetAccessServiceForTests",
                BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private readonly List<DungeonDefinition> entryEventLog = new List<DungeonDefinition>();

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(DataField);
            Assert.IsNotNull(LoadResultField);
            Assert.IsNotNull(ConfigureMethod);
            Assert.IsNotNull(SetAccessServiceMethod);

            DungeonEntryService.ResetRequestState();
            SetRosterInstance(null);
        }

        [TearDown]
        public void TearDown()
        {
            DungeonEntryService.DungeonEnterRequested -= RecordEntryEvent;
            CharacterRoster.CharacterStateChanged -= DummyStateHandler;
            DungeonEntryService.ResetRequestState();
            SetRosterInstance(null);

            ClearCharacterStateChangedEvent();

            ConfigureMethod.Invoke(null, new object[] { null, null, null });

            foreach (UnityEngine.Object obj in created)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
            entryEventLog.Clear();
        }

        // ---- 필요 레벨 텍스트 ----

        [Test]
        public void ItemView_RequiredLevelText_ShowsExactFormat()
        {
            DungeonListItemView view = CreateItemView();
            DungeonDefinition dungeon = Dungeon("d1", requiredLevel: 7);

            view.Bind(dungeon, _ => { });

            Assert.AreEqual("Lv. 7", view.CurrentRequirementText);
        }

        [Test]
        public void ItemView_RequiredLevelText_Level1()
        {
            DungeonListItemView view = CreateItemView();
            DungeonDefinition dungeon = Dungeon("d1", requiredLevel: 1);

            view.Bind(dungeon, _ => { });

            Assert.AreEqual("Lv. 1", view.CurrentRequirementText);
        }

        /// <summary>필요 레벨 문구는 사용자의 CurrentCulture와 무관하게 언제나 "Lv. N"이어야 한다.
        /// 자릿수 구분자를 넣는 문화권 설정을 씌워도 문구가 흔들리지 않는지 확인한다 - 예를 들어
        /// 형식이 "N0"으로 바뀌면 여기서 "Lv. 1٬2٬3٬4"가 되어 실패한다.</summary>
        [Test]
        public void ItemView_RequiredLevelText_IsInvariantUnderNonDefaultCulture()
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            try
            {
                var hostile = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                hostile.NumberFormat.NumberGroupSeparator = "٬";
                hostile.NumberFormat.NumberGroupSizes = new[] { 1 };
                hostile.NumberFormat.NegativeSign = "MINUS";
                CultureInfo.CurrentCulture = hostile;

                Assert.AreNotSame(originalCulture, CultureInfo.CurrentCulture,
                    "테스트가 실제로 기본이 아닌 문화권에서 돌아야 한다");

                DungeonListItemView view = CreateItemView();
                view.Bind(Dungeon("d1", requiredLevel: 1234), _ => { });

                Assert.AreEqual("Lv. 1234", view.CurrentRequirementText);
            }
            finally
            {
                // 문화권은 스레드 전역 상태다 - 이 테스트가 실패하든 성공하든 반드시 되돌린다.
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        // ---- 잠김 시각 ----

        [Test]
        public void ItemView_LockedVisual_WhenDenied()
        {
            DungeonListItemView view = CreateItemView();
            view.Bind(Dungeon("d1", requiredLevel: 5), _ => { });

            view.SetAccessResult(DungeonAccessResult.Deny(
                DungeonAccessFailureReason.InsufficientLevel, 5, 2));

            Assert.IsTrue(view.IsLocked);
        }

        [Test]
        public void ItemView_UnlockedVisual_WhenAllowed()
        {
            DungeonListItemView view = CreateItemView();
            view.Bind(Dungeon("d1", requiredLevel: 1), _ => { });

            view.SetAccessResult(DungeonAccessResult.Allow(1, 5));

            Assert.IsFalse(view.IsLocked);
        }

        /// <summary>잠김 표시가 <b>프리팹에 설정된 색</b>을 기준으로만 동작하는지 확인한다. 알파가 1이
        /// 아니고 RGB도 흰색이 아닌 색을 일부러 넣어서, (1) 잠기면 RGB는 그대로이고 알파만 원래 알파의
        /// 0.4배가 되는지, (2) 반복해서 잠가도 값이 누적되지 않는지, (3) 해제/Unbind/재바인딩에서
        /// 원래 색이 <b>정확히</b> 돌아오는지를 본다.</summary>
        [Test]
        public void ItemView_LockedVisual_ScalesAuthoredAlphaAndRestoresExactColors()
        {
            DungeonListItemView view = CreateItemView();
            TextMeshProUGUI nameText = TextFieldOf(view, "nameText");
            TextMeshProUGUI levelText = TextFieldOf(view, "requiredLevelText");

            var authoredName = new Color(0.25f, 0.5f, 0.75f, 0.8f);
            var authoredLevel = new Color(0.9f, 0.1f, 0.35f, 0.5f);
            nameText.color = authoredName;
            levelText.color = authoredLevel;

            DungeonDefinition dungeon = Dungeon("d1", requiredLevel: 5);
            view.Bind(dungeon, _ => { });

            AssertColor(authoredName, nameText.color, "바인딩만으로는 색이 바뀌지 않는다(이름)");
            AssertColor(authoredLevel, levelText.color, "바인딩만으로는 색이 바뀌지 않는다(레벨)");

            // (1) 잠김: RGB 유지, 알파만 원래 알파 x 0.4
            view.SetAccessResult(Denied());
            var lockedName = new Color(0.25f, 0.5f, 0.75f, 0.8f * 0.4f);
            var lockedLevel = new Color(0.9f, 0.1f, 0.35f, 0.5f * 0.4f);
            Assert.IsTrue(view.IsLocked);
            AssertColor(lockedName, nameText.color, "잠김 이름 색");
            AssertColor(lockedLevel, levelText.color, "잠김 레벨 색");

            // (2) 다시 잠가도 이미 낮아진 알파에 또 곱하지 않는다
            view.SetAccessResult(Denied());
            AssertColor(lockedName, nameText.color, "반복 잠금에도 이름 알파가 누적되지 않는다");
            AssertColor(lockedLevel, levelText.color, "반복 잠금에도 레벨 알파가 누적되지 않는다");

            // (3-a) 해제
            view.SetAccessResult(DungeonAccessResult.Allow(5, 9));
            Assert.IsFalse(view.IsLocked);
            AssertColor(authoredName, nameText.color, "해제 후 이름 색이 정확히 복원된다");
            AssertColor(authoredLevel, levelText.color, "해제 후 레벨 색이 정확히 복원된다");

            // (3-b) 잠긴 채로 Unbind
            view.SetAccessResult(Denied());
            view.Unbind();
            Assert.IsFalse(view.IsLocked);
            AssertColor(authoredName, nameText.color, "Unbind 후 이름 색이 정확히 복원된다");
            AssertColor(authoredLevel, levelText.color, "Unbind 후 레벨 색이 정확히 복원된다");

            // (3-c) 잠긴 채로 재바인딩 - 어두워진 값이 새 원본으로 굳으면 안 된다
            view.SetAccessResult(Denied());
            view.Bind(dungeon, _ => { });
            Assert.IsFalse(view.IsLocked, "재바인딩은 판정 전이므로 잠김이 아니다");
            AssertColor(authoredName, nameText.color, "재바인딩 후 이름 색이 정확히 복원된다");
            AssertColor(authoredLevel, levelText.color, "재바인딩 후 레벨 색이 정확히 복원된다");

            view.SetAccessResult(Denied());
            AssertColor(lockedName, nameText.color, "재바인딩 뒤 잠금도 원래 색 기준으로 계산된다");
            AssertColor(lockedLevel, levelText.color, "재바인딩 뒤 잠금도 원래 색 기준으로 계산된다");
        }

        // ---- 잠긴 항목 선택 콜백 ----

        [Test]
        public void ItemView_LockedItem_SelectCallbackStillWorks()
        {
            DungeonListItemView view = CreateItemView();
            DungeonDefinition dungeon = Dungeon("d1", requiredLevel: 10);
            DungeonDefinition selected = null;

            view.Bind(dungeon, d => selected = d);
            view.SetAccessResult(DungeonAccessResult.Deny(
                DungeonAccessFailureReason.InsufficientLevel, 10, 1));

            Assert.IsTrue(view.IsLocked);

            Button btn = view.GetComponent<Button>();
            Assert.IsNotNull(btn);
            Assert.IsTrue(btn.interactable, "잠긴 항목의 Button.interactable은 건드리지 않는다");
            btn.onClick.Invoke();

            Assert.AreSame(dungeon, selected, "잠긴 항목도 선택 콜백이 동작해야 한다");
        }

        // ---- 허용/잠김 선택 → 입장 버튼 ----

        [Test]
        public void Panel_AllowedSelectedDungeon_EnterButtonInteractable()
        {
            Inject(State("hero", level: 10));
            CharacterRoster roster = ReadyRoster("hero");
            SetRosterInstance(roster);

            DungeonPanel panel = CreatePanel(Dungeon("d1", requiredLevel: 5));
            ActivatePanel(panel);

            Assert.IsTrue(panel.IsEnterInteractable);
        }

        [Test]
        public void Panel_LockedSelectedDungeon_EnterButtonDisabled()
        {
            Inject(State("hero", level: 1));
            CharacterRoster roster = ReadyRoster("hero");
            SetRosterInstance(roster);

            DungeonPanel panel = CreatePanel(Dungeon("d1", requiredLevel: 10));
            ActivatePanel(panel);

            Assert.IsFalse(panel.IsEnterInteractable);
        }

        // ---- CharacterStateChanged → 전 항목 갱신 ----

        [Test]
        public void Panel_CharacterStateChanged_RefreshesAllItemsAndButton()
        {
            SaveData doc = Inject(State("hero", level: 1));
            CharacterRoster roster = ReadyRoster("hero");
            SetRosterInstance(roster);

            DungeonPanel panel = CreatePanel(Dungeon("d1", requiredLevel: 5));
            ActivatePanel(panel);

            Assert.IsFalse(panel.IsEnterInteractable, "초기: 레벨 미달");

            doc.characters[0].level = 10;
            RaiseCharacterStateChangedViaReflection(null);

            Assert.IsTrue(panel.IsEnterInteractable, "레벨 변경 후: 입장 가능");
        }

        // ---- 열기/닫기 구독 누수 없음 ----

        [Test]
        public void Panel_OpenClose_DoesNotDuplicateSubscription()
        {
            Inject(State("hero", level: 1));
            CharacterRoster roster = ReadyRoster("hero");
            SetRosterInstance(roster);

            DungeonPanel panel = CreatePanel(Dungeon("d1", requiredLevel: 5));

            LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called"));
            ActivatePanel(panel);
            DeactivatePanel(panel);

            LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called"));
            ActivatePanel(panel);
            DeactivatePanel(panel);

            bool fieldVal = (bool)GetPrivate(panel, "subscribedToStateChanged");
            Assert.IsFalse(fieldVal, "닫힌 뒤 구독 플래그는 false여야 한다");
        }

        // ---- 로스터 없음 → 전원 잠금 ----

        [Test]
        public void Panel_MissingRoster_AllLockedAndButtonDisabled()
        {
            SetRosterInstance(null);

            DungeonPanel panel = CreatePanel(Dungeon("d1", requiredLevel: 1));
            ActivatePanel(panel);

            Assert.IsFalse(panel.IsEnterInteractable);
            Assert.AreEqual(1, panel.SpawnedItemCount);

            // 개수만으로는 "잠갔다"를 증명하지 못한다 - 실제 복제본의 표시 상태를 본다.
            DungeonListItemView item = panel.GetSpawnedItem(0);
            Assert.IsNotNull(item, "목록 항목 복제본을 읽을 수 있어야 한다");
            Assert.IsTrue(item.IsLocked, "로스터가 없으면 fail closed - 항목이 잠긴 표시여야 한다");
            Assert.IsNull(panel.GetSpawnedItem(1), "범위를 벗어난 색인은 null이어야 한다");
            Assert.IsNull(panel.GetSpawnedItem(-1), "음수 색인은 null이어야 한다");
        }

        // ---- 잠긴 선택 → 상세/선택 표시 ----

        [Test]
        public void Panel_LockedSelection_ShowsDetailAndSelection()
        {
            Inject(State("hero", level: 1));
            CharacterRoster roster = ReadyRoster("hero");
            SetRosterInstance(roster);

            DungeonPanel panel = CreatePanel(Dungeon("d1", requiredLevel: 10));
            ActivatePanel(panel);

            Assert.IsNotNull(panel.SelectedDungeon, "잠긴 던전도 선택할 수 있어야 한다");
            Assert.AreEqual("d1", panel.SelectedDungeon.DungeonId);
        }

        // ---- 재열기/현재 데이터 재평가 ----

        [Test]
        public void Panel_RefocusReevaluatesAccess()
        {
            SaveData doc = Inject(State("hero", level: 1));
            CharacterRoster roster = ReadyRoster("hero");
            SetRosterInstance(roster);

            DungeonPanel panel = CreatePanel(Dungeon("d1", requiredLevel: 5));
            ActivatePanel(panel);
            Assert.IsFalse(panel.IsEnterInteractable);

            doc.characters[0].level = 10;
            LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called"));
            panel.Open();

            Assert.IsTrue(panel.IsEnterInteractable);
        }

        // ---- 필요 레벨 텍스트: 열림 상태에서도 항상 표시 ----

        [Test]
        public void ItemView_RequiredLevelAlwaysVisible_IncludingUnlocked()
        {
            DungeonListItemView view = CreateItemView();
            DungeonDefinition dungeon = Dungeon("d1", requiredLevel: 3);

            view.Bind(dungeon, _ => { });
            view.SetAccessResult(DungeonAccessResult.Allow(3, 10));

            Assert.AreEqual("Lv. 3", view.CurrentRequirementText);
            Assert.IsFalse(view.IsLocked);
        }

        // ---- DungeonEntryService가 최종 거부자 → 강제 interactable 우회 불가 ----

        /// <summary>입장 버튼의 interactable은 표시일 뿐이고 최종 거부자는
        /// <see cref="DungeonEntryService"/>임을 증명한다. 버튼이 낡은 채로 켜져 있는 상황을 그대로
        /// 재현한다 - <b>실제 입장 버튼을 강제로 켜고 그 버튼의 클릭을 발생시킨</b> 뒤, 요청 통로에
        /// 아무 흔적도 남지 않고 패널 상태도 되돌려졌는지 확인한다.</summary>
        [Test]
        public void Panel_StaleInteractable_CannotBypassDungeonEntryService()
        {
            Inject(State("hero", level: 1));
            CharacterRoster roster = ReadyRoster("hero");
            SetRosterInstance(roster);

            DungeonEntryService.DungeonEnterRequested += RecordEntryEvent;
            SetAccessOverride(new DungeonAccessService(new StubLevelSource(1)));

            DungeonDefinition dungeon = Dungeon("d1", requiredLevel: 10);
            DungeonPanel panel = CreatePanel(dungeon);
            ActivatePanel(panel);

            Assert.IsFalse(panel.IsEnterInteractable, "레벨 미달이면 입장 버튼은 꺼져 있어야 한다");

            Button enterButton = EnterButtonOf(panel);
            Assert.IsFalse(enterButton.interactable);

            // 낡은 상태 재현: 버튼을 강제로 켜고 실제로 누른다.
            enterButton.interactable = true;
            enterButton.onClick.Invoke();

            Assert.AreEqual(0, DungeonEntryService.AcceptedRequestCount, "요청이 받아들여지면 안 된다");
            Assert.AreEqual(0, entryEventLog.Count, "입장 이벤트가 발행되면 안 된다");
            Assert.IsNull(DungeonEntryService.LastRequestedDungeon,
                "거부된 요청은 LastRequestedDungeon을 남기지 않는다");
            Assert.AreEqual(string.Empty, DungeonEntryService.LastRequestedDungeonId,
                "거부된 요청은 LastRequestedDungeonId를 남기지 않는다");

            Assert.IsFalse(panel.IsEnterRequestSent, "거부되면 요청 상태를 되돌려야 한다");
            Assert.IsTrue(panel.gameObject.activeSelf, "거부는 패널을 닫지 않는다");
            Assert.AreSame(dungeon, panel.SelectedDungeon, "선택은 그대로 남아야 한다");
            Assert.IsFalse(enterButton.interactable, "버튼은 다시 잠겨야 한다");
        }

        // ---- 프로덕션 프리팹 검증 ----

        [Test]
        public void ProductionPrefab_HasConnectedRequirementTMP()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "프리팹이 존재해야 한다");

            var view = prefab.GetComponent<DungeonListItemView>();
            Assert.IsNotNull(view, "DungeonListItemView 컴포넌트가 있어야 한다");

            var so = new SerializedObject(view);
            var prop = so.FindProperty("requiredLevelText");
            Assert.IsNotNull(prop, "requiredLevelText 필드가 있어야 한다");
            Assert.IsNotNull(prop.objectReferenceValue, "requiredLevelText가 연결되어 있어야 한다");

            TextMeshProUGUI tmp = prop.objectReferenceValue as TextMeshProUGUI;
            Assert.IsNotNull(tmp, "연결된 오브젝트가 TextMeshProUGUI여야 한다");
        }

        [Test]
        public void ProductionPrefab_RootSizeRemains164x40()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);

            RectTransform root = prefab.GetComponent<RectTransform>();
            Assert.IsNotNull(root);
            Assert.AreEqual(164f, root.sizeDelta.x, 0.01f);
            Assert.AreEqual(40f, root.sizeDelta.y, 0.01f);
        }

        [Test]
        public void ProductionPrefab_SameGuidAndMeta()
        {
            string guid = AssetDatabase.AssetPathToGUID(PrefabPath);
            Assert.AreEqual(PrefabGuid, guid);
        }

        [Test]
        public void ProductionPrefab_NoNewArt()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab);

            Image[] images = prefab.GetComponentsInChildren<Image>(true);
            Assert.AreEqual(1, images.Length, "루트 Image 하나만 있어야 한다 (새 아트 없음)");
        }

        // ---- 도우미 ----

        private void RecordEntryEvent(DungeonDefinition d) => entryEventLog.Add(d);
        private static void DummyStateHandler(CharacterDefinition _) { }

        private static DungeonAccessResult Denied()
        {
            return DungeonAccessResult.Deny(DungeonAccessFailureReason.InsufficientLevel, 5, 2);
        }

        /// <summary>색은 정확히 되돌아와야 하므로 float 비교 오차만 허용한다.</summary>
        private static void AssertColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-5f, $"{message} (r)");
            Assert.AreEqual(expected.g, actual.g, 1e-5f, $"{message} (g)");
            Assert.AreEqual(expected.b, actual.b, 1e-5f, $"{message} (b)");
            Assert.AreEqual(expected.a, actual.a, 1e-5f, $"{message} (a)");
        }

        private static TextMeshProUGUI TextFieldOf(DungeonListItemView view, string field)
        {
            var so = new SerializedObject(view);
            SerializedProperty prop = so.FindProperty(field);
            Assert.IsNotNull(prop, $"DungeonListItemView.{field} 필드가 있어야 한다");

            var text = prop.objectReferenceValue as TextMeshProUGUI;
            Assert.IsNotNull(text, $"DungeonListItemView.{field}가 연결되어 있어야 한다");
            return text;
        }

        private static Button EnterButtonOf(DungeonPanel panel)
        {
            var so = new SerializedObject(panel);
            SerializedProperty prop = so.FindProperty("enterButton");
            Assert.IsNotNull(prop, "DungeonPanel.enterButton 필드가 있어야 한다");

            var button = prop.objectReferenceValue as Button;
            Assert.IsNotNull(button, "입장 버튼이 연결되어 있어야 한다");
            return button;
        }

        private static void RaiseCharacterStateChangedViaReflection(CharacterDefinition def)
        {
            FieldInfo fi = typeof(CharacterRoster).GetField("CharacterStateChanged",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(fi, "CharacterStateChanged 이벤트 backing field를 찾을 수 없다");
            var handler = fi.GetValue(null) as Action<CharacterDefinition>;
            handler?.Invoke(def);
        }

        private static void ClearCharacterStateChangedEvent()
        {
            FieldInfo fi = typeof(CharacterRoster).GetField("CharacterStateChanged",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (fi != null) fi.SetValue(null, null);
        }

        private static void SetAccessOverride(DungeonAccessService service)
        {
            SetAccessServiceMethod.Invoke(null, new object[] { service });
        }

        private DungeonListItemView CreateItemView()
        {
            var go = new GameObject("TestItem", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            created.Add(go);
            go.SetActive(false);

            var nameGo = new GameObject("NameText", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            nameGo.transform.SetParent(go.transform, false);

            var levelGo = new GameObject("LevelText", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            levelGo.transform.SetParent(go.transform, false);

            var view = go.AddComponent<DungeonListItemView>();
            var viewSo = new SerializedObject(view);
            viewSo.FindProperty("selectButton").objectReferenceValue = go.GetComponent<Button>();
            viewSo.FindProperty("nameText").objectReferenceValue = nameGo.GetComponent<TextMeshProUGUI>();
            viewSo.FindProperty("requiredLevelText").objectReferenceValue = levelGo.GetComponent<TextMeshProUGUI>();
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            go.GetComponent<Button>().targetGraphic = go.GetComponent<Image>();
            go.SetActive(true);

            return view;
        }

        private DungeonPanel CreatePanel(params DungeonDefinition[] dungeons)
        {
            var panelGo = new GameObject("TestPanel", typeof(RectTransform));
            created.Add(panelGo);
            panelGo.SetActive(false);

            DungeonListItemView template = CreateItemView();
            template.gameObject.SetActive(false);
            template.transform.SetParent(panelGo.transform, false);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(panelGo.transform, false);

            var enterGo = new GameObject("EnterButton", typeof(RectTransform), typeof(Button));
            enterGo.transform.SetParent(panelGo.transform, false);

            DungeonCatalog catalog = CreateCatalog(dungeons);

            var panel = panelGo.AddComponent<DungeonPanel>();
            var panelSo = new SerializedObject(panel);
            panelSo.FindProperty("catalog").objectReferenceValue = catalog;
            panelSo.FindProperty("dungeonListItemTemplate").objectReferenceValue = template;
            panelSo.FindProperty("dungeonListContent").objectReferenceValue = contentGo.GetComponent<RectTransform>();
            panelSo.FindProperty("enterButton").objectReferenceValue = enterGo.GetComponent<Button>();
            panelSo.ApplyModifiedPropertiesWithoutUndo();

            return panel;
        }

        private DungeonCatalog CreateCatalog(params DungeonDefinition[] dungeons)
        {
            var catalog = ScriptableObject.CreateInstance<DungeonCatalog>();
            created.Add(catalog);

            var so = new SerializedObject(catalog);
            SerializedProperty list = so.FindProperty("dungeons");
            list.arraySize = dungeons.Length;
            for (int i = 0; i < dungeons.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = dungeons[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }

        private DungeonDefinition Dungeon(string id, int requiredLevel = 1)
        {
            var def = ScriptableObject.CreateInstance<DungeonDefinition>();
            created.Add(def);

            var so = new SerializedObject(def);
            so.FindProperty("dungeonId").stringValue = id;
            so.FindProperty("requiredCharacterLevel").intValue = requiredLevel;
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        private static SaveData Inject(params CharacterSaveState[] states)
        {
            var doc = new SaveData { characters = new List<CharacterSaveState>(states) };
            DataField.SetValue(null, doc);
            LoadResultField.SetValue(null, SaveLoadResult.NewGame(doc));
            return doc;
        }

        private static CharacterSaveState State(string id, int level = 1, int stamina = 10)
        {
            return new CharacterSaveState { characterId = id, level = level, currentStamina = stamina };
        }

        private CharacterRoster ReadyRoster(params string[] catalogIds)
        {
            var host = new GameObject("AccessTestHost");
            created.Add(host);
            host.SetActive(false);

            CharacterRoster roster = host.AddComponent<CharacterRoster>();
            SetPrivate(roster, "catalog", CharCatalog(catalogIds));
            SetPrivate(roster, "owned", new OwnedCharacterCollection(
                (CharacterCatalog)GetPrivate(roster, "catalog"), SaveSystem.Data));

            InvokeMethod(roster, "BuildUsableEntries");
            return roster;
        }

        private CharacterCatalog CharCatalog(params string[] ids)
        {
            var definitions = new CharacterDefinition[ids.Length];
            for (int i = 0; i < ids.Length; i++) definitions[i] = CharDef(ids[i]);

            var catalog = ScriptableObject.CreateInstance<CharacterCatalog>();
            created.Add(catalog);

            var so = new SerializedObject(catalog);
            SerializedProperty list = so.FindProperty("characters");
            list.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }

        private CharacterDefinition CharDef(string id)
        {
            var def = ScriptableObject.CreateInstance<CharacterDefinition>();
            created.Add(def);

            var so = new SerializedObject(def);
            so.FindProperty("characterId").stringValue = id;
            so.FindProperty("initiallyOwned").boolValue = true;
            so.FindProperty("maxStamina").intValue = 30;
            so.FindProperty("motionProfile").objectReferenceValue = CharProfile();
            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        private CharacterMotionProfile CharProfile()
        {
            var profile = ScriptableObject.CreateInstance<CharacterMotionProfile>();
            created.Add(profile);

            var tex = new Texture2D(4, 4);
            created.Add(tex);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            created.Add(sprite);

            var so = new SerializedObject(profile);
            SerializedProperty frames = so.FindProperty("baseIdle").FindPropertyRelative("frames");
            frames.arraySize = 1;
            frames.GetArrayElementAtIndex(0).objectReferenceValue = sprite;
            so.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        private static void SetRosterInstance(CharacterRoster roster)
        {
            PropertyInfo prop = typeof(CharacterRoster).GetProperty(
                "Instance", BindingFlags.Public | BindingFlags.Static);
            MethodInfo setter = prop.GetSetMethod(nonPublic: true);
            Assert.IsNotNull(setter);
            setter.Invoke(null, new object[] { roster });
        }

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{field} not found");
            info.SetValue(target, value);
        }

        private static object GetPrivate(object target, string field)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info);
            return info.GetValue(target);
        }

        private static object InvokeMethod(object target, string method)
        {
            Type type = target.GetType();
            MethodInfo mi = null;
            while (type != null && mi == null)
            {
                mi = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance
                                            | BindingFlags.DeclaredOnly);
                type = type.BaseType;
            }
            Assert.IsNotNull(mi, $"{target.GetType().Name}.{method} not found");
            return mi.Invoke(target, null);
        }

        private static void ActivatePanel(DungeonPanel panel)
        {
            panel.gameObject.SetActive(true);
            InvokeMethod(panel, "OnEnable");
        }

        private static void DeactivatePanel(DungeonPanel panel)
        {
            InvokeMethod(panel, "OnDisable");
            panel.gameObject.SetActive(false);
        }

        private sealed class StubLevelSource : IOwnedCharacterLevelSource
        {
            private readonly int level;
            public StubLevelSource(int level) { this.level = level; }
            public int HighestOwnedCharacterLevel => level;
        }
    }
}
