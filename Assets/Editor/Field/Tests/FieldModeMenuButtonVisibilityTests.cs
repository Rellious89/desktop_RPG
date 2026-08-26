using System.Collections.Generic;
using System.Reflection;
using Common;
using Dungeon;
using Field;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FieldEditor.Tests
{
    /// <summary>
    /// 하단 메뉴 버튼의 <b>필드별 표시 규칙</b> 시험(<see cref="FieldModeMenuButtonVisibilityController"/>).
    ///
    /// <b>씬을 열지 않는다.</b> 실제 desktopScene_ReSize를 Play Mode로 켜지 않고, 씬과 같은 구성
    /// (MainMenu 아래 btnArea, 그 아래 버튼 루트 7개)을 시험이 직접 세운다 - 확인하려는 것은 컴포넌트의
    /// 규칙이지 씬의 배치가 아니고, 배치는 수동 확인이 맡는다.
    ///
    /// <b>전환은 매니저를 통해 일으킨다.</b> 컨트롤러의 비공개 핸들러를 부르지 않고
    /// <see cref="FieldModeManager.TryEnterDungeon"/>/<see cref="FieldModeManager.TryReturnToTown"/>을
    /// 부르므로 구독 경로까지 함께 확인된다. 다만 매니저는 <b>한 프레임에 한 번</b>만 전환하는데
    /// EditMode에서는 <see cref="Time.frameCount"/>가 늘지 않으므로, 전환 직전에 그 프레임 잠금만
    /// 비공개 필드로 풀어준다 - 규칙 자체를 우회하는 것이 아니라 "다음 프레임"을 흉내 내는 것이다.
    /// </summary>
    public sealed class FieldModeMenuButtonVisibilityTests
    {
        private static readonly FieldInfo ManagerFrameField = typeof(FieldModeManager).GetField(
            "lastTransitionFrame", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo ControllerManagerField =
            typeof(FieldModeMenuButtonVisibilityController).GetField(
                "fieldModeManager", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo ControllerButtonsField =
            typeof(FieldModeMenuButtonVisibilityController).GetField(
                "buttons", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>씬의 등록 순서 그대로. 이 순서가 곧 btnArea 아래 오브젝트 순서다.</summary>
        private const string Switching = "btn_switching";
        private const string Inventory = "btn_inventory";
        private const string Recovery = "btn_Recovery";
        private const string DungeonEntry = "btn_Dungeon";
        private const string ReturnTown = "btn_ReturnTown";
        private const string Archive = "btn_CharacterArchive";
        private const string Purification = "btn_Purification";
        private const string ExitGame = "btn_exitGame";

        private readonly List<Object> created = new List<Object>();

        private GameObject mainMenu;
        private GameObject btnArea;
        private FieldModeManager manager;
        private FieldModeMenuButtonVisibilityController controller;
        private DungeonDefinition dungeon;

        private readonly Dictionary<string, GameObject> buttonRoots = new Dictionary<string, GameObject>();

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(ManagerFrameField, "lastTransitionFrame");
            Assert.IsNotNull(ControllerManagerField, "fieldModeManager");
            Assert.IsNotNull(ControllerButtonsField, "buttons");

            buttonRoots.Clear();

            var managerObject = NewObject("FieldSystem");
            manager = managerObject.AddComponent<FieldModeManager>();

            dungeon = ScriptableObject.CreateInstance<DungeonDefinition>();
            created.Add(dungeon);
            typeof(DungeonDefinition)
                .GetField("dungeonId", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(dungeon, "TEST_DUNGEON");

            // MainMenu(항상 활성) 아래에 btnArea(접힘 상태에서는 비활성)를 두는 실제 구성 그대로다.
            mainMenu = NewObject("MainMenu");
            btnArea = NewObject("btnArea");
            btnArea.transform.SetParent(mainMenu.transform, false);

            controller = mainMenu.AddComponent<FieldModeMenuButtonVisibilityController>();
            ControllerManagerField.SetValue(controller, manager);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
            created.Clear();
        }

        // ---- 도우미 ----

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        /// <summary>버튼 하나를 씬과 같은 모양으로 만든다 - 바깥 루트(WindowInputRegion 자리)가 안쪽
        /// Button을 감싼다. 컨트롤러가 켜고 끄는 것은 언제나 바깥 루트다.</summary>
        private Button AddButton(string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(btnArea.transform, false);
            created.Add(root);

            var inner = new GameObject("btn");
            inner.transform.SetParent(root.transform, false);
            var button = inner.AddComponent<Button>();

            buttonRoots.Add(name, root);
            return button;
        }

        private GameObject Root(string name) => buttonRoots[name];

        private static FieldModeMenuButtonVisibilityController.ButtonVisibilityEntry Entry(
            string label, GameObject root, bool town, bool dungeonMode, ModalPanel panel = null)
        {
            return new FieldModeMenuButtonVisibilityController.ButtonVisibilityEntry
            {
                label = label,
                buttonRoot = root,
                showInTown = town,
                showInDungeon = dungeonMode,
                panelToCloseWhenHidden = panel,
            };
        }

        private void SetEntries(
            params FieldModeMenuButtonVisibilityController.ButtonVisibilityEntry[] entries)
        {
            ControllerButtonsField.SetValue(
                controller,
                new List<FieldModeMenuButtonVisibilityController.ButtonVisibilityEntry>(entries));
        }

        /// <summary>씬에 등록된 8개 항목을 그대로 만든다.</summary>
        private void SetSceneEntries(
            ModalPanel recoveryPanel = null,
            ModalPanel dungeonPanel = null,
            ModalPanel archivePanel = null,
            ModalPanel purificationPanel = null)
        {
            AddButton(ExitGame);
            AddButton(Switching);
            AddButton(Inventory);
            AddButton(Recovery);
            AddButton(DungeonEntry);
            AddButton(ReturnTown);
            AddButton(Archive);
            AddButton(Purification);

            SetEntries(
                Entry("게임 종료", Root(ExitGame), true, true),
                Entry("용병 교체", Root(Switching), true, true),
                Entry("인벤토리", Root(Inventory), true, true),
                Entry("회복소", Root(Recovery), true, false, recoveryPanel),
                Entry("던전", Root(DungeonEntry), true, false, dungeonPanel),
                Entry("마을 복귀", Root(ReturnTown), false, true),
                Entry("용병 명부", Root(Archive), true, false, archivePanel),
                Entry("기도", Root(Purification), true, false, purificationPanel));
        }

        /// <summary>엔진 대신 <c>OnEnable</c>을 부른다 - EditMode에서는 활성 상태를 바꿔도 오지 않는다.</summary>
        private void EnableController()
        {
            typeof(FieldModeMenuButtonVisibilityController)
                .GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(controller, null);
        }

        private void DisableController()
        {
            typeof(FieldModeMenuButtonVisibilityController)
                .GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(controller, null);
        }

        /// <summary>매니저의 프레임 잠금만 풀어 "다음 프레임"을 흉내 낸다.</summary>
        private void AllowNextTransition() => ManagerFrameField.SetValue(manager, -1);

        private void EnterDungeon()
        {
            AllowNextTransition();
            Assert.IsTrue(manager.TryEnterDungeon(dungeon), "던전 입장이 받아들여져야 한다");
        }

        private void ReturnToTown()
        {
            AllowNextTransition();
            Assert.IsTrue(manager.TryReturnToTown(), "마을 복귀가 받아들여져야 한다");
        }

        private void AssertVisible(string message, params string[] expectedVisible)
        {
            var expected = new HashSet<string>(expectedVisible);
            foreach (KeyValuePair<string, GameObject> pair in buttonRoots)
            {
                Assert.AreEqual(
                    expected.Contains(pair.Key),
                    pair.Value.activeSelf,
                    $"{message}: '{pair.Key}'의 표시 상태");
            }
        }

        private TestModalPanel NewPanel(string name, bool open)
        {
            var go = NewObject(name);
            var panel = go.AddComponent<TestModalPanel>();
            go.SetActive(open);
            return panel;
        }

        // ---- 시험 ----

        [Test]
        public void Town_ShowsSevenButtons_AndHidesReturnTown()
        {
            SetSceneEntries();

            EnableController();

            AssertVisible("마을", ExitGame, Switching, Inventory, Recovery, DungeonEntry, Archive, Purification);
        }

        [Test]
        public void Dungeon_ShowsOnlyExitSwitchingInventoryAndReturnTown()
        {
            SetSceneEntries();
            EnableController();

            EnterDungeon();

            AssertVisible("던전", ExitGame, Switching, Inventory, ReturnTown);
        }

        [Test]
        public void ExitGameButton_StaysVisibleInBothFields()
        {
            SetSceneEntries();
            EnableController();

            Assert.IsTrue(Root(ExitGame).activeSelf, "종료 버튼은 마을에서 보여야 한다");

            EnterDungeon();
            Assert.IsTrue(Root(ExitGame).activeSelf, "종료 버튼은 던전에서도 보여야 한다");

            ReturnToTown();
            Assert.IsTrue(Root(ExitGame).activeSelf, "마을로 돌아와도 그대로 보여야 한다");
        }

        [Test]
        public void BothChecked_StaysVisibleInBothFields()
        {
            SetSceneEntries();
            EnableController();

            Assert.IsTrue(Root(Switching).activeSelf, "마을에서 보여야 한다");
            Assert.IsTrue(Root(Inventory).activeSelf, "마을에서 보여야 한다");

            EnterDungeon();

            Assert.IsTrue(Root(Switching).activeSelf, "던전에서도 그대로 보여야 한다");
            Assert.IsTrue(Root(Inventory).activeSelf, "던전에서도 그대로 보여야 한다");
        }

        [Test]
        public void NeitherChecked_HiddenInBothFields()
        {
            AddButton(Switching);
            SetEntries(Entry("둘 다 꺼짐", Root(Switching), false, false));

            EnableController();
            Assert.IsFalse(Root(Switching).activeSelf, "마을에서 숨어야 한다");

            EnterDungeon();
            Assert.IsFalse(Root(Switching).activeSelf, "던전에서도 숨어야 한다");

            ReturnToTown();
            Assert.IsFalse(Root(Switching).activeSelf, "마을로 돌아와도 계속 숨어야 한다");
        }

        [Test]
        public void HidingButton_ClosesAttachedPanel_WhenOpen()
        {
            TestModalPanel archive = NewPanel("pn_CharacterArchive", open: true);
            TestModalPanel purification = NewPanel("pn_Purification", open: true);
            TestModalPanel recovery = NewPanel("pn_RecoveryStation", open: false);

            SetSceneEntries(
                recoveryPanel: recovery,
                archivePanel: archive,
                purificationPanel: purification);
            EnableController();

            EnterDungeon();

            Assert.IsFalse(archive.gameObject.activeSelf, "열려 있던 용병 명부는 닫혀야 한다");
            Assert.AreEqual(1, archive.CloseRequestCount, "정상 닫기 경로를 한 번만 지나야 한다");
            Assert.IsFalse(purification.gameObject.activeSelf, "열려 있던 기도 패널은 닫혀야 한다");
            Assert.AreEqual(1, purification.CloseRequestCount, "정상 닫기 경로를 한 번만 지나야 한다");
            Assert.AreEqual(0, recovery.CloseRequestCount, "이미 닫혀 있던 패널은 다시 닫지 않는다");
        }

        [Test]
        public void ReturningToTown_DoesNotReopenClosedPanel()
        {
            TestModalPanel archive = NewPanel("pn_CharacterArchive", open: true);

            SetSceneEntries(archivePanel: archive);
            EnableController();

            EnterDungeon();
            ReturnToTown();

            AssertVisible("마을 복귀", ExitGame, Switching, Inventory, Recovery, DungeonEntry, Archive, Purification);
            Assert.IsFalse(archive.gameObject.activeSelf, "닫힌 패널을 대신 열어주지 않는다");
        }

        [Test]
        public void DoesNotTouchInteractableOrOnClick()
        {
            Button purification = AddButton(Purification);
            AddButton(Switching);

            // 교회 미완공 상태를 흉내 낸다 - 보이더라도 눌리지 않아야 한다.
            purification.interactable = false;

            int clicks = 0;
            purification.onClick.AddListener(() => clicks++);

            SetEntries(
                Entry("용병 교체", Root(Switching), true, true),
                Entry("기도", Root(Purification), true, false));

            EnableController();
            Assert.IsTrue(Root(Purification).activeSelf, "마을에서는 보여야 한다");
            Assert.IsFalse(purification.interactable, "해금 조건을 덮어쓰지 않는다");

            EnterDungeon();
            Assert.IsFalse(Root(Purification).activeSelf, "던전에서는 숨어야 한다");
            Assert.IsFalse(purification.interactable, "숨겨도 interactable을 건드리지 않는다");

            ReturnToTown();
            Assert.IsTrue(Root(Purification).activeSelf, "마을로 돌아오면 다시 보여야 한다");
            Assert.IsFalse(purification.interactable, "다시 보여도 interactable을 건드리지 않는다");

            purification.onClick.Invoke();
            Assert.AreEqual(1, clicks, "기존 onClick 리스너가 그대로 남아 있어야 한다");
        }

        [Test]
        public void MenuCollapseAndExpand_KeepsVisibilityRules()
        {
            SetSceneEntries();
            EnableController();

            // 접힘: btnArea가 꺼져도 각 버튼의 activeSelf는 그대로 갱신되어야 한다.
            btnArea.SetActive(false);

            EnterDungeon();
            AssertVisible("접힌 상태에서의 던전 전환", ExitGame, Switching, Inventory, ReturnTown);

            // 펼침: 접혀 있는 동안 정해진 표시 상태가 그대로 드러난다.
            btnArea.SetActive(true);
            AssertVisible("펼친 뒤 던전", ExitGame, Switching, Inventory, ReturnTown);

            ReturnToTown();
            AssertVisible("펼친 뒤 마을", ExitGame, Switching, Inventory, Recovery, DungeonEntry, Archive, Purification);
        }

        [Test]
        public void ReapplyingSameMode_IsIdempotent()
        {
            SetSceneEntries();

            EnableController();
            EnableController();
            EnableController();

            AssertVisible("같은 상태 반복 적용", ExitGame, Switching, Inventory, Recovery, DungeonEntry, Archive, Purification);
        }

        [Test]
        public void DisabledController_StopsFollowingTransitions()
        {
            SetSceneEntries();
            EnableController();

            DisableController();
            EnterDungeon();

            AssertVisible("구독 해제 뒤", ExitGame, Switching, Inventory, Recovery, DungeonEntry, Archive, Purification);

            // 다시 켜면 놓친 전환을 그 자리에서 따라잡는다.
            EnableController();
            AssertVisible("재활성화 뒤", ExitGame, Switching, Inventory, ReturnTown);
        }

        [Test]
        public void MissingAndDuplicateEntries_WarnOnce_AndOtherEntriesStillApply()
        {
            AddButton(Switching);
            AddButton(Recovery);

            SetEntries(
                Entry("빈 항목", null, true, false),
                Entry("용병 교체", Root(Switching), true, true),
                Entry("회복소", Root(Recovery), true, false),
                Entry("회복소(중복)", Root(Recovery), false, true));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("다시 등록"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Button Root가 비어 있는"));

            EnableController();

            Assert.IsTrue(Root(Switching).activeSelf, "정상 항목은 그대로 처리된다");
            Assert.IsTrue(Root(Recovery).activeSelf, "먼저 등록된 설정이 이긴다");

            // 두 번째 적용에서는 같은 경고가 다시 나오지 않는다 - 예상하지 않은 로그가 하나라도 더
            // 오면 여기서 실패한다.
            EnterDungeon();
            LogAssert.NoUnexpectedReceived();

            Assert.IsTrue(Root(Switching).activeSelf, "던전에서도 보인다");
            Assert.IsFalse(Root(Recovery).activeSelf, "중복 항목은 무시되므로 던전에서 숨는다");
        }

        /// <summary>시험용 최소 <see cref="ModalPanel"/>. 열고 닫는 것 외에는 아무것도 하지 않는다.</summary>
        private sealed class TestModalPanel : ModalPanel
        {
            public int CloseRequestCount { get; private set; }

            protected override void RefreshContents()
            {
            }

            protected override void OnCloseRequested()
            {
                CloseRequestCount++;
            }
        }
    }
}
