using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Dungeon;
using Field;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DungeonEditor.Tests
{
    public sealed class DungeonResultUiTests
    {
        private const string PanelPath =
            "Assets/Art/UI/Prefab/panel/pn_DungeonResult.prefab";
        private const string ItemPath =
            "Assets/Art/UI/Prefab/Dungeon/DungeonReward/item_DungeonResultReward.prefab";
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";

        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [Test]
        public void ProductionPanel_UsesExactInspectorReferences()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
            Assert.IsNotNull(prefab);

            DungeonResultPanel panel = prefab.GetComponent<DungeonResultPanel>();
            Assert.IsNotNull(panel, "정산 패널 루트에 DungeonResultPanel이 있어야 한다");

            var so = new SerializedObject(panel);
            AssertReference<TextMeshProUGUI>(so, "dungeonNameText", "lb_DungeonName");
            AssertReference<TextMeshProUGUI>(so, "elapsedTimeText", "lb_Timer");
            AssertReference<TextMeshProUGUI>(so, "defeatedMonsterCountText", "lb_Count");
            AssertReference<TextMeshProUGUI>(so, "earnedCurrencyText", "lb_Count");
            AssertReference<RectTransform>(so, "rewardItemContent", "content");
            AssertReference<Button>(so, "confirmButton", "btn_Confirm");

            Object kill = so.FindProperty("defeatedMonsterCountText").objectReferenceValue;
            Object currency = so.FindProperty("earnedCurrencyText").objectReferenceValue;
            Assert.AreNotSame(kill, currency, "같은 이름의 두 lb_Count는 정확한 서로 다른 참조여야 한다");

            var rewardPrefab = so.FindProperty("rewardItemPrefab").objectReferenceValue
                as DungeonResultRewardItemView;
            Assert.IsNotNull(rewardPrefab);
            Assert.AreEqual("item_DungeonResultReward", rewardPrefab.name);

            Assert.IsTrue(so.FindProperty("blockBackgroundInput").boolValue,
                "정산 패널은 뒤쪽 입력을 막는 확인형 모달이어야 한다");

            SerializedProperty day = so.FindProperty("dayOrMoreText");
            Assert.IsNotNull(day);
            Assert.AreEqual(8207294704640004L,
                day.FindPropertyRelative("m_TableEntryReference.m_KeyId").longValue,
                "01_UI / 38의 실제 Entry ID가 연결되어야 한다");
        }

        [Test]
        public void ProductionRewardItem_UsesIconAndLongStackReferences()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ItemPath);
            Assert.IsNotNull(prefab);

            DungeonResultRewardItemView view = prefab.GetComponent<DungeonResultRewardItemView>();
            Assert.IsNotNull(view);
            var so = new SerializedObject(view);
            AssertReference<Image>(so, "itemIcon", "sp_ItemIcon");
            AssertReference<TextMeshProUGUI>(so, "stackCountText", "lb_StackCount");

            GameObject instance = Object.Instantiate(prefab);
            created.Add(instance);
            DungeonResultRewardItemView runtimeView = instance.GetComponent<DungeonResultRewardItemView>();
            runtimeView.Bind(new DungeonSessionItemReward(null, "item_a", long.MaxValue));

            var runtimeSo = new SerializedObject(runtimeView);
            var count = runtimeSo.FindProperty("stackCountText").objectReferenceValue as TextMeshProUGUI;
            var icon = runtimeSo.FindProperty("itemIcon").objectReferenceValue as Image;
            Assert.AreEqual(long.MaxValue.ToString(), count.text);
            Assert.IsTrue(count.gameObject.activeSelf);
            Assert.IsFalse(icon.enabled, "아이콘이 없으면 Image는 비활성이어야 한다");
        }

        [Test]
        public void Panel_CloseRequestsConfirm_ButDirectDisableDoesNot()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
            var parent = new GameObject("PanelParent", typeof(RectTransform));
            created.Add(parent);
            GameObject instance = Object.Instantiate(prefab, parent.transform, false);
            created.Add(instance);

            DungeonResultPanel panel = instance.GetComponent<DungeonResultPanel>();
            DungeonSessionSnapshot snapshot = MakeSnapshot(65d);
            int confirmations = 0;
            long confirmedSequence = 0L;
            panel.ConfirmationRequested += sequence =>
            {
                confirmations++;
                confirmedSequence = sequence;
            };

            Assert.IsTrue(panel.ShowSnapshot(snapshot));
            Assert.AreEqual(0, confirmations, "Open/Peek만으로 확인 처리하면 안 된다");
            panel.Close();
            Assert.AreEqual(1, confirmations);
            Assert.AreEqual(snapshot.SessionSequence, confirmedSequence);

            Assert.IsTrue(panel.ShowSnapshot(snapshot));
            instance.SetActive(false);
            Assert.AreEqual(1, confirmations, "직접 비활성화는 Consume용 확인 요청이 아니다");
        }

        [Test]
        public void Scene_HasOneCoordinator_WithExactRuntimeReferences()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                DungeonResultCoordinator[] all =
                    Object.FindObjectsOfType<DungeonResultCoordinator>(true);
                DungeonResultCoordinator[] coordinators = System.Array.FindAll(
                    all, value => value.gameObject.scene == scene);
                Assert.AreEqual(1, coordinators.Length);

                var so = new SerializedObject(coordinators[0]);
                var tracker = so.FindProperty("sessionTracker").objectReferenceValue
                    as DungeonSessionTracker;
                var panel = so.FindProperty("resultPanel").objectReferenceValue
                    as DungeonResultPanel;
                var sequencer = so.FindProperty("transitionSequencer").objectReferenceValue
                    as FieldTransitionSequencer;

                Assert.IsNotNull(tracker);
                Assert.IsNotNull(panel);
                Assert.IsNotNull(sequencer);
                Assert.AreEqual(scene, tracker.gameObject.scene);
                Assert.AreEqual(scene, panel.gameObject.scene);
                Assert.AreEqual(scene, sequencer.gameObject.scene);
                Assert.AreSame(tracker.gameObject, coordinators[0].gameObject,
                    "Coordinator는 기존 FieldSystem 관리자 오브젝트에만 추가한다");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void Coordinator_DefersUntilTransitionCompleted()
        {
            DungeonSessionTracker tracker = CreateInactiveTracker(out DungeonSessionLedger ledger);
            DungeonSessionSnapshot snapshot = EnqueueSnapshot(ledger, "deferred", 10d);
            DungeonResultPanel panel = CreatePanelInstance();
            FieldTransitionSequencer sequencer = CreateSequencer(isPlaying: true);
            DungeonResultCoordinator coordinator = CreateCoordinator(tracker, panel, sequencer);

            Assert.IsFalse(panel.gameObject.activeSelf,
                "귀환 연출 중에는 완료 스냅샷이 있어도 패널을 열지 않는다");
            Assert.AreEqual(1, tracker.PendingCompletedSessionCount);

            SetSequencerPlaying(sequencer, false);
            InvokePrivate(coordinator, "HandleTransitionCompleted");

            Assert.IsTrue(panel.gameObject.activeSelf);
            Assert.AreEqual(snapshot.SessionSequence, panel.DisplayedSessionSequence);
            Assert.AreEqual(1, tracker.PendingCompletedSessionCount,
                "표시는 Peek이므로 아직 Consume하지 않는다");
        }

        [Test]
        public void Coordinator_ConfirmConsumesOne_ThenShowsNextFifoNextFrame()
        {
            DungeonSessionTracker tracker = CreateInactiveTracker(out DungeonSessionLedger ledger);
            DungeonSessionSnapshot first = EnqueueSnapshot(ledger, "first", 1d);
            DungeonSessionSnapshot second = EnqueueSnapshot(ledger, "second", 2d);
            DungeonResultPanel panel = CreatePanelInstance();
            DungeonResultCoordinator coordinator = CreateCoordinator(tracker, panel, null);
            InvokePrivate(coordinator, "Subscribe");
            InvokePrivate(coordinator, "TryShowNextCompletedSession");

            Assert.IsTrue(panel.gameObject.activeSelf);
            Assert.AreEqual(first.SessionSequence, panel.DisplayedSessionSequence);
            Assert.AreEqual(2, tracker.PendingCompletedSessionCount,
                "첫 표시 전에는 FIFO를 소비하지 않는다");

            panel.Close();
            Assert.AreEqual(1, tracker.PendingCompletedSessionCount,
                "확인 한 번은 선두 결과 하나만 소비한다");
            Assert.IsFalse(panel.gameObject.activeSelf);
            if (panel.HasSnapshot)
                InvokePrivate(panel, "OnModalClosed");

            IEnumerator nextFrame = InvokePrivateEnumerator(coordinator, "ShowNextFrame");
            Assert.IsTrue(nextFrame.MoveNext(), "첫 단계는 다음 프레임까지 대기한다");
            Assert.IsFalse(panel.gameObject.activeSelf,
                "패널이 닫힌 현재 프레임에는 다음 결과를 표시하지 않는다");
            Assert.IsFalse(nextFrame.MoveNext(), "다음 프레임에 표시한 뒤 루틴이 끝난다");

            Assert.IsTrue(panel.gameObject.activeSelf);
            Assert.AreEqual(second.SessionSequence, panel.DisplayedSessionSequence,
                "패널이 완전히 닫힌 다음 프레임에 두 번째 결과를 표시한다");
            Assert.AreEqual(1, tracker.PendingCompletedSessionCount,
                "두 번째 결과도 표시만으로는 소비하지 않는다");

            panel.Close();
            Assert.AreEqual(0, tracker.PendingCompletedSessionCount);
        }

        private DungeonSessionSnapshot MakeSnapshot(double elapsedSeconds)
        {
            var dungeon = ScriptableObject.CreateInstance<DungeonDefinition>();
            created.Add(dungeon);
            var so = new SerializedObject(dungeon);
            so.FindProperty("dungeonId").stringValue = "test_dungeon";
            so.ApplyModifiedPropertiesWithoutUndo();

            var ledger = new DungeonSessionLedger();
            Assert.AreEqual(SessionStartResult.Started, ledger.TryStartSession(dungeon));
            Assert.IsTrue(ledger.TryCompleteSession(elapsedSeconds, out DungeonSessionSnapshot snapshot));
            return snapshot;
        }

        private DungeonResultPanel CreatePanelInstance()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath);
            var parent = new GameObject("ResultPanelParent", typeof(RectTransform));
            created.Add(parent);
            GameObject instance = Object.Instantiate(prefab, parent.transform, false);
            created.Add(instance);
            return instance.GetComponent<DungeonResultPanel>();
        }

        private DungeonSessionTracker CreateInactiveTracker(out DungeonSessionLedger ledger)
        {
            var go = new GameObject("ResultTestTracker");
            go.SetActive(false);
            created.Add(go);
            DungeonSessionTracker tracker = go.AddComponent<DungeonSessionTracker>();

            FieldInfo field = typeof(DungeonSessionTracker).GetField(
                "ledger", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            ledger = (DungeonSessionLedger)field.GetValue(tracker);
            return tracker;
        }

        private FieldTransitionSequencer CreateSequencer(bool isPlaying)
        {
            var go = new GameObject("ResultTestSequencer");
            go.SetActive(false);
            created.Add(go);
            FieldTransitionSequencer sequencer = go.AddComponent<FieldTransitionSequencer>();
            SetSequencerPlaying(sequencer, isPlaying);
            return sequencer;
        }

        private DungeonResultCoordinator CreateCoordinator(
            DungeonSessionTracker tracker,
            DungeonResultPanel panel,
            FieldTransitionSequencer sequencer)
        {
            var go = new GameObject("ResultTestCoordinator");
            go.SetActive(false);
            created.Add(go);
            DungeonResultCoordinator coordinator = go.AddComponent<DungeonResultCoordinator>();
            var so = new SerializedObject(coordinator);
            so.FindProperty("sessionTracker").objectReferenceValue = tracker;
            so.FindProperty("resultPanel").objectReferenceValue = panel;
            so.FindProperty("transitionSequencer").objectReferenceValue = sequencer;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(true);
            return coordinator;
        }

        private DungeonSessionSnapshot EnqueueSnapshot(
            DungeonSessionLedger ledger, string id, double elapsedSeconds)
        {
            var dungeon = ScriptableObject.CreateInstance<DungeonDefinition>();
            created.Add(dungeon);
            var so = new SerializedObject(dungeon);
            so.FindProperty("dungeonId").stringValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(SessionStartResult.Started, ledger.TryStartSession(dungeon));
            Assert.IsTrue(ledger.TryCompleteSession(elapsedSeconds, out DungeonSessionSnapshot snapshot));
            return snapshot;
        }

        private static void SetSequencerPlaying(FieldTransitionSequencer sequencer, bool value)
        {
            FieldInfo field = typeof(FieldTransitionSequencer).GetField(
                "<IsPlaying>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(sequencer, value);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, methodName);
            method.Invoke(target, null);
        }

        private static IEnumerator InvokePrivateEnumerator(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, methodName);
            return (IEnumerator)method.Invoke(target, null);
        }

        private static void AssertReference<T>(SerializedObject owner, string propertyName, string objectName)
            where T : Object
        {
            SerializedProperty property = owner.FindProperty(propertyName);
            Assert.IsNotNull(property, propertyName);
            T value = property.objectReferenceValue as T;
            Assert.IsNotNull(value, propertyName);
            Assert.AreEqual(objectName, value.name, propertyName);
        }
    }
}
