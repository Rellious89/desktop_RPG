using System;
using System.Collections.Generic;
using System.Reflection;
using Common;
using Dungeon;
using Field;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FieldEditor.Tests
{
    /// <summary>
    /// 하단 메뉴 버튼의 <b>건축 완공 게이트</b> 시험(<see cref="FieldModeMenuButtonVisibilityController"/>).
    /// 용병 명부(건물 1)와 기도(건물 2)는 마을에서 <b>보이도록 설정</b>돼 있어도, 해당 건물이 사용자
    /// 완료 확정되기 전에는 버튼 루트가 꺼져 있어야 한다.
    ///
    /// <b>실제 저장 파일에 가지 않는다.</b> <see cref="SaveSystem"/>을 메모리 저장소로 바꿔
    /// (ConfigureForTests) 건설 기록을 직접 세운다 - 완공 판정은 공통 정책
    /// (<see cref="Building.BuildingCompletionPolicy"/>)이 그 기록과 실제 UTC를 보고 내린다.
    ///
    /// <b>전환·활성화는 매니저와 리플렉션으로 일으킨다.</b> EditMode에서는 활성 상태를 바꿔도
    /// OnEnable/Update가 오지 않으므로, 기존 필드 표시 시험과 같은 방식으로 비공개 진입점을 부른다.
    /// </summary>
    public sealed class FieldModeMenuBuildingGateTests
    {
        private static readonly MethodInfo ConfigureSaveMethod = typeof(SaveSystem).GetMethod(
            "ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo ManagerFrameField = typeof(FieldModeManager).GetField(
            "lastTransitionFrame", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo ControllerManagerField =
            typeof(FieldModeMenuButtonVisibilityController).GetField(
                "fieldModeManager", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo ControllerButtonsField =
            typeof(FieldModeMenuButtonVisibilityController).GetField(
                "buttons", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>확정 완료를 흉내 내는 과거 시각 - 실제 UTC보다 반드시 앞선다.</summary>
        private static readonly DateTime PastUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private const string Archive = "btn_CharacterArchive";
        private const string Purification = "btn_Purification";
        private const string Switching = "btn_switching";

        private readonly List<Object> created = new List<Object>();
        private readonly Dictionary<string, GameObject> buttonRoots = new Dictionary<string, GameObject>();

        private GameObject mainMenu;
        private GameObject btnArea;
        private FieldModeManager manager;
        private FieldModeMenuButtonVisibilityController controller;
        private DungeonDefinition dungeon;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(ConfigureSaveMethod, "SaveSystem.ConfigureForTests");
            Assert.IsNotNull(ManagerFrameField, "lastTransitionFrame");
            Assert.IsNotNull(ControllerManagerField, "fieldModeManager");
            Assert.IsNotNull(ControllerButtonsField, "buttons");

            // 실제 저장 파일 근처에도 가지 않도록 메모리 저장소를 끼운다(건물 시험과 같은 방식).
            ConfigureSaveMethod.Invoke(null, new object[] { new FakeStorage(), null, null });

            buttonRoots.Clear();

            var managerObject = NewObject("FieldSystem");
            manager = managerObject.AddComponent<FieldModeManager>();

            dungeon = ScriptableObject.CreateInstance<DungeonDefinition>();
            created.Add(dungeon);
            typeof(DungeonDefinition)
                .GetField("dungeonId", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(dungeon, "TEST_DUNGEON");

            mainMenu = NewObject("MainMenu");
            btnArea = NewObject("btnArea");
            btnArea.transform.SetParent(mainMenu.transform, false);

            controller = mainMenu.AddComponent<FieldModeMenuButtonVisibilityController>();
            ControllerManagerField.SetValue(controller, manager);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            }
            created.Clear();

            ConfigureSaveMethod.Invoke(null, new object[] { null, null, null });
        }

        // ---- 도우미 ----

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            return go;
        }

        private GameObject AddButton(string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(btnArea.transform, false);
            created.Add(root);

            var inner = new GameObject("btn");
            inner.transform.SetParent(root.transform, false);
            inner.AddComponent<Button>();

            buttonRoots.Add(name, root);
            return root;
        }

        private GameObject Root(string name) => buttonRoots[name];

        private static FieldModeMenuButtonVisibilityController.ButtonVisibilityEntry Entry(
            string label, GameObject root, bool town, bool dungeonMode, string requiredBuildingId = null)
        {
            return new FieldModeMenuButtonVisibilityController.ButtonVisibilityEntry
            {
                label = label,
                buttonRoot = root,
                showInTown = town,
                showInDungeon = dungeonMode,
                requiredBuildingId = requiredBuildingId,
            };
        }

        /// <summary>씬과 같은 게이트 구성(용병 명부=1, 기도=2, 게이트 없는 대조군 하나)을 세운다.</summary>
        private void SetGatedEntries()
        {
            AddButton(Switching);
            AddButton(Archive);
            AddButton(Purification);

            ControllerButtonsField.SetValue(
                controller,
                new List<FieldModeMenuButtonVisibilityController.ButtonVisibilityEntry>
                {
                    Entry("용병 교체", Root(Switching), true, true),
                    Entry("용병 명부", Root(Archive), true, false, "1"),
                    Entry("기도", Root(Purification), true, false, "2"),
                });
        }

        private void EnableController()
        {
            typeof(FieldModeMenuButtonVisibilityController)
                .GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(controller, null);
        }

        private void UpdateController()
        {
            typeof(FieldModeMenuButtonVisibilityController)
                .GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(controller, null);
        }

        private void EnterDungeon()
        {
            ManagerFrameField.SetValue(manager, -1);
            Assert.IsTrue(manager.TryEnterDungeon(dungeon), "던전 입장이 받아들여져야 한다");
        }

        /// <summary>건물을 확정 완료 상태로 만든다 - 예정 시각이 과거이고 사용자가 완료를 확정했다.</summary>
        private static void MarkConfirmedCompleted(string buildingId)
        {
            SaveSystem.Data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = buildingId,
                startedAtUtc = SaveData.FormatTimestamp(PastUtc),
                completeAtUtc = SaveData.FormatTimestamp(PastUtc),
                completionNotified = true,
            });
        }

        /// <summary>완료 확인 대기 상태 - 예정 시각은 지났지만 아직 확정하지 않았다.</summary>
        private static void MarkAwaitingConfirmation(string buildingId)
        {
            SaveSystem.Data.buildingConstructions.Add(new BuildingConstructionSaveState
            {
                buildingId = buildingId,
                startedAtUtc = SaveData.FormatTimestamp(PastUtc),
                completeAtUtc = SaveData.FormatTimestamp(PastUtc),
                completionNotified = false,
            });
        }

        // ---- 5. 미완공 여관: 용병 명부 숨김 ----

        [Test]
        public void 미완공_여관이면_마을에서도_용병_명부_루트가_숨는다()
        {
            SetGatedEntries();

            EnableController();

            Assert.IsTrue(Root(Switching).activeSelf, "게이트 없는 버튼은 마을에서 보인다");
            Assert.IsFalse(Root(Archive).activeSelf, "여관이 완공되기 전에는 용병 명부가 숨어야 한다");
        }

        // ---- 6. 완료 확인 대기 여관: 용병 명부 숨김 ----

        [Test]
        public void 완료_확인_대기_여관이면_용병_명부_루트가_숨는다()
        {
            MarkAwaitingConfirmation("1");
            SetGatedEntries();

            EnableController();

            Assert.IsFalse(Root(Archive).activeSelf,
                "예정 시각이 지나도 사용자가 확정하기 전에는 아직 미완공으로 본다");
        }

        // ---- 7. 확정 완료 여관: 마을에서 용병 명부 표시 ----

        [Test]
        public void 확정_완료_여관이면_마을에서_용병_명부가_보인다()
        {
            MarkConfirmedCompleted("1");
            SetGatedEntries();

            EnableController();

            Assert.IsTrue(Root(Archive).activeSelf, "여관이 확정 완료되면 마을에서 보여야 한다");
        }

        // ---- 8. 미완공 교회: 기도 숨김 ----

        [Test]
        public void 미완공_교회이면_마을에서도_기도_루트가_숨는다()
        {
            MarkConfirmedCompleted("1"); // 여관은 완공(용병 명부는 보임) - 교회만 미완공으로 둔다.
            SetGatedEntries();

            EnableController();

            Assert.IsTrue(Root(Archive).activeSelf, "여관 완공은 그대로 보인다");
            Assert.IsFalse(Root(Purification).activeSelf, "교회가 완공되기 전에는 기도가 숨어야 한다");
        }

        // ---- 9. 확정 완료 교회: 마을에서 기도 표시 ----

        [Test]
        public void 확정_완료_교회이면_마을에서_기도가_보인다()
        {
            MarkConfirmedCompleted("2");
            SetGatedEntries();

            EnableController();

            Assert.IsTrue(Root(Purification).activeSelf, "교회가 확정 완료되면 마을에서 보여야 한다");
        }

        // ---- 10. 던전에서는 두 버튼 모두 숨김 ----

        [Test]
        public void 던전에서는_완공됐어도_두_버튼_모두_숨는다()
        {
            MarkConfirmedCompleted("1");
            MarkConfirmedCompleted("2");
            SetGatedEntries();
            EnableController();

            // 마을에서는 둘 다 보인다.
            Assert.IsTrue(Root(Archive).activeSelf);
            Assert.IsTrue(Root(Purification).activeSelf);

            EnterDungeon();

            Assert.IsFalse(Root(Archive).activeSelf, "던전에서는 완공됐어도 용병 명부가 숨어야 한다");
            Assert.IsFalse(Root(Purification).activeSelf, "던전에서는 완공됐어도 기도가 숨어야 한다");
        }

        // ---- 런타임 갱신: 완료 확정 직후 메뉴가 갱신된다 ----

        [Test]
        public void 완료_확정_직후_Update에서_용병_명부가_나타난다()
        {
            SetGatedEntries();
            EnableController();
            Assert.IsFalse(Root(Archive).activeSelf, "완공 전에는 숨어 있다");

            // 필드 전환 없이 완료가 확정된다(완료 버튼 클릭에 해당).
            MarkConfirmedCompleted("1");
            UpdateController();

            Assert.IsTrue(Root(Archive).activeSelf, "완료 확정 직후 Update에서 나타나야 한다");
        }

        [Test]
        public void 완료_상태가_그대로면_Update는_다시_적용하지_않는다()
        {
            MarkConfirmedCompleted("1");
            SetGatedEntries();
            EnableController();
            Assert.IsTrue(Root(Archive).activeSelf);

            // 사용자가 버튼 루트를 임의로 끈 뒤 상태 변화가 없으면 Update가 다시 켜지 않는다 -
            // 값이 바뀔 때만 재적용한다는 성질(매 프레임 SetActive를 하지 않음)을 드러낸다.
            Root(Archive).SetActive(false);
            UpdateController();

            Assert.IsFalse(Root(Archive).activeSelf, "완료 상태가 그대로면 Update는 아무것도 다시 켜지 않는다");
        }

        /// <summary>메모리 위 가짜 저장소. 읽기는 '없음'을 돌려주어 <see cref="SaveSystem"/>이 빈 문서를
        /// 만들게 하고, 쓰기는 이 시험에서 일어나면 안 되므로 예외로 막는다(게이트는 읽기 전용이다).</summary>
        private sealed class FakeStorage : ISaveStorage
        {
            public bool WritesBlocked => false;
            public string BlockedReason => null;
            public SaveReadResult ReadPrimary() => SaveReadResult.Missing("fake://primary");
            public SaveReadResult ReadBackup() => SaveReadResult.Missing("fake://backup");

            public SaveWriteResult Write(string text) =>
                throw new InvalidOperationException("게이트 시험 중 저장이 시도되었습니다 - 게이트는 읽기 전용입니다.");

            public SaveQuarantineResult QuarantinePrimary(string reason) =>
                SaveQuarantineResult.Moved("fake://corrupted/primary");
        }
    }
}
