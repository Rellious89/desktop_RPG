using CharacterArchive;
using Common;
using NUnit.Framework;
using Quest;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CharacterArchiveEditorTests
{
    public sealed class CharacterStoryQuestArchivePrefabTests
    {
        private const string PrefabPath = "Assets/Art/UI/Prefab/panel/pn_CharacterArchive.prefab";
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";

        [Test]
        public void CharacterArchive_QuestUiHasConfiguredSingleVerticalScrollView()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform questInfo = Find(root.transform, "pn_right/QuestInfo");
                var controller = questInfo.GetComponent<CharacterStoryQuestUiController>();
                Assert.NotNull(controller);
                Assert.IsNull(root.GetComponent<CharacterStoryQuestUiController>());
                var panel = root.GetComponent<CharacterArchivePanel>();
                Assert.AreSame(controller, new SerializedObject(panel).FindProperty("storyQuestUi").objectReferenceValue);
                Assert.IsTrue(controller.HasRequiredReferences);
                SerializedObject panelSerialized = new SerializedObject(panel);
                Assert.AreSame(Find(root.transform, "pn_right").gameObject,
                    panelSerialized.FindProperty("rightPanel").objectReferenceValue,
                    "우측 셸 전체가 닫혀야 공용 전환 버튼도 함께 숨습니다.");
                Assert.AreSame(Find(root.transform, "pn_right/CharacterInfo/bg/top/btn_close").GetComponent<Button>(),
                    panelSerialized.FindProperty("rightCloseButton").objectReferenceValue);
                Transform current = Find(root.transform, "pn_right/QuestInfo/QuestInfo/Current");
                SerializedObject controllerSerialized = new SerializedObject(controller);
                Assert.AreSame(Find(root.transform, "pn_right/QuestInfo/bg/top/btn_close").GetComponent<Button>(),
                    controllerSerialized.FindProperty("closeButton").objectReferenceValue,
                    "퀘스트 페이지가 자신의 닫기 버튼을 소유해야 합니다.");
                Assert.AreSame(current.Find("CurrentProgress").GetComponent<Slider>(),
                    controllerSerialized.FindProperty("currentProgressSlider").objectReferenceValue);
                Assert.AreSame(Find(root.transform, "pn_right/QuestInfo/QuestInfo/TotalProgress").GetComponent<Slider>(),
                    controllerSerialized.FindProperty("totalProgressSlider").objectReferenceValue);
                Transform totalProgressText = Find(root.transform,
                    "pn_right/QuestInfo/QuestInfo/TotalProgress/bottomDeco/sp_description/lb_totalProgress");
                Assert.AreSame(totalProgressText.GetComponent<TMP_Text>(),
                    controllerSerialized.FindProperty("totalProgressText").objectReferenceValue,
                    "보이는 전체 진행 문구가 컨트롤러에 연결되어야 합니다.");
                Transform completeButtonText = Find(root.transform,
                    "pn_right/QuestInfo/QuestInfo/btn_QuestComplete/lb_QuestComplete");
                Assert.AreSame(completeButtonText.GetComponent<TMP_Text>(),
                    controllerSerialized.FindProperty("completeButtonText").objectReferenceValue,
                    "완료 버튼 문구는 진행/완료 상태에 따라 컨트롤러가 갱신해야 합니다.");
                Transform allClear = Find(root.transform, "pn_right/QuestInfo/QuestInfo/lb_AllClear");
                Assert.AreSame(allClear.GetComponent<TMP_Text>(),
                    controllerSerialized.FindProperty("allClearText").objectReferenceValue,
                    "공용 QuestInfo의 lb_AllClear를 명부 상세 완료 상태에 직접 연결해야 합니다.");
                Transform allList = Find(root.transform, "pn_right/QuestInfo/bg_AllList");
                Transform allListScroll = Find(allList, "ObjectiveScroll");
                Transform allListContent = Find(allListScroll, "Viewport/Content");
                Transform allListTemplate = Find(allListContent, "QuestInfo");
                CharacterStoryQuestListItemView allListItem = allListTemplate.GetComponent<CharacterStoryQuestListItemView>();
                Assert.NotNull(allListItem);
                Assert.IsTrue(allListItem.HasRequiredReferences);
                SerializedObject listItemSerialized = new SerializedObject(allListItem);
                Transform allListTitle = Find(allListTemplate, "lb_title");
                Transform allListComplete = Find(allListTitle, "lb_complete");
                Assert.AreSame(allListTitle.GetComponent<TMP_Text>(),
                    listItemSerialized.FindProperty("titleText").objectReferenceValue);
                Assert.AreSame(allListComplete.gameObject,
                    listItemSerialized.FindProperty("completeLabel").objectReferenceValue,
                    "새 완료 라벨은 목록 행의 직렬화 참조로 직접 연결되어야 합니다.");
                Assert.AreEqual(14504778829651968L,
                    allListTitle.GetComponent<LocalizedTMPText>().TextReference.TableEntryReference.KeyId,
                    "lb_title은 UI 로컬라이즈 106번 엔트리를 유지해야 합니다.");
                Assert.AreEqual(14505869390635008L,
                    allListComplete.GetComponent<LocalizedTMPText>().TextReference.TableEntryReference.KeyId,
                    "lb_complete는 UI 로컬라이즈 107번 엔트리를 유지해야 합니다.");
                Assert.AreSame(allList.gameObject, controllerSerialized.FindProperty("allQuestListRoot").objectReferenceValue);
                Assert.AreSame(allListScroll.GetComponent<ScrollRect>(), controllerSerialized.FindProperty("allQuestListScroll").objectReferenceValue);
                Assert.AreSame(allListContent.GetComponent<RectTransform>(), controllerSerialized.FindProperty("allQuestListContent").objectReferenceValue);
                Assert.AreSame(allListItem, controllerSerialized.FindProperty("allQuestListItemTemplate").objectReferenceValue);
                Assert.AreSame(Find(allListScroll, "Viewport/sp_selectAni").GetComponent<RectTransform>(),
                    controllerSerialized.FindProperty("allQuestSelection").objectReferenceValue);
                Transform content = Find(current, "ObjectiveScroll/Viewport/Content");
                ScrollRect scroll = current.Find("ObjectiveScroll").GetComponent<ScrollRect>();
                Assert.IsTrue(scroll.vertical); Assert.IsFalse(scroll.horizontal);
                Assert.AreEqual(ScrollRect.MovementType.Clamped, scroll.movementType);
                RectTransform viewport = scroll.viewport;
                Assert.NotNull(viewport.GetComponent<RectMask2D>());
                Image viewportInput = viewport.GetComponent<Image>();
                Assert.NotNull(viewportInput, "빈 Viewport 영역도 ScrollRect 포인터 입력을 받아야 합니다.");
                Assert.IsTrue(viewportInput.raycastTarget);
                Assert.AreEqual(0f, viewportInput.color.a, "입력용 Viewport Graphic은 화면에 보이면 안 됩니다.");
                Assert.AreSame(viewport, scroll.viewport);
                Assert.AreSame(content.GetComponent<RectTransform>(), scroll.content);
                Assert.NotNull(content.GetComponent<VerticalLayoutGroup>());
                ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
                Assert.NotNull(fitter); Assert.AreEqual(ContentSizeFitter.FitMode.PreferredSize, fitter.verticalFit);
                Assert.AreSame(content, Find(content, "QuestType").parent);
                Assert.AreSame(content, Find(content, "QuestDesctiption").parent);
                Transform reward = Find(content, "QuestReward");
                Transform rewardCurrency = Find(reward, "Reward_Currency");
                Transform rewardItem = Find(reward, "Reward_Item");
                Assert.AreSame(reward.gameObject, controllerSerialized.FindProperty("rewardRoot").objectReferenceValue);
                Assert.AreSame(rewardCurrency.gameObject,
                    controllerSerialized.FindProperty("rewardCurrencyRoot").objectReferenceValue);
                Assert.AreSame(Find(rewardCurrency, "lb_RewardValue").GetComponent<TMP_Text>(),
                    controllerSerialized.FindProperty("rewardCurrencyAmountText").objectReferenceValue);
                Assert.AreSame(rewardItem.gameObject, controllerSerialized.FindProperty("rewardItemRoot").objectReferenceValue);
                Assert.AreSame(rewardItem.GetComponentInChildren<InventorySlotView>(true),
                    controllerSerialized.FindProperty("rewardItemSlot").objectReferenceValue,
                    "아이템 보상은 InventorySlotView를 써야 기존 아이콘/수량/호버 툴팁 경로를 그대로 공유합니다.");
                LocalizedTMPText totalProgressLocalizer = totalProgressText.GetComponent<LocalizedTMPText>();
                Assert.IsTrue(totalProgressLocalizer == null || !totalProgressLocalizer.enabled,
                    "동적 총 진행 문구는 컨트롤러가 단독 소유해야 합니다.");
                LocalizedTMPText completeButtonLocalizer = completeButtonText.GetComponent<LocalizedTMPText>();
                Assert.IsTrue(completeButtonLocalizer == null || !completeButtonLocalizer.enabled,
                    "동적 완료 버튼 문구는 정적 LocalizedTMPText와 동시에 갱신되면 안 됩니다.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void CharacterArchive_QuestLineTemplatesAreInactiveAndDoNotBlockScrolling()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform content = Find(root.transform, "pn_right/QuestInfo/QuestInfo/Current/ObjectiveScroll/Viewport/Content");
                TMP_Text typeTemplate = Find(content, "QuestType/lb_contents").GetComponent<TMP_Text>();
                TMP_Text descriptionTemplate = Find(content, "QuestDesctiption/lb_contents").GetComponent<TMP_Text>();
                Assert.IsFalse(typeTemplate.gameObject.activeSelf);
                Assert.IsFalse(descriptionTemplate.gameObject.activeSelf);
                Assert.IsFalse(typeTemplate.raycastTarget);
                Assert.IsFalse(descriptionTemplate.raycastTarget);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void SharedQuestInfo_UsesItsExistingAllClearLabelForArchiveCompletionState()
        {
            GameObject root = PrefabUtility.LoadPrefabContents("Assets/Art/UI/Prefab/Quest/QuestInfo.prefab");
            try
            {
                CharacterStoryQuestUiController controller = root.GetComponent<CharacterStoryQuestUiController>();
                Assert.NotNull(controller);
                SerializedObject serialized = new SerializedObject(controller);
                Assert.AreSame(Find(root.transform, "QuestInfo/lb_AllClear").GetComponent<TMP_Text>(),
                    serialized.FindProperty("allClearText").objectReferenceValue);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        [Test]
        public void DesktopResize_CharacterArchivePrefabInstanceHasQuestUiController()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            CharacterStoryQuestUiController[] controllers = Object.FindObjectsOfType<CharacterStoryQuestUiController>(true);
            Assert.AreEqual(1, controllers.Length, $"{scene.name}에 연결된 CharacterStoryQuestUiController가 하나 필요합니다.");
            Assert.IsTrue(controllers[0].HasRequiredReferences);
            Assert.AreEqual("QuestInfo", controllers[0].gameObject.name);
        }

        [Test]
        public void QuestNotification_PrefabOwnsOnlyItsVisualAndClickReferences()
        {
            GameObject root = PrefabUtility.LoadPrefabContents("Assets/Art/UI/Prefab/QuestNotification.prefab");
            try
            {
                QuestNotificationController controller = root.GetComponent<QuestNotificationController>();
                Assert.NotNull(controller);
                Assert.IsTrue(controller.HasRequiredReferences);
                Assert.IsFalse(controller.HasQuestPanelTarget,
                    "프리팹은 씬 오브젝트를 전역 탐색하지 않고, scene instance가 좁은 딥링크 참조를 준다.");
                Transform message = root.transform.Find("sp_messageBox");
                Assert.NotNull(message);
                Assert.NotNull(message.GetComponent<Button>());
                Assert.NotNull(message.Find("sp_count/lb_count").GetComponent<TMP_Text>());
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Transform Find(Transform root, string path)
        {
            Transform result = root.Find(path);
            Assert.NotNull(result, path);
            return result;
        }
    }
}
