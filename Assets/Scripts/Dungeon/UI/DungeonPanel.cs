using System;
using System.Collections.Generic;
using Common;
using Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Dungeon
{
    /// <summary>
    /// 던전 입장 패널(pn_Dungeon). 씬 시작 시에는 비활성이며 <see cref="ModalPanelOpener"/>가
    /// <see cref="ModalPanel.Open"/>으로 켠다 - 열고 닫기, 닫기 버튼(btn_close), 배경 입력 차단,
    /// Windows 클릭 관통 예외(패널 루트의 WindowInputRegion)는 <see cref="ModalPanel"/>이 다른 패널과
    /// 같은 방식으로 처리하고, 이 클래스는 <b>목록을 만들고 상세를 그리고 입장 요청을 보내는</b> 일만 한다.
    ///
    /// <b>구조는 프리팹이 소유한다.</b> ScrollRect / Viewport / Content / Mask / LayoutGroup /
    /// ScrollRectInitialPosition 같은 것은 이 코드가 만들지도, 값을 바꾸지도, 참조를 덮어쓰지도 않는다 -
    /// 읽기만 한다. 참조는 전부 Inspector에서 직접 연결하며 <b>이름으로 찾지 않는다</b>. 빠진 참조는
    /// 조용히 채우지 않고 패널을 열 때 한 번만 진단 로그를 남긴다.
    ///
    /// <b>원본(템플릿)은 절대 켜지 않는다.</b> item_dungeonList / item_monster / item_item은 비활성
    /// 상태 그대로 두고, 복제본만 켜서 쓴다. 복제본은 이 클래스가 만든 것만 목록으로 들고 있다가
    /// 선택이 바뀌거나 패널이 닫힐 때 구독을 끊고 파괴한다 - 그래서 닫았다 다시 열어도 항목이 쌓이지 않는다.
    ///
    /// <b>입장 이후는 이 패널이 알지 못한다.</b> 입장 버튼은 <see cref="DungeonEntryService"/>에 요청을
    /// 한 번 보낼 뿐이고, 필드 모드 전환이나 전투 시작은 나중에 그 이벤트를 구독하는 쪽이 담당한다 -
    /// 지금은 구독자가 없어도 정상 동작한다(요청 로그만 남고 패널이 닫힌다).
    /// </summary>
    [DisallowMultipleComponent]
    public class DungeonPanel : ModalPanel
    {
        private static readonly DungeonDefinition[] EmptyDungeons = new DungeonDefinition[0];

        [Header("Data")]
        [Tooltip("표시할 던전 목록의 원천(DungeonCatalog 에셋). 씬에서 던전을 찾아 모으지 않는다.")]
        [SerializeField] private DungeonCatalog catalog;

        [Header("Dungeon List (list_left/viewport/content)")]
        [Tooltip("던전 목록 항목이 들어갈 Content. 비워두면 아래 원본의 부모를 그대로 읽어 쓴다 " +
                 "(값을 다시 써넣지는 않는다).")]
        [SerializeField] private RectTransform dungeonListContent;

        [Tooltip("복제할 던전 목록 항목 원본(item_dungeonList). 비활성 상태 그대로 두며 절대 켜지 않는다.")]
        [SerializeField] private DungeonListItemView dungeonListItemTemplate;

        [Header("Detail (list_right/bg_description)")]
        [Tooltip("선택된 던전이 있을 때만 보이는 상세 루트(bg_description). 목록이 비어 있으면 꺼진다.")]
        [SerializeField] private GameObject detailRoot;

        [Tooltip("월드 이름을 표시할 텍스트(top_description/lb). 선택에 따라 내용이 바뀌므로 " +
                 "여기에 붙어 있는 정적 LocalizedTMPText는 실행 중에 꺼진다.")]
        [SerializeField] private TextMeshProUGUI worldText;

        [Tooltip("월드 문구 틀. {0} 자리에 던전 정의의 월드 이름(로컬라이징된 값)이 들어간다 - " +
                 "인자는 월드 이름 하나뿐이며, 문구 자체도 코드가 아니라 이 참조가 소유한다.")]
        [SerializeField] private LocalizedTextReference worldTextFormat = new LocalizedTextReference();

        [Header("Monster Preview (mid_description/list_monster/list/viewport/content)")]
        [Tooltip("몬스터 미리보기 칸이 들어갈 Content. 비워두면 원본의 부모를 읽어 쓴다.")]
        [SerializeField] private RectTransform monsterPreviewContent;

        [Tooltip("복제할 몬스터 미리보기 원본(item_monster). 비활성 상태 그대로 둔다.")]
        [SerializeField] private DungeonMonsterPreviewView monsterPreviewTemplate;

        [Header("Reward Preview (bot_description/list_item/list/viewport/content)")]
        [Tooltip("대표 보상 칸이 들어갈 Content. 비워두면 원본의 부모를 읽어 쓴다.")]
        [SerializeField] private RectTransform rewardPreviewContent;

        [Tooltip("복제할 보상 미리보기 원본(item_item). 비활성 상태 그대로 둔다.")]
        [SerializeField] private DungeonRewardPreviewView rewardPreviewTemplate;

        [Header("Enter (list_right/bottom/btn_Enter)")]
        [Tooltip("입장 버튼(btn_Enter). 선택이 없거나 유효하지 않으면 꺼진다.")]
        [SerializeField] private Button enterButton;

        private readonly List<DungeonListItemView> spawnedItems = new List<DungeonListItemView>();
        private readonly List<DungeonMonsterPreviewView> spawnedMonsterPreviews = new List<DungeonMonsterPreviewView>();
        private readonly List<DungeonRewardPreviewView> spawnedRewardPreviews = new List<DungeonRewardPreviewView>();

        private DungeonDefinition selectedDungeon;

        // 한 번 연 동안 입장 요청이 이미 나갔는지. 연타로 요청이 두 번 발행되는 것을 막는다.
        private bool enterRequestSent;

        private LocalizedTextReference boundWorldFormat;
        private LocalizedTextReference boundWorldName;
        private string worldFormatText;
        private string worldNameText;

        private bool referencesValidated;
        private bool missingWorldFormatWarned;
        private bool missingWorldNameWarned;
        private bool worldFormatFailureLogged;

        /// <summary>지금 선택된 던전. 검증/디버깅과 테스트용 읽기 전용 값이다.</summary>
        public DungeonDefinition SelectedDungeon => selectedDungeon;

        /// <summary>지금 만들어져 있는 목록 항목 복제본 수.</summary>
        public int SpawnedItemCount => spawnedItems.Count;

        /// <summary>지금 만들어져 있는 몬스터 미리보기 복제본 수.</summary>
        public int SpawnedMonsterPreviewCount => spawnedMonsterPreviews.Count;

        /// <summary>지금 만들어져 있는 보상 미리보기 복제본 수.</summary>
        public int SpawnedRewardPreviewCount => spawnedRewardPreviews.Count;

        /// <summary>이번에 연 동안 입장 요청이 이미 발행됐는지. 패널을 다시 열면 false로 되돌아간다.</summary>
        public bool IsEnterRequestSent => enterRequestSent;

        /// <summary>입장 버튼이 지금 눌릴 수 있는 상태인지.</summary>
        public bool IsEnterInteractable => enterButton != null && enterButton.interactable;

        /// <summary>지금 화면에 그려져 있는 월드 문구. 검증/디버깅용 읽기 전용 값이다.</summary>
        public string CurrentWorldText => worldText != null ? worldText.text : null;

        // ---- 열기 / 닫기 ----

        protected override void OnModalOpened()
        {
            ValidateReferences();

            // 중복 클릭 억제는 "한 번 여는 동안"의 상태다 - 닫았다 다시 열면 당연히 다시 입장할 수 있어야 한다.
            enterRequestSent = false;

            if (enterButton != null)
            {
                enterButton.onClick.RemoveListener(HandleEnterClicked);
                enterButton.onClick.AddListener(HandleEnterClicked);
            }
        }

        protected override void OnModalClosed()
        {
            if (enterButton != null) enterButton.onClick.RemoveListener(HandleEnterClicked);

            // 복제본과 문구 구독을 모두 정리한다 - 다시 열 때 새로 만들기 때문에, 남겨두면 그대로 중복이 된다.
            UnbindWorldText();
            ClearMonsterPreviews();
            ClearRewardPreviews();
            ClearDungeonList();

            selectedDungeon = null;
        }

        /// <summary>패널이 열릴 때(그리고 이미 열린 상태에서 다시 Open이 불릴 때) 목록을 처음부터 다시
        /// 만들고 첫 번째 던전을 선택한다.</summary>
        protected override void RefreshContents()
        {
            RebuildDungeonList();
        }

        // ---- 던전 목록 ----

        private void RebuildDungeonList()
        {
            // 항상 비우고 다시 만든다 - 열려 있는 상태에서 Open이 다시 불려도 항목이 쌓이지 않는다.
            ClearDungeonList();

            IReadOnlyList<DungeonDefinition> entries = catalog != null ? catalog.Dungeons : EmptyDungeons;
            RectTransform parent = ResolveDungeonListParent();

            if (dungeonListItemTemplate != null && parent != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    DungeonDefinition dungeon = entries[i];
                    if (dungeon == null || !dungeon.IsValid) continue;

                    DungeonListItemView item = Instantiate(dungeonListItemTemplate, parent, false);
                    item.name = $"{dungeonListItemTemplate.name}_{dungeon.DungeonId}";
                    // 비활성 상태에서 먼저 바인딩하고 <b>마지막에</b> 켠다 - 켜고 나서 바인딩하면 그 사이에
                    // 복제본에 남아 있던 LocalizedTMPText의 OnEnable이 먼저 돌아 프리팹의 정적 문구가
                    // 한 프레임 보이게 된다.
                    item.Bind(dungeon, HandleDungeonSelected);
                    // 원본은 비활성 그대로 두고 복제본만 켠다.
                    item.gameObject.SetActive(true);
                    spawnedItems.Add(item);
                }
            }

            // 목록이 비어 있으면 선택 없음으로 정리된다(상세가 꺼지고 입장 버튼이 잠긴다).
            SelectDungeon(spawnedItems.Count > 0 ? spawnedItems[0].BoundDungeon : null);
        }

        private void ClearDungeonList()
        {
            for (int i = 0; i < spawnedItems.Count; i++)
            {
                DungeonListItemView item = spawnedItems[i];
                if (item == null) continue;

                // 구독과 리스너를 먼저 끊고 파괴한다 - 원본(템플릿)은 목록에 들어오지 않으므로 파괴 대상이 아니다.
                item.Unbind();
                // Destroy는 프레임 끝에 실행되므로, 같은 프레임에 새 목록을 만들면 옛 항목이 잠깐 함께
                // 보인다. 끄는 것은 즉시 반영되므로 파괴 전에 먼저 꺼서 그 틈을 없앤다.
                item.gameObject.SetActive(false);
                Destroy(item.gameObject);
            }
            spawnedItems.Clear();
        }

        // ---- 선택 ----

        private void HandleDungeonSelected(DungeonDefinition dungeon)
        {
            if (dungeon == null || dungeon == selectedDungeon) return;
            SelectDungeon(dungeon);
        }

        /// <summary>
        /// 선택을 <paramref name="dungeon"/>으로 옮기고 화면 전체를 그 선택 기준으로 다시 맞춘다.
        ///
        /// 순서가 중요하다 - <b>먼저 이전 미리보기를 모두 지운 뒤</b> 선택 표시, 월드 문구, 몬스터,
        /// 보상을 한 번에 새로 만든다. 그래서 선택을 여러 번 옮겨도 미리보기가 누적되지 않고, 중간에
        /// 이전 던전의 값이 섞인 상태가 남지 않는다.
        /// </summary>
        private void SelectDungeon(DungeonDefinition dungeon)
        {
            ClearMonsterPreviews();
            ClearRewardPreviews();

            selectedDungeon = dungeon != null && dungeon.IsValid ? dungeon : null;

            // 이전 항목의 선택 표시를 끄고 새 항목만 켠다.
            for (int i = 0; i < spawnedItems.Count; i++)
            {
                DungeonListItemView item = spawnedItems[i];
                if (item == null) continue;
                item.SetSelected(selectedDungeon != null && item.BoundDungeon == selectedDungeon);
            }

            bool hasSelection = selectedDungeon != null;
            if (detailRoot != null && detailRoot.activeSelf != hasSelection) detailRoot.SetActive(hasSelection);

            if (!hasSelection)
            {
                UnbindWorldText();
                if (worldText != null) worldText.text = string.Empty;
                UpdateEnterButton();
                return;
            }

            BindWorldText(selectedDungeon);
            BuildMonsterPreviews(selectedDungeon);
            BuildRewardPreviews(selectedDungeon);
            UpdateEnterButton();
        }

        // ---- 월드 문구 ----

        /// <summary>월드 문구를 <b>문구 틀 + 월드 이름</b> 두 참조의 조합으로 만든다. 둘 다 로컬라이징
        /// 대상이라 각각 구독하고, 어느 쪽이든 값이 들어올 때마다 다시 조립한다 - Locale을 바꾸면 두
        /// 값이 모두 새로 들어오므로 문구 전체가 그 언어로 바뀐다.</summary>
        private void BindWorldText(DungeonDefinition dungeon)
        {
            UnbindWorldText();

            if (worldText == null) return;

            // 같은 텍스트를 정적 키로 덮어쓰는 컴포넌트가 남아 있으면 실행 중에는 꺼 둔다.
            DungeonStaticLocalizerGuard.DisableIfPresent(worldText, nameof(DungeonPanel));

            bool hasFormat = worldTextFormat != null && worldTextFormat.HasReference;
            if (!hasFormat && !missingWorldFormatWarned)
            {
                missingWorldFormatWarned = true;
                Debug.LogWarning($"[DungeonPanel] '{name}': 월드 문구 틀에 Localization Table/Key가 지정되지 " +
                                 "않아 월드 문구를 비워 둡니다 - Inspector에서 Category와 Key를 지정하세요.", this);
            }

            // 월드 이름 문구는 던전이 아니라 월드 정의가 소유한다 - 던전에 월드가 연결되어 있지 않으면
            // 그릴 문구 자체가 없는 것이고, 여기서 대신 지어내지 않는다.
            WorldDefinition world = dungeon != null ? dungeon.World : null;
            LocalizedTextReference nameReference = world != null ? world.LocalizedName : null;
            bool hasName = nameReference != null && nameReference.HasReference;
            if (!hasName && !missingWorldNameWarned)
            {
                missingWorldNameWarned = true;
                string id = dungeon != null ? dungeon.DungeonId : "(없음)";
                string cause = world == null
                    ? "던전 에셋에 World Definition이 연결되지 않아"
                    : "월드 이름에 Localization Table/Key가 지정되지 않아";
                Debug.LogWarning($"[DungeonPanel] 던전 '{id}'의 {cause} 월드 문구를 비워 둡니다 - " +
                                 "던전 에셋의 World와 월드 에셋의 Category/Key를 확인하세요.", this);
            }

            if (!hasFormat || !hasName)
            {
                // 참조가 없으면 비워 둔다 - 한국어/영어 문자열을 코드에 적어 메우지 않는다.
                worldText.text = string.Empty;
                return;
            }

            boundWorldFormat = worldTextFormat;
            boundWorldName = nameReference;
            worldFormatText = null;
            worldNameText = null;

            // 구독 자체가 최초 로드를 유발하고, 이후 Locale이 바뀌면 자동으로 다시 호출된다.
            boundWorldFormat.StringChanged += ApplyWorldFormat;
            boundWorldName.StringChanged += ApplyWorldName;
        }

        private void UnbindWorldText()
        {
            if (boundWorldFormat != null)
            {
                boundWorldFormat.StringChanged -= ApplyWorldFormat;
                boundWorldFormat = null;
            }
            if (boundWorldName != null)
            {
                boundWorldName.StringChanged -= ApplyWorldName;
                boundWorldName = null;
            }

            worldFormatText = null;
            worldNameText = null;
        }

        private void ApplyWorldFormat(string localizedText)
        {
            worldFormatText = localizedText;
            ComposeWorldText();
        }

        private void ApplyWorldName(string localizedText)
        {
            worldNameText = localizedText;
            ComposeWorldText();
        }

        /// <summary>두 값이 모두 들어온 뒤에만 문구를 만든다 - 한쪽만 들어온 중간 상태를 화면에 내보내지
        /// 않기 위함이다. 자리표시자가 문구 틀과 맞지 않으면 예외를 밖으로 던지지 않고 틀을 그대로
        /// 표시하며, 원인은 로그로 한 번 남긴다.</summary>
        private void ComposeWorldText()
        {
            if (worldText == null) return;

            if (worldFormatText == null || worldNameText == null)
            {
                worldText.text = string.Empty;
                return;
            }

            try
            {
                worldText.text = string.Format(worldFormatText, worldNameText);
            }
            catch (FormatException e)
            {
                if (!worldFormatFailureLogged)
                {
                    worldFormatFailureLogged = true;
                    Debug.LogError($"[DungeonPanel] '{name}': 월드 문구를 월드 이름 하나로 포맷하지 못했습니다 - " +
                                   $"문구 틀을 그대로 표시합니다. 자리표시자는 {{0}} 하나여야 합니다: {e.Message}", this);
                }
                worldText.text = worldFormatText;
            }
        }

        // ---- 미리보기 ----

        private void BuildMonsterPreviews(DungeonDefinition dungeon)
        {
            RectTransform parent = ResolveMonsterPreviewParent();
            if (monsterPreviewTemplate == null || parent == null) return;

            // 던전 에셋에 적힌 순서 그대로 만든다 - MonsterDefinition.DisplayOrder로 다시 정렬하지 않는다.
            IReadOnlyList<MonsterDefinition> monsters = dungeon.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                MonsterDefinition monster = monsters[i];
                if (monster == null) continue;

                DungeonMonsterPreviewView view = Instantiate(monsterPreviewTemplate, parent, false);
                view.name = $"{monsterPreviewTemplate.name}_{i}";
                // 비활성 상태에서 먼저 표시할 값을 넣고 마지막에 켠다 - 켠 뒤에 넣으면 프리팹에 저장된
                // 이미지가 한 프레임 먼저 보인다.
                view.Bind(monster);
                view.gameObject.SetActive(true);
                spawnedMonsterPreviews.Add(view);
            }
        }

        private void ClearMonsterPreviews()
        {
            for (int i = 0; i < spawnedMonsterPreviews.Count; i++)
            {
                DungeonMonsterPreviewView view = spawnedMonsterPreviews[i];
                if (view == null) continue;

                view.Clear();
                // Destroy는 프레임 끝에 실행된다 - 선택을 옮긴 같은 프레임에 이전 던전의 빈 칸이 새 칸과
                // 함께 보이지 않도록 즉시 끈다.
                view.gameObject.SetActive(false);
                Destroy(view.gameObject);
            }
            spawnedMonsterPreviews.Clear();
        }

        private void BuildRewardPreviews(DungeonDefinition dungeon)
        {
            RectTransform parent = ResolveRewardPreviewParent();
            if (rewardPreviewTemplate == null || parent == null) return;

            IReadOnlyList<ItemDefinition> rewards = dungeon.RewardItems;
            for (int i = 0; i < rewards.Count; i++)
            {
                ItemDefinition reward = rewards[i];
                if (reward == null) continue;

                DungeonRewardPreviewView view = Instantiate(rewardPreviewTemplate, parent, false);
                view.name = $"{rewardPreviewTemplate.name}_{reward.ItemId}";
                // 비활성 상태에서 먼저 아이콘과 수량 숨김을 적용하고 마지막에 켠다 - 켠 뒤에 적용하면
                // 프리팹의 아이콘과 lb_count가 한 프레임 먼저 보인다.
                view.Bind(reward);
                view.gameObject.SetActive(true);
                spawnedRewardPreviews.Add(view);
            }
        }

        private void ClearRewardPreviews()
        {
            for (int i = 0; i < spawnedRewardPreviews.Count; i++)
            {
                DungeonRewardPreviewView view = spawnedRewardPreviews[i];
                if (view == null) continue;

                view.Clear();
                // Destroy는 프레임 끝에 실행된다 - 선택을 옮긴 같은 프레임에 이전 던전의 빈 칸이 새 칸과
                // 함께 보이지 않도록 즉시 끈다.
                view.gameObject.SetActive(false);
                Destroy(view.gameObject);
            }
            spawnedRewardPreviews.Clear();
        }

        // ---- 입장 ----

        /// <summary>
        /// 입장 요청을 <b>한 번만</b> 보낸다. 유효성을 확인한 뒤 <b>요청을 보내기 전에</b> 먼저 요청
        /// 상태를 세우고 버튼을 잠근다 - 요청 처리 도중 구독자가 같은 프레임에 이 버튼을 다시 누르게
        /// 만들거나(동기 재진입) 연타가 들어와도 두 번째 호출이 첫 줄에서 막힌다.
        ///
        /// 요청이 거부되면 세워 둔 상태를 <b>되돌리고</b> 버튼을 다시 판정한다 - 거부는 패널을 닫지
        /// 않는 경로이므로, 되돌리지 않으면 열려 있는 패널의 입장 버튼이 영영 잠긴 채로 남는다.
        /// 왜 거부됐는지는 요청 통로가 로그로 남긴다.
        /// </summary>
        private void HandleEnterClicked()
        {
            if (enterRequestSent) return;

            if (selectedDungeon == null || !selectedDungeon.IsValid)
            {
                UpdateEnterButton();
                return;
            }

            enterRequestSent = true;
            if (enterButton != null) enterButton.interactable = false;

            if (!DungeonEntryService.RequestEnterDungeon(selectedDungeon))
            {
                enterRequestSent = false;
                UpdateEnterButton();
                return;
            }

            Close();
        }

        /// <summary>입장 버튼은 "지금 이 선택으로 입장 요청이 실제로 나갈 수 있을 때"만 켠다.</summary>
        private void UpdateEnterButton()
        {
            if (enterButton == null) return;

            enterButton.interactable = !enterRequestSent
                                       && selectedDungeon != null
                                       && selectedDungeon.IsValid;
        }

        // ---- 참조 ----

        /// <summary>복제본을 넣을 부모를 정한다. Inspector에 Content가 연결되어 있으면 그것을,
        /// 없으면 원본(템플릿)의 부모를 <b>읽어서</b> 쓴다 - 어느 쪽도 값을 다시 써넣지 않으므로
        /// ScrollRect/Content 설정은 에디터가 정한 그대로 유지된다.</summary>
        private RectTransform ResolveDungeonListParent()
        {
            if (dungeonListContent != null) return dungeonListContent;
            return dungeonListItemTemplate != null ? dungeonListItemTemplate.transform.parent as RectTransform : null;
        }

        private RectTransform ResolveMonsterPreviewParent()
        {
            if (monsterPreviewContent != null) return monsterPreviewContent;
            return monsterPreviewTemplate != null ? monsterPreviewTemplate.transform.parent as RectTransform : null;
        }

        private RectTransform ResolveRewardPreviewParent()
        {
            if (rewardPreviewContent != null) return rewardPreviewContent;
            return rewardPreviewTemplate != null ? rewardPreviewTemplate.transform.parent as RectTransform : null;
        }

        /// <summary>빠진 참조를 자동으로 채우지 않고 무엇이 빠졌는지만 알린다 - 패널을 처음 열 때 한 번만
        /// 검사한다. 구조를 런타임에 만들어 버리면 에디터에서 보이는 계층과 실제 동작이 달라지므로,
        /// 여기서는 진단만 한다.</summary>
        private void ValidateReferences()
        {
            if (referencesValidated) return;
            referencesValidated = true;

            if (catalog == null)
            {
                Debug.LogError($"[DungeonPanel] '{name}': DungeonCatalog가 연결되지 않아 던전 목록을 만들 수 " +
                               "없습니다 - Inspector에 카탈로그 에셋을 연결하세요.", this);
            }

            if (dungeonListItemTemplate == null)
            {
                Debug.LogError($"[DungeonPanel] '{name}': 던전 목록 항목 원본(item_dungeonList)이 연결되지 " +
                               "않았습니다.", this);
            }
            else if (ResolveDungeonListParent() == null)
            {
                Debug.LogError($"[DungeonPanel] '{name}': 던전 목록 항목을 넣을 Content를 찾을 수 없습니다 - " +
                               "Content를 연결하거나 원본을 Content 아래에 두세요.", this);
            }

            if (monsterPreviewTemplate == null)
            {
                Debug.LogWarning($"[DungeonPanel] '{name}': 몬스터 미리보기 원본(item_monster)이 연결되지 " +
                                 "않아 몬스터 칸이 표시되지 않습니다.", this);
            }
            else if (ResolveMonsterPreviewParent() == null)
            {
                Debug.LogWarning($"[DungeonPanel] '{name}': 몬스터 미리보기를 넣을 Content를 찾을 수 없습니다.", this);
            }

            if (rewardPreviewTemplate == null)
            {
                Debug.LogWarning($"[DungeonPanel] '{name}': 보상 미리보기 원본(item_item)이 연결되지 않아 " +
                                 "보상 칸이 표시되지 않습니다.", this);
            }
            else if (ResolveRewardPreviewParent() == null)
            {
                Debug.LogWarning($"[DungeonPanel] '{name}': 보상 미리보기를 넣을 Content를 찾을 수 없습니다.", this);
            }

            if (detailRoot == null)
            {
                Debug.LogWarning($"[DungeonPanel] '{name}': 상세 루트(bg_description)가 연결되지 않아 " +
                                 "목록이 비어도 상세 영역이 그대로 보입니다.", this);
            }

            if (worldText == null)
            {
                Debug.LogWarning($"[DungeonPanel] '{name}': 월드 텍스트(top_description/lb)가 연결되지 않아 " +
                                 "월드 문구가 표시되지 않습니다.", this);
            }

            if (enterButton == null)
            {
                Debug.LogError($"[DungeonPanel] '{name}': 입장 버튼(btn_Enter)이 연결되지 않았습니다.", this);
            }
        }
    }
}
