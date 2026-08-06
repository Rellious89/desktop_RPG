using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Character;
using Common;
using Enemy;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace CharacterEditor
{
    /// <summary>
    /// Assets/Art 아래 폴더를 작업 목록으로 사용하는 KeyBuddy 통합 모션 제작 도구.
    /// 프로필이 없는 폴더는 규칙화된 하위 폴더(idle, idle_*, attack*, hit, defeat)를 읽어
    /// Assets/Data/MotionProfiles 아래에 초기 프로필과 공격 에셋을 자동 생성한다.
    /// </summary>
    public class MotionEditorWindow : EditorWindow
    {
        private const string CharacterArtRoot = "Assets/Art/Character";
        private const string MonsterArtRoot = "Assets/Art/Monster";
        private const string LegacyEnemyArtRoot = "Assets/Art/Enemy";
        private const string ProfileDataRoot = "Assets/Data/MotionProfiles";
        private const string StageLayoutAssetPath = ProfileDataRoot + "/CombatStageLayout.asset";

        private const float LeftWorkspaceWidth = 680f;
        private const float LibraryWidth = 245f;
        private const float NavigationWidth = LeftWorkspaceWidth - LibraryWidth - 8f;
        private const float StageWidth = LeftWorkspaceWidth - 18f;
        private const float StageHeight = 360f;
        private const float GroundRatio = 0.82f;
        private const float ZoomMin = 0.1f;
        private const float ZoomMax = 8f;
        private const float DefaultZoom = 0.5f;
        private const float FitMargin = 12f;
        private const float PreviewControlSpacing = 5f;
        private const float TimelineSliderWidth = 276f;
        private const string MotionNameControlName = "MotionEditorMotionName";
        private const string DescriptionControlName = "MotionEditorDescription";
        private static readonly Color ActiveTextFieldTint = new Color(0.68f, 0.84f, 1f, 0.8f);
        private static readonly Color ActiveTextFieldBorder = new Color(0.32f, 0.68f, 1f, 0.95f);
        // 되돌리기 어려운 동작(애니메이션 등록 삭제)만 쓰는 색 - 일반 편집 버튼과 눈에 띄게 구분한다.
        private static readonly Color DangerButtonColor = new Color(1f, 0.44f, 0.38f);
        private const float DeleteButtonWidth = 46f;

        /// <summary>Fast/Slow Input Simulation의 입력 간격(초) - 각각 "연타"와 "천천히 두드리기"에 해당한다.</summary>
        private const double FastInputInterval = 0.06d;
        private const double SlowInputInterval = 0.4d;

        private enum ActorKind { Character, Monster }

        /// <summary>누적 입력 시뮬레이션의 상태. 런타임 AttackPhase(None/Charging/Recovery)와 1:1 대응한다.</summary>
        private enum ChargeSimPhase { Idle, Charging, Recovery }
        private enum Workspace { Overview, Idle, IdleEvents, Attack, Movement, Hit, Defeat }
        private enum PreviewMotionKind { Idle, IdleEvent, Attack, Hit, Defeat }
        private enum HistoryAction { None, Undo, Redo }

        private sealed class ResourceEntry
        {
            public ActorKind Kind;
            public string Name;
            public string FolderPath;
            public string DataFolderPath;
            public CharacterMotionProfile CharacterProfile;
            public MonsterMotionProfile MonsterProfile;

            public bool HasProfile => CharacterProfile != null || MonsterProfile != null;
            public UnityEngine.Object ProfileObject => CharacterProfile != null
                ? (UnityEngine.Object)CharacterProfile
                : MonsterProfile;
        }

        private sealed class PreviewMotion
        {
            public string Label;
            public PreviewMotionKind Kind;
            public Sprite[] Frames = Array.Empty<Sprite>();
            public float Fps = 6f;
            public int HitFrame = -1;
            public AttackMotionDefinition Attack;

            public float Duration => Frames.Length > 0 && Fps > 0f ? Frames.Length / Fps : 0f;
        }

        private sealed class ClipSnapshot
        {
            public string Slot;
            public string DisplayName;
            public string Description;
            public float Fps;
            public Sprite[] Frames;
        }

        private sealed class ProfileSnapshot
        {
            public readonly List<ClipSnapshot> Clips = new List<ClipSnapshot>();
        }

        private sealed class AttackSnapshot
        {
            public string DisplayName;
            public string Description;
            public float Fps;
            public int HitFrame;
            public Sprite[] Frames;
            public Sprite[] OverlayFrames;
            public GameObject ProjectilePrefab;
            public Vector2 ProjectileLaunchOffset;
            public float ProjectileScale;
        }

        [MenuItem("Tools/KeyBuddy/Motion Editor")]
        private static void Open()
        {
            var window = GetWindow<MotionEditorWindow>("Motion Editor");
            window.minSize = new Vector2(1180f, 700f);
        }

        private readonly List<ResourceEntry> resources = new List<ResourceEntry>();
        private readonly List<ResourceEntry> previewCharacters = new List<ResourceEntry>();
        private readonly List<ResourceEntry> previewMonsters = new List<ResourceEntry>();
        private readonly List<Sprite> rawIdlePreviewFrames = new List<Sprite>();
        private readonly Dictionary<int, ProfileSnapshot> savedProfileSnapshots = new Dictionary<int, ProfileSnapshot>();
        private readonly Dictionary<int, AttackSnapshot> savedAttackSnapshots = new Dictionary<int, AttackSnapshot>();
        private ActorKind actorKind;
        private Workspace workspace = Workspace.Overview;
        private int selectedResourceIndex = -1;
        private int selectedIdleEventIndex = -1;
        private int selectedPreviewTargetIndex;
        private int selectedOpponentMotionIndex;
        private int activeTier = 1;

        /// <summary>오버레이 Drop Zone의 동작. 기본은 Replace(드롭한 스프라이트로 배열 통째 교체)이고,
        /// 켜면 기존 배열 뒤에 덧붙인다.</summary>
        private bool overlayDropAppends;

        /// <summary>Attack Editor의 Input Response 영역 펼침 상태(기본 펼침).</summary>
        private bool inputResponseExpanded = true;

        private SerializedObject activeProfileObject;
        private AttackMotionDefinition selectedAttack;
        private SerializedObject attackObject;
        private ComboTierAttackPool activePool;
        private SerializedObject poolObject;
        private ReorderableList frameList;
        private SerializedObject frameListOwner;
        private string frameListPropertyPath;

        private CombatStageLayout stageLayout;
        private SerializedObject stageLayoutObject;

        /// <summary>개발용 진단 토글 - Damage Number Preview가 왜 안 보이는지(조건 미충족 vs 좌표/스타일
        /// 문제) 바로 구분할 수 있게 활성 상태·elapsed/duration·anchor 좌표를 화면에 함께 표시한다.
        /// 기본은 꺼짐이고, Zoom 옆의 체크박스로 켤 수 있다.</summary>
        private bool debugDamageNumberOverlay;

        private bool previewPlaying;
        private bool previewLoop = true;
        private int previewFrameIndex;
        private double previewLastStepTime;
        private double previewElapsedTime;
        private float previewZoom = DefaultZoom;
        private bool previewCastCueFired;
        private bool previewHitCueFired;

        // ---- Accumulated Input 입력 시뮬레이션 ----
        // 누적 입력 공격은 그냥 자동 재생만 봐서는 설정을 확인할 수 없다(프레임이 시간이 아니라 입력으로
        // 진행하므로). 런타임 PlayerCharacterAnimator와 같은 규칙으로 충전/유예/감쇠/이월을 흉내 내고,
        // 그 결과 프레임을 previewElapsedTime(= frame / fps)으로 환산해 기존 Preview 그리기 경로에
        // 그대로 태운다 - 발사체/피격 반응/Cast·Hit 사운드 판정이 자동 재생과 완전히 같은 코드를 탄다.
        private bool chargeSimRunning;
        private ChargeSimPhase chargeSimPhase;
        private float chargeSimInputs;
        private float chargeSimCarried;
        private int chargeSimStrikes;
        private double chargeSimLastInputTime;
        private double chargeSimLastStepTime;
        private double chargeSimAutoInterval;      // 0이면 자동 입력 없음(수동 또는 Stop Input 상태)
        private double chargeSimResumeInterval = SlowInputInterval;
        private double chargeSimNextAutoInput;
        private float chargeSimRecoveryTimer;

        private Vector2 libraryScroll;
        private Vector2 navigationScroll;
        private Vector2 inspectorScroll;
        private Vector2 characterMotionScroll;
        private Vector2 monsterMotionScroll;
        private Vector2 targetDropdownScroll;
        private Vector2 descriptionScroll;
        private bool pointerDownStartedWithTextFocus;
        private bool pointerDownInsideTextInput;
        private bool targetDropdownOpen;
        private bool pendingUndoRedoRefresh;
        private bool pendingMotionDelete;
        private HistoryAction pendingHistoryAction;
        private GUIStyle hitLabelStyle;
        private GUIStyle hitTagStyle;
        private GUIStyle castTagStyle;
        private GUIStyle castHitTagStyle;
        private GUIStyle damageNumberPreviewStyle;
        private GUIStyle toolbarStatusStyle;

        private ResourceEntry SelectedResource => selectedResourceIndex >= 0 && selectedResourceIndex < resources.Count
            ? resources[selectedResourceIndex]
            : null;

        /// <summary>캐릭터/몬스터 프로필 하나가 아니라 프로젝트 전체가 공유하는 단일 배치 에셋이다 -
        /// 없으면(최초 실행 등) 자동으로 만들어서 항상 non-null을 보장한다.</summary>
        private CombatStageLayout GetStageLayout()
        {
            if (stageLayout != null) return stageLayout;

            stageLayout = AssetDatabase.LoadAssetAtPath<CombatStageLayout>(StageLayoutAssetPath);
            if (stageLayout == null)
            {
                stageLayout = CreateInstance<CombatStageLayout>();
                AssetDatabase.CreateAsset(stageLayout, StageLayoutAssetPath);
                AssetDatabase.SaveAssets();
            }
            return stageLayout;
        }

        private SerializedObject GetStageLayoutObject()
        {
            GetStageLayout();
            if (stageLayoutObject == null || stageLayoutObject.targetObject != stageLayout)
            {
                stageLayoutObject = new SerializedObject(stageLayout);
            }
            return stageLayoutObject;
        }

        /// <summary>열려 있는 씬(들)에서 PlayerCharacterAnimator/TargetCombatController를 전부 찾아
        /// (비활성 오브젝트 포함 - CharacterRoster가 꺼둔 대기 캐릭터도 맞춰야 한다) 각자의
        /// 연결된 Motion Profile + 공용 CombatStageLayout 기준으로 위치/스케일/Flip을 다시 계산해서
        /// 그 자리에서 Transform에 적용한다. Preview와 완전히 같은 공식(Slot + Actor Offset)을 쓴다.
        /// Undo에 등록하고 Scene을 Dirty 처리한다 - 자동 실행이 아니라 이 버튼을 눌렀을 때만 동작한다.</summary>
        private void ApplyPreviewLayoutToOpenStage()
        {
            CombatStageLayout layout = GetStageLayout();
            var characterAnimators = UnityEngine.Object.FindObjectsOfType<PlayerCharacterAnimator>(true);
            var monsterControllers = UnityEngine.Object.FindObjectsOfType<TargetCombatController>(true);

            if (characterAnimators.Length == 0 && monsterControllers.Length == 0)
            {
                EditorUtility.DisplayDialog("Apply Preview Layout",
                    "열려 있는 씬에서 캐릭터(PlayerCharacterAnimator)나 몬스터(TargetCombatController)를 찾지 못했습니다.",
                    "확인");
                return;
            }

            Undo.SetCurrentGroupName("Apply Preview Layout to Stage");
            int undoGroup = Undo.GetCurrentGroup();
            var dirtyScenes = new HashSet<Scene>();

            foreach (PlayerCharacterAnimator animator in characterAnimators)
            {
                Transform actorTransform = animator.transform;
                CharacterMotionProfile profile = animator.MotionProfile;
                Vector2 offset = profile != null ? profile.Preview.ActorOffset : Vector2.zero;
                float scale = profile != null ? profile.Preview.ActorScale : 1f;
                Vector3 localPos = new Vector3(layout.CharacterSlotPosition.x + offset.x, layout.CharacterSlotPosition.y + offset.y, actorTransform.localPosition.z);

                Undo.RecordObject(actorTransform, "Apply Preview Layout to Stage");
                AttackMovement movement = animator.GetComponent<AttackMovement>();
                if (movement != null) movement.SetPresentationBasePosition(localPos);
                else actorTransform.localPosition = localPos;
                actorTransform.localScale = new Vector3(scale, scale, 1f);
                EditorUtility.SetDirty(actorTransform);

                dirtyScenes.Add(actorTransform.gameObject.scene);
            }

            foreach (TargetCombatController combat in monsterControllers)
            {
                Transform actorTransform = combat.transform;
                MonsterMotionProfile profile = combat.MotionProfile;
                Vector2 offset = profile != null ? profile.Preview.ActorOffset : Vector2.zero;
                float scale = profile != null ? profile.Preview.ActorScale : 1f;
                bool flipX = profile != null && profile.SpriteFlipX;
                Vector3 localPos = new Vector3(layout.MonsterSlotPosition.x + offset.x, layout.MonsterSlotPosition.y + offset.y, actorTransform.localPosition.z);

                Undo.RecordObject(actorTransform, "Apply Preview Layout to Stage");
                combat.SetPresentationBasePosition(localPos);
                actorTransform.localScale = new Vector3(scale, scale, 1f);
                EditorUtility.SetDirty(actorTransform);

                SpriteRenderer spriteRenderer = combat.GetComponent<SpriteRenderer>();
                if (spriteRenderer != null)
                {
                    Undo.RecordObject(spriteRenderer, "Apply Preview Layout to Stage");
                    spriteRenderer.flipX = flipX;
                    EditorUtility.SetDirty(spriteRenderer);
                }

                dirtyScenes.Add(actorTransform.gameObject.scene);
            }

            Undo.CollapseUndoOperations(undoGroup);
            foreach (Scene scene in dirtyScenes)
            {
                if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        private GUIStyle HitLabelStyle
        {
            get
            {
                if (hitLabelStyle == null)
                {
                    hitLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.UpperCenter,
                        normal = { textColor = new Color(1f, 0.32f, 0.22f) }
                    };
                }
                return hitLabelStyle;
            }
        }

        private GUIStyle HitTagStyle
        {
            get
            {
                if (hitTagStyle == null)
                {
                    hitTagStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = new Color(1f, 0.3f, 0.22f) }
                    };
                }
                return hitTagStyle;
            }
        }

        private GUIStyle CastTagStyle
        {
            get
            {
                if (castTagStyle == null)
                {
                    castTagStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = new Color(0.35f, 0.65f, 1f) }
                    };
                }
                return castTagStyle;
            }
        }

        private GUIStyle CastHitTagStyle
        {
            get
            {
                if (castHitTagStyle == null)
                {
                    castHitTagStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = new Color(1f, 0.75f, 0.15f) }
                    };
                }
                return castHitTagStyle;
            }
        }

        /// <summary>DamageNumberPopup의 실제 런타임 스타일(중앙 정렬)을 흉내낸다 - TMP가 아니라
        /// IMGUI라 폰트가 완전히 같지는 않다. 색상/크기는 매 호출마다 Monster Profile 값으로 덮어써야
        /// 하므로(몬스터마다 다르고, 페이드에 따라 알파도 매 프레임 바뀐다) 여기서는 골격만 한 번
        /// 만들어두고 캐싱한다.</summary>
        private GUIStyle DamageNumberPreviewStyle
        {
            get
            {
                if (damageNumberPreviewStyle == null)
                {
                    damageNumberPreviewStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleCenter
                    };
                }
                return damageNumberPreviewStyle;
            }
        }

        private GUIStyle ToolbarStatusStyle
        {
            get
            {
                if (toolbarStatusStyle == null)
                {
                    toolbarStatusStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter
                    };
                }
                return toolbarStatusStyle;
            }
        }

        private void OnEnable()
        {
            ScanArtFolders();
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        private void OnGUI()
        {
            BeginTextFocusPointerHandling();
            HandlePreviewShortcuts();
            DrawToolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(LeftWorkspaceWidth)))
                {
                    using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
                    {
                        DrawResourceLibrary();
                        DrawNavigation();
                    }
                    DrawPersistentPreview();
                }
                DrawInspector();
            }
            EndTextFocusPointerHandling();
        }

        private void HandlePreviewShortcuts()
        {
            Event evt = Event.current;
            if (evt.type != EventType.KeyDown || IsTextInputFocused()) return;

            bool handled = true;
            if (evt.keyCode == KeyCode.Space)
            {
                if (evt.shift) StopPreview();
                else TogglePreviewPlayback();
            }
            else if (evt.keyCode == KeyCode.LeftArrow)
            {
                MovePreviewTimeline(evt.shift ? int.MinValue : -1);
            }
            else if (evt.keyCode == KeyCode.RightArrow)
            {
                MovePreviewTimeline(evt.shift ? int.MaxValue : 1);
            }
            else if (evt.keyCode == KeyCode.X && !evt.command && !evt.control && !evt.alt)
            {
                previewLoop = !previewLoop;
            }
            else
            {
                handled = false;
            }

            if (!handled) return;
            evt.Use();
            Repaint();
        }

        private static bool IsTextInputFocused()
        {
            if (EditorGUIUtility.editingTextField) return true;
            string focused = GUI.GetNameOfFocusedControl();
            return focused == MotionNameControlName || focused == DescriptionControlName;
        }

        private static bool IsFocusedControl(string controlName)
        {
            return GUI.GetNameOfFocusedControl() == controlName;
        }

        private void BeginTextFocusPointerHandling()
        {
            pointerDownInsideTextInput = false;
            pointerDownStartedWithTextFocus = Event.current.rawType == EventType.MouseDown && IsTextInputFocused();
        }

        private void RegisterTextInputPointerDown(Rect rect)
        {
            if (Event.current.rawType == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                pointerDownInsideTextInput = true;
        }

        private void EndTextFocusPointerHandling()
        {
            if (!pointerDownStartedWithTextFocus) return;
            bool textControlReceivedClick = pointerDownInsideTextInput
                || GUIUtility.hotControl != 0 && GUIUtility.hotControl == GUIUtility.keyboardControl;
            if (textControlReceivedClick) return;
            GUI.FocusControl(null);
            EditorGUIUtility.editingTextField = false;
        }

        private void MovePreviewTimeline(int movement)
        {
            PreviewMotion main = GetMainPreviewMotion();
            PreviewMotion opponent = GetOpponentPreviewMotion();
            PreviewMotion driver = GetTimelineDriver(main, opponent);
            if (main == null || driver == null) return;

            float fps = Mathf.Max(0.01f, driver.Fps);
            int frameCount = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.001f, GetPreviewDuration()) * fps));
            int current = Mathf.Clamp(Mathf.RoundToInt((float)previewElapsedTime * fps), 0, frameCount - 1);
            int target = movement == int.MinValue ? 0
                : movement == int.MaxValue ? frameCount - 1
                : (current + movement + frameCount) % frameCount;
            SetPreviewTimelineFrame(target, fps, main);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool characterSelected = actorKind == ActorKind.Character;
                if (GUILayout.Toggle(characterSelected, "Characters", EditorStyles.toolbarButton, GUILayout.Width(92f)) && !characterSelected)
                {
                    ChangeActorKind(ActorKind.Character);
                }
                if (GUILayout.Toggle(!characterSelected, "Monsters", EditorStyles.toolbarButton, GUILayout.Width(92f)) && characterSelected)
                {
                    ChangeActorKind(ActorKind.Monster);
                }

                GUILayout.Space(8f);
                if (GUILayout.Button("Rescan Art", EditorStyles.toolbarButton, GUILayout.Width(88f))) ScanArtFolders();

                using (new EditorGUI.DisabledScope(SelectedResource == null || !SelectedResource.HasProfile))
                {
                    if (GUILayout.Button("Sync Frames", EditorStyles.toolbarButton, GUILayout.Width(88f))) SyncActiveFramesFromArt();
                }

                GUILayout.Space(8f);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
                {
                    var applyLayoutContent = new GUIContent("Apply Preview Layout",
                        "현재 저장된 Presentation/Layout 값(Actor Offset/Scale/Flip, Combat Stage Layout)을 열려 있는 " +
                        "씬의 캐릭터·몬스터 Transform에 즉시 적용합니다(Undo 가능). Play Mode에서는 런타임 초기화가 " +
                        "같은 규칙을 자동 적용하므로 이 버튼은 에디트 모드에서만 동작합니다.");
                    if (GUILayout.Button(applyLayoutContent, EditorStyles.toolbarButton, GUILayout.Width(150f)))
                    {
                        ApplyPreviewLayoutToOpenStage();
                    }
                }

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(SelectedResource == null || !SelectedResource.HasProfile))
                {
                    if (GUILayout.Button(new GUIContent("Undo", "마지막 변경을 되돌립니다."), EditorStyles.toolbarButton, GUILayout.Width(44f))) QueueHistoryAction(HistoryAction.Undo);
                    if (GUILayout.Button(new GUIContent("Redo", "되돌린 변경을 다시 적용합니다."), EditorStyles.toolbarButton, GUILayout.Width(44f))) QueueHistoryAction(HistoryAction.Redo);
                }

                if (SelectedResource != null)
                {
                    bool unsaved = IsCurrentSelectionDirty();
                    string status = SelectedResource.HasProfile ? (unsaved ? "● Unsaved" : "Saved") : "Profile not created";
                    Color previousTextColor = GUI.contentColor;
                    if (unsaved) GUI.contentColor = new Color(1f, 0.84f, 0.18f);
                    GUILayout.Label($"{SelectedResource.Name}  |  {status}", ToolbarStatusStyle,
                        GUILayout.Width(220f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    GUI.contentColor = previousTextColor;

                    using (new EditorGUI.DisabledScope(!SelectedResource.HasProfile))
                    {
                        Color previousButtonColor = GUI.backgroundColor;
                        if (unsaved) GUI.backgroundColor = new Color(1f, 0.74f, 0.16f);
                        if (GUILayout.Button("Save Profile", EditorStyles.toolbarButton, GUILayout.Width(92f))) SaveActiveProfile();
                        GUI.backgroundColor = previousButtonColor;
                    }
                }
            }
        }

        private bool IsCurrentSelectionDirty()
        {
            ResourceEntry entry = SelectedResource;
            if (entry?.ProfileObject != null && EditorUtility.IsDirty(entry.ProfileObject)) return true;
            if (entry?.CharacterProfile == null) return false;
            foreach (ComboTierAttackPool pool in GetProfilePools(entry.CharacterProfile))
            {
                if (pool != null && EditorUtility.IsDirty(pool)) return true;
                if (pool == null) continue;
                foreach (AttackMotionDefinition attack in pool.Motions)
                    if (attack != null && EditorUtility.IsDirty(attack)) return true;
            }
            return false;
        }

        private static IEnumerable<ComboTierAttackPool> GetProfilePools(CharacterMotionProfile profile)
        {
            if (profile == null) yield break;
            yield return profile.Tier1Pool;
            yield return profile.Tier2Pool;
            yield return profile.Tier3Pool;
        }

        private static IEnumerable<AttackMotionDefinition> GetProfileAttacks(CharacterMotionProfile profile)
        {
            var seen = new HashSet<AttackMotionDefinition>();
            foreach (ComboTierAttackPool pool in GetProfilePools(profile))
            {
                if (pool == null) continue;
                foreach (AttackMotionDefinition attack in pool.Motions)
                    if (attack != null && seen.Add(attack)) yield return attack;
            }
        }

        private void EnsureSavedSnapshots(ResourceEntry entry)
        {
            if (entry?.ProfileObject == null) return;
            int profileId = entry.ProfileObject.GetInstanceID();
            if (!savedProfileSnapshots.ContainsKey(profileId)) savedProfileSnapshots[profileId] = CaptureProfileSnapshot(entry);
            if (entry.CharacterProfile == null) return;
            foreach (AttackMotionDefinition attack in GetProfileAttacks(entry.CharacterProfile))
            {
                int attackId = attack.GetInstanceID();
                if (!savedAttackSnapshots.ContainsKey(attackId)) savedAttackSnapshots[attackId] = CaptureAttackSnapshot(attack);
            }
        }

        private void CaptureSavedSnapshots(ResourceEntry entry)
        {
            if (entry?.ProfileObject == null) return;
            savedProfileSnapshots[entry.ProfileObject.GetInstanceID()] = CaptureProfileSnapshot(entry);
            if (entry.CharacterProfile == null) return;
            foreach (AttackMotionDefinition attack in GetProfileAttacks(entry.CharacterProfile))
                savedAttackSnapshots[attack.GetInstanceID()] = CaptureAttackSnapshot(attack);
        }

        private static ProfileSnapshot CaptureProfileSnapshot(ResourceEntry entry)
        {
            var snapshot = new ProfileSnapshot();
            var serialized = new SerializedObject(entry.ProfileObject);
            serialized.Update();
            snapshot.Clips.Add(CaptureClipSnapshot("기본 아이들", serialized.FindProperty("baseIdle")));
            SerializedProperty events = serialized.FindProperty("idleEvents");
            for (int i = 0; i < events.arraySize; i++)
                snapshot.Clips.Add(CaptureClipSnapshot($"아이들 이벤트 {i + 1}", events.GetArrayElementAtIndex(i)));
            if (entry.Kind == ActorKind.Monster)
            {
                snapshot.Clips.Add(CaptureClipSnapshot("피격", serialized.FindProperty("hit")));
                snapshot.Clips.Add(CaptureClipSnapshot("패배", serialized.FindProperty("defeat")));
            }
            return snapshot;
        }

        private static ClipSnapshot CaptureClipSnapshot(string slot, SerializedProperty clip)
        {
            SerializedProperty frames = clip.FindPropertyRelative("frames");
            return new ClipSnapshot
            {
                Slot = slot,
                DisplayName = clip.FindPropertyRelative("displayName").stringValue,
                Description = clip.FindPropertyRelative("editorDescription").stringValue,
                Fps = clip.FindPropertyRelative("animationFps").floatValue,
                Frames = ReadSpriteArray(frames)
            };
        }

        private static AttackSnapshot CaptureAttackSnapshot(AttackMotionDefinition attack)
        {
            var serialized = new SerializedObject(attack);
            serialized.Update();
            return new AttackSnapshot
            {
                DisplayName = attack.name,
                Description = serialized.FindProperty("editorDescription").stringValue,
                Fps = serialized.FindProperty("animationFps").floatValue,
                HitFrame = serialized.FindProperty("hitFrameIndex").intValue,
                Frames = ReadSpriteArray(serialized.FindProperty("frames")),
                OverlayFrames = ReadSpriteArray(serialized.FindProperty("overlayFrames")),
                ProjectilePrefab = serialized.FindProperty("projectilePrefab").objectReferenceValue as GameObject,
                ProjectileLaunchOffset = serialized.FindProperty("projectileLaunchOffset").vector2Value,
                ProjectileScale = serialized.FindProperty("projectileScale").floatValue
            };
        }

        private List<string> BuildUnsavedChanges(ResourceEntry entry)
        {
            var changes = new List<string>();
            if (entry?.ProfileObject == null || !IsCurrentSelectionDirty()) return changes;
            EnsureSavedSnapshots(entry);

            ProfileSnapshot savedProfile = savedProfileSnapshots[entry.ProfileObject.GetInstanceID()];
            ProfileSnapshot currentProfile = CaptureProfileSnapshot(entry);
            bool profileDetailsChanged = false;
            int clipCount = Mathf.Max(savedProfile.Clips.Count, currentProfile.Clips.Count);
            for (int i = 0; i < clipCount; i++)
            {
                ClipSnapshot before = i < savedProfile.Clips.Count ? savedProfile.Clips[i] : null;
                ClipSnapshot after = i < currentProfile.Clips.Count ? currentProfile.Clips[i] : null;
                profileDetailsChanged |= AppendClipChanges(changes, before, after);
            }
            if (EditorUtility.IsDirty(entry.ProfileObject) && !profileDetailsChanged)
                changes.Add("프로필 설정 변경");

            if (entry.CharacterProfile != null)
            {
                bool attackDetailsChanged = false;
                foreach (AttackMotionDefinition attack in GetProfileAttacks(entry.CharacterProfile))
                {
                    AttackSnapshot before;
                    if (!savedAttackSnapshots.TryGetValue(attack.GetInstanceID(), out before))
                    {
                        changes.Add($"{attack.name}: 공격 모션 추가");
                        attackDetailsChanged = true;
                        continue;
                    }
                    bool changed = AppendAttackChanges(changes, before, CaptureAttackSnapshot(attack));
                    if (EditorUtility.IsDirty(attack) && !changed) changes.Add($"{attack.name}: 공격 설정 변경");
                    attackDetailsChanged |= changed;
                }
                foreach (ComboTierAttackPool pool in GetProfilePools(entry.CharacterProfile))
                    if (pool != null && EditorUtility.IsDirty(pool) && !attackDetailsChanged) changes.Add("공격 목록 변경");
            }
            return changes;
        }

        private static bool AppendClipChanges(List<string> changes, ClipSnapshot before, ClipSnapshot after)
        {
            if (before == null && after == null) return false;
            string slot = after != null ? after.Slot : before.Slot;
            if (before == null) { changes.Add($"{slot}: 모션 추가"); return true; }
            if (after == null) { changes.Add($"{slot}: 모션 삭제"); return true; }
            bool changed = false;
            if (before.DisplayName != after.DisplayName) { changes.Add($"{slot}: 모션 이름 변경"); changed = true; }
            if (before.Description != after.Description) { changes.Add($"{slot}: 설명 변경"); changed = true; }
            if (!Mathf.Approximately(before.Fps, after.Fps)) { changes.Add($"{slot}: FPS 변경"); changed = true; }
            changed |= AppendFrameChanges(changes, slot, before.Frames, after.Frames);
            return changed;
        }

        private static bool AppendAttackChanges(List<string> changes, AttackSnapshot before, AttackSnapshot after)
        {
            string slot = after.DisplayName;
            bool changed = false;
            if (before.Description != after.Description) { changes.Add($"{slot}: 설명 변경"); changed = true; }
            if (!Mathf.Approximately(before.Fps, after.Fps)) { changes.Add($"{slot}: FPS 변경"); changed = true; }
            if (before.HitFrame != after.HitFrame) { changes.Add($"{slot}: 히트 프레임 변경"); changed = true; }
            // 두 배열을 모두 검사해야 한다 - 오버레이만 고친 뒤 리소스/탭을 옮겨도 경고가 떠야 하므로
            // 본체 프레임 변경 여부로 단축 평가하지 않는다.
            changed |= AppendFrameChanges(changes, slot, before.Frames, after.Frames);
            changed |= AppendFrameChanges(changes, slot, before.OverlayFrames, after.OverlayFrames, "오버레이");
            if (before.ProjectilePrefab != after.ProjectilePrefab) { changes.Add($"{slot}: 발사체 프리팹 변경"); changed = true; }
            if (before.ProjectileLaunchOffset != after.ProjectileLaunchOffset) { changes.Add($"{slot}: 발사 위치 변경"); changed = true; }
            if (!Mathf.Approximately(before.ProjectileScale, after.ProjectileScale)) { changes.Add($"{slot}: 발사체 크기 변경"); changed = true; }
            return changed;
        }

        private static bool AppendFrameChanges(List<string> changes, string slot, Sprite[] before, Sprite[] after, string frameLabel = "프레임")
        {
            if (before.Length != after.Length)
            {
                changes.Add($"{slot}: {frameLabel} 수 변경 ({before.Length} → {after.Length})");
                return true;
            }
            bool changed = false;
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] == after[i]) continue;
                changes.Add($"{slot}: {i + 1}번 {frameLabel} 스프라이트 변경");
                changed = true;
            }
            return changed;
        }

        private void OnUndoRedoPerformed()
        {
            if (pendingUndoRedoRefresh) return;
            pendingUndoRedoRefresh = true;
            EditorApplication.delayCall += RefreshAfterUndoRedo;
        }

        private void QueueHistoryAction(HistoryAction action)
        {
            if (pendingHistoryAction != HistoryAction.None) return;
            pendingHistoryAction = action;
            EditorApplication.delayCall += PerformQueuedHistoryAction;
        }

        private void PerformQueuedHistoryAction()
        {
            HistoryAction action = pendingHistoryAction;
            pendingHistoryAction = HistoryAction.None;
            if (this == null) return;
            if (action == HistoryAction.Undo) Undo.PerformUndo();
            else if (action == HistoryAction.Redo) Undo.PerformRedo();
        }

        private void RefreshAfterUndoRedo()
        {
            pendingUndoRedoRefresh = false;
            if (this == null) return;
            activeProfileObject?.Update();
            attackObject?.Update();
            poolObject?.Update();
            ValidateSelectionAfterHistoryChange();
            RebuildFrameList();
            RestartPreview();
            Repaint();
        }

        /// <summary>Undo/Redo로 목록 길이나 등록 내용이 달라졌을 수 있으므로 현재 선택을 다시 검증한다 -
        /// Idle Event 인덱스는 남은 목록 범위로 보정하고(목록이 비면 선택 해제), 선택된 공격이 지금 티어
        /// 풀에서 빠졌으면(삭제가 Redo로 다시 적용된 경우) 남아 있는 항목으로 선택을 옮긴다. 선택이 없던
        /// 상태(-1/null)를 임의로 채우지는 않는다.</summary>
        private void ValidateSelectionAfterHistoryChange()
        {
            if (activeProfileObject != null && selectedIdleEventIndex >= 0)
            {
                SerializedProperty events = activeProfileObject.FindProperty("idleEvents");
                if (events != null)
                {
                    selectedIdleEventIndex = events.arraySize == 0 ? -1 : Mathf.Min(selectedIdleEventIndex, events.arraySize - 1);
                }
            }

            CharacterMotionProfile profile = SelectedResource?.CharacterProfile;
            if (profile == null || selectedAttack == null) return;
            ComboTierAttackPool pool = GetPool(profile, activeTier);
            if (pool == null)
            {
                SelectAttack(null);
                return;
            }
            if (IndexOfMotion(pool, selectedAttack) >= 0) return;
            IReadOnlyList<AttackMotionDefinition> motions = pool.Motions;
            SelectAttack(motions.Count > 0 ? motions[0] : null);
        }

        private void ChangeActorKind(ActorKind kind)
        {
            actorKind = kind;
            workspace = Workspace.Overview;
            selectedResourceIndex = -1;
            selectedIdleEventIndex = -1;
            SelectAttack(null);
            ScanArtFolders();
        }

        private void ScanArtFolders()
        {
            string selectedPath = SelectedResource != null ? SelectedResource.FolderPath : null;
            resources.Clear();
            if (actorKind == ActorKind.Character)
            {
                AddResourceFolders(CharacterArtRoot, ActorKind.Character, resources);
            }
            else
            {
                AddResourceFolders(MonsterArtRoot, ActorKind.Monster, resources);
                AddResourceFolders(LegacyEnemyArtRoot, ActorKind.Monster, resources);
            }
            resources.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            previewCharacters.Clear();
            AddResourceFolders(CharacterArtRoot, ActorKind.Character, previewCharacters);
            previewCharacters.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            previewMonsters.Clear();
            AddResourceFolders(MonsterArtRoot, ActorKind.Monster, previewMonsters);
            AddResourceFolders(LegacyEnemyArtRoot, ActorKind.Monster, previewMonsters);
            previewMonsters.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            selectedPreviewTargetIndex = Mathf.Clamp(selectedPreviewTargetIndex, 0, Mathf.Max(0, GetPreviewTargets().Count - 1));

            selectedResourceIndex = resources.FindIndex(entry => entry.FolderPath == selectedPath);
            if (selectedResourceIndex < 0 && resources.Count > 0) selectedResourceIndex = 0;
            SelectResource(selectedResourceIndex);
        }

        private static void AddResourceFolders(string root, ActorKind kind, List<ResourceEntry> destination)
        {
            if (!AssetDatabase.IsValidFolder(root)) return;
            foreach (string folder in AssetDatabase.GetSubFolders(root))
            {
                string name = Path.GetFileName(folder);
                string typeFolder = kind == ActorKind.Character ? "Characters" : "Monsters";
                string dataFolder = $"{ProfileDataRoot}/{typeFolder}/{name}";
                var entry = new ResourceEntry
                {
                    Kind = kind,
                    Name = name,
                    FolderPath = folder,
                    DataFolderPath = dataFolder,
                };

                if (AssetDatabase.IsValidFolder(dataFolder))
                {
                    if (kind == ActorKind.Character) entry.CharacterProfile = FindFirstAsset<CharacterMotionProfile>(dataFolder);
                    else entry.MonsterProfile = FindFirstAsset<MonsterMotionProfile>(dataFolder);
                }
                destination.Add(entry);
            }
        }

        private static T FindFirstAsset<T>(string folder) where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { folder });
            return guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }

        private void DrawResourceLibrary()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(LibraryWidth)))
            {
                EditorGUILayout.LabelField(actorKind == ActorKind.Character ? "Character Folders" : "Monster Folders", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(actorKind == ActorKind.Character ? CharacterArtRoot : $"{MonsterArtRoot} / Enemy", EditorStyles.miniLabel);
                EditorGUILayout.Space(4f);
                libraryScroll = EditorGUILayout.BeginScrollView(libraryScroll);

                if (resources.Count == 0)
                {
                    EditorGUILayout.HelpBox("등록할 아트 폴더가 없습니다.", MessageType.Info);
                }

                for (int i = 0; i < resources.Count; i++)
                {
                    ResourceEntry entry = resources[i];
                    bool selected = i == selectedResourceIndex;
                    Color old = GUI.backgroundColor;
                    if (selected) GUI.backgroundColor = new Color(0.42f, 0.82f, 1f);
                    string state = entry.HasProfile ? "●" : "○";
                    if (GUILayout.Button($"{state}  {entry.Name}", EditorStyles.miniButton, GUILayout.Height(26f)))
                    {
                        selectedResourceIndex = i;
                        SelectResource(i);
                    }
                    GUI.backgroundColor = old;
                }
                EditorGUILayout.EndScrollView();
                EditorGUILayout.LabelField("● Profile ready   ○ Not created", EditorStyles.centeredGreyMiniLabel);
            }
        }

        private void SelectResource(int index)
        {
            previewPlaying = false;
            previewFrameIndex = 0;
            targetDropdownOpen = false;
            selectedIdleEventIndex = -1;
            SelectAttack(null);

            if (index < 0 || index >= resources.Count)
            {
                activeProfileObject = null;
                return;
            }

            ResourceEntry entry = resources[index];
            activeProfileObject = entry.ProfileObject != null ? new SerializedObject(entry.ProfileObject) : null;
            EnsureSavedSnapshots(entry);
            rawIdlePreviewFrames.Clear();
            rawIdlePreviewFrames.AddRange(LoadSprites(FindMotionFolder(entry.FolderPath, "idle")));
            // 대상을 고르는 즉시 기본 Idle을 프리뷰한다. 프로필이 아직 없어도 Art/idle 폴더를 사용한다.
            workspace = entry.HasProfile ? Workspace.Idle : Workspace.Overview;
            RebuildFrameList();
            SelectDefaultOpponentMotion();
            RestartPreview();
        }

        private void DrawNavigation()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(NavigationWidth)))
            {
                EditorGUILayout.LabelField("Motion List", EditorStyles.boldLabel);
                navigationScroll = EditorGUILayout.BeginScrollView(navigationScroll);
                ResourceEntry entry = SelectedResource;
                if (entry == null)
                {
                    EditorGUILayout.HelpBox("왼쪽에서 폴더를 선택하세요.", MessageType.Info);
                }
                else if (!entry.HasProfile)
                {
                    DrawDetectedFolderSummary(entry);
                    EditorGUILayout.Space(8f);
                    if (GUILayout.Button("Create Profile from Art", GUILayout.Height(34f))) CreateProfileFromArt(entry);
                }
                else if (entry.Kind == ActorKind.Character)
                {
                    DrawCharacterNavigation(entry);
                }
                else
                {
                    DrawMonsterNavigation(entry);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawInspectorUnsavedChanges()
        {
            List<string> changes = BuildUnsavedChanges(SelectedResource);
            if (changes.Count == 0) return;

            const float lineHeight = 17f;
            int visibleCount = changes.Count;
            float height = visibleCount * lineHeight + 8f;
            Rect panel = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(panel, new Color(0.16f, 0.05f, 0.05f, 0.2f));

            Color previous = GUI.contentColor;
            GUI.contentColor = new Color(1f, 0.28f, 0.24f);
            float y = panel.yMax - 4f - visibleCount * lineHeight;
            for (int i = 0; i < changes.Count; i++)
            {
                GUI.Label(new Rect(panel.x + 8f, y, panel.width - 16f, lineHeight), "• " + changes[i], EditorStyles.miniLabel);
                y += lineHeight;
            }
            GUI.contentColor = previous;
        }

        private static void DrawDetectedFolderSummary(ResourceEntry entry)
        {
            EditorGUILayout.HelpBox("프로필이 없습니다. 아래 아트 폴더를 읽어 초기 모션 데이터를 생성할 수 있습니다.", MessageType.Info);
            foreach (string folder in AssetDatabase.GetSubFolders(entry.FolderPath))
            {
                int count = LoadSprites(folder).Count;
                EditorGUILayout.LabelField($"• {Path.GetFileName(folder)} ({count} frames)", EditorStyles.miniLabel);
            }
        }

        private void DrawCharacterNavigation(ResourceEntry entry)
        {
            DrawWorkspaceButton(Workspace.Overview, "Overview");
            DrawWorkspaceButton(Workspace.Idle, "Base Idle");
            DrawWorkspaceButton(Workspace.IdleEvents, "Idle Events");
            if (workspace == Workspace.IdleEvents) DrawIdleEventButtons();
            DrawWorkspaceButton(Workspace.Attack, "Attacks");
            if (workspace == Workspace.Attack) DrawAttackButtons(entry.CharacterProfile);
            DrawWorkspaceButton(Workspace.Movement, "Movement");
        }

        private void DrawMonsterNavigation(ResourceEntry entry)
        {
            DrawWorkspaceButton(Workspace.Overview, "Overview");
            DrawWorkspaceButton(Workspace.Idle, "Base Idle");
            DrawWorkspaceButton(Workspace.IdleEvents, "Idle Events");
            if (workspace == Workspace.IdleEvents) DrawIdleEventButtons();
            DrawWorkspaceButton(Workspace.Hit, "Hit Reaction");
            DrawWorkspaceButton(Workspace.Defeat, "Defeat");
        }

        private void DrawWorkspaceButton(Workspace target, string label)
        {
            bool selected = workspace == target;
            if (GUILayout.Toggle(selected, label, EditorStyles.miniButton, GUILayout.Height(25f)) && !selected)
            {
                workspace = target;
                descriptionScroll = Vector2.zero;
                if (target == Workspace.IdleEvents) selectedIdleEventIndex = 0;
                if (target == Workspace.Attack && SelectedResource?.CharacterProfile != null)
                {
                    SelectTier(1);
                }
                else if (target != Workspace.Attack)
                {
                    SelectAttack(null);
                }
                RebuildFrameList();
                SelectDefaultOpponentMotion();
                RestartPreview();
            }
        }

        private void DrawIdleEventButtons()
        {
            if (activeProfileObject == null) return;
            activeProfileObject.Update();
            SerializedProperty events = activeProfileObject.FindProperty("idleEvents");
            EditorGUI.indentLevel++;
            for (int i = 0; i < events.arraySize; i++)
            {
                SerializedProperty name = events.GetArrayElementAtIndex(i).FindPropertyRelative("displayName");
                string label = string.IsNullOrWhiteSpace(name.stringValue) ? $"Event {i + 1}" : name.stringValue;
                bool selected = selectedIdleEventIndex == i;
                if (GUILayout.Toggle(selected, label, EditorStyles.miniButton) && !selected)
                {
                    selectedIdleEventIndex = i;
                    descriptionScroll = Vector2.zero;
                    RebuildFrameList();
                    RestartPreview();
                }
            }
            EditorGUI.indentLevel--;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+")) AddIdleEvent();
                using (new EditorGUI.DisabledScope(selectedIdleEventIndex < 0 || selectedIdleEventIndex >= events.arraySize))
                {
                    if (GUILayout.Button("−")) RemoveIdleEvent();
                }
            }
        }

        private void AddIdleEvent()
        {
            activeProfileObject.Update();
            SerializedProperty events = activeProfileObject.FindProperty("idleEvents");
            int index = events.arraySize;
            events.InsertArrayElementAtIndex(index);
            SerializedProperty clip = events.GetArrayElementAtIndex(index);
            clip.FindPropertyRelative("displayName").stringValue = $"Idle Event {index + 1}";
            clip.FindPropertyRelative("editorDescription").stringValue = string.Empty;
            clip.FindPropertyRelative("frames").ClearArray();
            clip.FindPropertyRelative("animationFps").floatValue = 6f;
            activeProfileObject.ApplyModifiedProperties();
            selectedIdleEventIndex = index;
            RebuildFrameList();
            RestartPreview();
        }

        private void RemoveIdleEvent()
        {
            activeProfileObject.Update();
            SerializedProperty events = activeProfileObject.FindProperty("idleEvents");
            if (selectedIdleEventIndex < 0 || selectedIdleEventIndex >= events.arraySize) return;
            events.DeleteArrayElementAtIndex(selectedIdleEventIndex);
            activeProfileObject.ApplyModifiedProperties();
            selectedIdleEventIndex = Mathf.Clamp(selectedIdleEventIndex - 1, 0, events.arraySize - 1);
            if (events.arraySize == 0) selectedIdleEventIndex = -1;
            RebuildFrameList();
            RestartPreview();
        }

        private void DrawAttackButtons(CharacterMotionProfile profile)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int tier = 1; tier <= 3; tier++)
                {
                    int capturedTier = tier;
                    bool selected = activeTier == tier;
                    if (GUILayout.Toggle(selected, "T" + tier, EditorStyles.miniButton) && !selected) SelectTier(capturedTier);
                }
            }

            ComboTierAttackPool pool = GetPool(profile, activeTier);
            if (pool != null)
            {
                IReadOnlyList<AttackMotionDefinition> motions = pool.Motions;
                EditorGUI.indentLevel++;
                for (int i = 0; i < motions.Count; i++)
                {
                    AttackMotionDefinition motion = motions[i];
                    if (motion == null) continue;
                    bool selected = motion == selectedAttack;
                    if (GUILayout.Toggle(selected, motion.name, EditorStyles.miniButton) && !selected) SelectAttack(motion);
                }
                EditorGUI.indentLevel--;
            }

            if (GUILayout.Button("+ New Attack")) CreateAttackAsset(activeTier);
        }

        private void SelectTier(int tier)
        {
            activeTier = tier;
            CharacterMotionProfile profile = SelectedResource?.CharacterProfile;
            activePool = profile != null ? GetPool(profile, tier) : null;
            poolObject = activePool != null ? new SerializedObject(activePool) : null;
            AttackMotionDefinition first = activePool != null && activePool.Motions.Count > 0 ? activePool.Motions[0] : null;
            SelectAttack(first);
        }

        private static ComboTierAttackPool GetPool(CharacterMotionProfile profile, int tier)
        {
            if (profile == null) return null;
            return tier == 1 ? profile.Tier1Pool : tier == 2 ? profile.Tier2Pool : profile.Tier3Pool;
        }

        private void SelectAttack(AttackMotionDefinition motion)
        {
            selectedAttack = motion;
            attackObject = motion != null ? new SerializedObject(motion) : null;
            descriptionScroll = Vector2.zero;
            ResetChargeSimulation(); // 다른 공격으로 넘어가면 이전 공격의 충전 상태는 남기지 않는다.
            RebuildFrameList();
            if (motion != null) RestartPreview();
        }

        /// <summary>삭제 방식은 대상이 어디에 등록돼 있느냐로 갈린다: IdleEvent/Attack은 목록에서 항목
        /// 자체를 제거하고, ClipSlot(Base Idle/Hit/Defeat)은 프로필에 항상 존재해야 하는 고정 슬롯이라
        /// 항목 대신 내용만 비운다.</summary>
        private enum MotionDeleteKind { IdleEvent, Attack, ClipSlot }

        /// <summary>Inspector 이름 행의 "삭제" 버튼이 지울 대상 하나. Owner는 실제로 바뀌는 에셋이자
        /// Undo 기록 대상이다 - Attack은 프로필이 아니라 그 공격을 참조하는 ComboTierAttackPool이
        /// 바뀐다는 점이 다르다.</summary>
        private sealed class MotionDeleteTarget
        {
            public MotionDeleteKind Kind;
            public UnityEngine.Object Owner;
            public string ProfileName;
            public string AreaLabel;
            public string AnimationName;
            public int Index = -1;              // IdleEvent/Attack 목록 인덱스
            public string ClipPropertyPath;     // ClipSlot 전용
            public ComboTierAttackPool Pool;    // Attack 전용
        }

        /// <summary>지금 Inspector에 열려 있는 애니메이션 하나를 삭제 대상으로 해석한다. 선택이 없거나
        /// 프로필이 아직 만들어지지 않았거나 인덱스가 유효하지 않으면 null이고, 그때 삭제 버튼은
        /// 비활성화된다. 어느 목록에 속한 항목인지는 workspace 기준으로 판정한다 - GetActiveClip()이
        /// 프리뷰 대상을 고르는 규칙과 같아서 화면에 보이는 애니메이션과 항상 일치한다.</summary>
        private MotionDeleteTarget ResolveMotionDeleteTarget()
        {
            ResourceEntry entry = SelectedResource;
            if (entry == null || !entry.HasProfile || activeProfileObject == null) return null;
            string profileName = entry.ProfileObject != null ? entry.ProfileObject.name : entry.Name;

            if (workspace == Workspace.Attack)
            {
                if (entry.CharacterProfile == null || selectedAttack == null) return null;
                ComboTierAttackPool pool = GetPool(entry.CharacterProfile, activeTier);
                if (pool == null) return null;
                int index = IndexOfMotion(pool, selectedAttack);
                if (index < 0) return null; // 지금 티어 풀에 등록돼 있지 않은 공격은 여기서 지우지 않는다
                return new MotionDeleteTarget
                {
                    Kind = MotionDeleteKind.Attack,
                    Owner = pool,
                    Pool = pool,
                    Index = index,
                    ProfileName = profileName,
                    AreaLabel = $"Attack T{activeTier}",
                    AnimationName = selectedAttack.name,
                };
            }

            if (workspace == Workspace.IdleEvents)
            {
                SerializedProperty events = activeProfileObject.FindProperty("idleEvents");
                if (events == null || selectedIdleEventIndex < 0 || selectedIdleEventIndex >= events.arraySize) return null;
                return new MotionDeleteTarget
                {
                    Kind = MotionDeleteKind.IdleEvent,
                    Owner = entry.ProfileObject,
                    Index = selectedIdleEventIndex,
                    ProfileName = profileName,
                    AreaLabel = $"Idle Event {selectedIdleEventIndex + 1}",
                    AnimationName = ReadClipDisplayName(events.GetArrayElementAtIndex(selectedIdleEventIndex)),
                };
            }

            string slotPath = workspace == Workspace.Idle ? "baseIdle"
                : workspace == Workspace.Hit ? "hit"
                : workspace == Workspace.Defeat ? "defeat"
                : null;
            if (slotPath == null) return null; // Overview/Movement에는 삭제할 애니메이션이 없다
            SerializedProperty slot = activeProfileObject.FindProperty(slotPath);
            if (slot == null) return null;
            return new MotionDeleteTarget
            {
                Kind = MotionDeleteKind.ClipSlot,
                Owner = entry.ProfileObject,
                ClipPropertyPath = slotPath,
                ProfileName = profileName,
                AreaLabel = workspace == Workspace.Idle ? "Base Idle" : workspace.ToString(),
                AnimationName = ReadClipDisplayName(slot),
            };
        }

        private static string ReadClipDisplayName(SerializedProperty clip)
        {
            SerializedProperty name = clip?.FindPropertyRelative("displayName");
            string value = name != null ? name.stringValue : null;
            return string.IsNullOrWhiteSpace(value) ? "Motion" : value;
        }

        private static int IndexOfMotion(ComboTierAttackPool pool, AttackMotionDefinition motion)
        {
            IReadOnlyList<AttackMotionDefinition> motions = pool.Motions;
            for (int i = 0; i < motions.Count; i++)
            {
                if (motions[i] == motion) return i;
            }
            return -1;
        }

        private void DrawMotionDeleteButton(Rect rect)
        {
            MotionDeleteTarget target = ResolveMotionDeleteTarget();
            Color previous = GUI.backgroundColor;
            using (new EditorGUI.DisabledScope(target == null))
            {
                if (target != null) GUI.backgroundColor = DangerButtonColor;
                var content = new GUIContent("삭제", target != null
                    ? $"'{target.AnimationName}'을(를) {target.AreaLabel}에서 삭제합니다. 확인창이 먼저 뜨고, Undo로 되돌릴 수 있습니다."
                    : "삭제할 애니메이션을 먼저 선택하세요.");
                if (GUI.Button(rect, content, EditorStyles.miniButton)) RequestMotionDelete();
            }
            GUI.backgroundColor = previous;
        }

        private void DrawMotionDeleteButtonLayout()
        {
            DrawMotionDeleteButton(GUILayoutUtility.GetRect(
                DeleteButtonWidth, EditorGUIUtility.singleLineHeight, GUILayout.Width(DeleteButtonWidth)));
        }

        /// <summary>확인창과 실제 삭제를 지금 진행 중인 IMGUI 패스 안에서 처리하지 않는다 - 그리는 중인
        /// 배열 항목을 그 자리에서 지우면 같은 패스의 남은 필드(Description/FPS/Frame List)가 이미
        /// 사라진 SerializedProperty를 계속 참조하고, 모달 확인창도 Layout/Repaint 사이에 끼어들어
        /// 레이아웃 불일치를 낼 수 있다. Undo/Redo와 같은 방식으로 다음 에디터 틱으로 미룬다.</summary>
        private void RequestMotionDelete()
        {
            if (pendingMotionDelete) return;
            pendingMotionDelete = true;
            EditorApplication.delayCall += PerformPendingMotionDelete;
        }

        private void PerformPendingMotionDelete()
        {
            pendingMotionDelete = false;
            if (this == null) return;

            // 한 틱 뒤이므로 대상을 다시 해석해서 검증한다 - 그 사이 선택이 사라졌으면 아무것도 하지 않는다.
            MotionDeleteTarget target = ResolveMotionDeleteTarget();
            if (target == null || target.Owner == null) return;
            if (!EditorUtility.DisplayDialog("애니메이션 삭제", BuildMotionDeleteMessage(target), "삭제", "취소")) return;

            // 확인을 누른 뒤에만 Undo를 기록한다 - 취소하면 Undo 스택에 아무것도 남지 않는다.
            // RegisterCompleteObjectUndo는 에셋 전체 상태를 스냅샷하므로 되돌릴 때 항목의 원래 위치와
            // 이름/설명/프레임 배열/FPS까지 그대로 복구된다.
            Undo.RegisterCompleteObjectUndo(target.Owner, "Delete Motion Animation");
            switch (target.Kind)
            {
                case MotionDeleteKind.Attack:
                    DeleteAttackFromPool(target);
                    break;
                case MotionDeleteKind.IdleEvent:
                    DeleteIdleEventEntry(target);
                    break;
                default:
                    ClearClipSlot(target);
                    break;
            }
            EditorUtility.SetDirty(target.Owner);
            Repaint();
        }

        private static string BuildMotionDeleteMessage(MotionDeleteTarget target)
        {
            string scope = target.Kind == MotionDeleteKind.ClipSlot
                ? "이 슬롯은 프로필에 항상 존재해야 하므로 항목을 없애지 않고 등록된 이름/설명/프레임/FPS를 비웁니다."
                : "현재 Motion Profile에서 이 애니메이션의 등록만 제거합니다.";
            return $"Motion Profile: {target.ProfileName}\n" +
                   $"등록 영역: {target.AreaLabel}\n" +
                   $"애니메이션: {target.AnimationName}\n\n" +
                   scope + "\n" +
                   "원본 Sprite/PNG, 에셋 파일, 다른 프로필이나 다른 티어의 등록은 삭제되지 않습니다.\n\n" +
                   "이 작업은 Undo(Ctrl/Cmd+Z)로 복구할 수 있습니다.";
        }

        /// <summary>풀에서 참조만 제거한다 - AttackMotionDefinition 에셋 파일, 그 프레임이 쓰던
        /// Sprite/PNG, 같은 공격을 참조하는 다른 티어/다른 캐릭터의 등록은 전부 그대로 남는다.</summary>
        private void DeleteAttackFromPool(MotionDeleteTarget target)
        {
            var serializedPool = new SerializedObject(target.Pool);
            RemoveArrayElement(serializedPool.FindProperty("motions"), target.Index);
            serializedPool.ApplyModifiedPropertiesWithoutUndo();

            // 오래 살아 있는 poolObject가 나중에 옛 목록을 다시 써버리지 않도록 방금 만든 것으로 교체한다
            // (CreateAttackAsset이 새 공격을 추가할 때와 같은 처리).
            activePool = target.Pool;
            poolObject = serializedPool;

            IReadOnlyList<AttackMotionDefinition> remaining = target.Pool.Motions;
            int next = ResolveSelectionAfterDelete(target.Index, remaining.Count);
            SelectAttack(next >= 0 ? remaining[next] : null);
        }

        private void DeleteIdleEventEntry(MotionDeleteTarget target)
        {
            activeProfileObject.Update();
            RemoveArrayElement(activeProfileObject.FindProperty("idleEvents"), target.Index);
            activeProfileObject.ApplyModifiedPropertiesWithoutUndo();

            activeProfileObject.Update();
            int remaining = activeProfileObject.FindProperty("idleEvents").arraySize;
            selectedIdleEventIndex = ResolveSelectionAfterDelete(target.Index, remaining);
            descriptionScroll = Vector2.zero;
            RebuildFrameList();
            RestartPreview();
        }

        /// <summary>Base Idle/Hit/Defeat는 프로필이 항상 들고 있어야 하는 고정 슬롯이라 목록처럼 항목을
        /// 뺄 수 없다 - 대신 새로 만든 FrameClip과 같은 상태(이름 Motion, 설명 없음, 프레임 0개, FPS 6)로
        /// 비운다. 프레임이 참조하던 Sprite 에셋 자체는 건드리지 않는다.</summary>
        private void ClearClipSlot(MotionDeleteTarget target)
        {
            activeProfileObject.Update();
            SerializedProperty clip = activeProfileObject.FindProperty(target.ClipPropertyPath);
            clip.FindPropertyRelative("displayName").stringValue = "Motion";
            clip.FindPropertyRelative("editorDescription").stringValue = string.Empty;
            clip.FindPropertyRelative("frames").ClearArray();
            clip.FindPropertyRelative("animationFps").floatValue = 6f;
            activeProfileObject.ApplyModifiedPropertiesWithoutUndo();

            descriptionScroll = Vector2.zero;
            RebuildFrameList();
            RestartPreview();
        }

        /// <summary>배열에서 index 항목을 실제로 제거한다. 오브젝트 참조 배열에서는
        /// DeleteArrayElementAtIndex가 항목을 먼저 null로만 만들고 길이를 줄이지 않는 Unity 레거시
        /// 동작이 있어, 길이가 그대로면 한 번 더 호출한다. 나머지 항목의 순서는 그대로 유지된다.</summary>
        private static void RemoveArrayElement(SerializedProperty array, int index)
        {
            if (array == null || index < 0 || index >= array.arraySize) return;
            int size = array.arraySize;
            array.DeleteArrayElementAtIndex(index);
            if (array.arraySize == size) array.DeleteArrayElementAtIndex(index);
        }

        /// <summary>삭제 직후 선택 규칙: 지운 자리로 밀려온 다음 항목 -> 없으면 이전 항목 -> 목록이
        /// 비면 선택 해제(-1).</summary>
        private static int ResolveSelectionAfterDelete(int deletedIndex, int remainingCount)
        {
            if (remainingCount <= 0) return -1;
            return deletedIndex < remainingCount ? deletedIndex : remainingCount - 1;
        }

        private void DrawInspector()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                ResourceEntry entry = SelectedResource;
                if (entry == null)
                {
                    EditorGUILayout.HelpBox("아트 폴더를 선택하세요.", MessageType.Info);
                }
                else
                {
                    DrawResourceHeader(entry);
                    if (!entry.HasProfile) DrawCreationGuide(entry);
                    else if (entry.Kind == ActorKind.Character) DrawCharacterInspector(entry);
                    else DrawMonsterInspector(entry);
                }
                EditorGUILayout.EndScrollView();
                DrawInspectorUnsavedChanges();
            }
        }

        private static void DrawResourceHeader(ResourceEntry entry)
        {
            EditorGUILayout.LabelField(entry.Name, EditorStyles.largeLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Art Folder", entry.FolderPath);
                EditorGUILayout.ObjectField("Profile", entry.ProfileObject, entry.Kind == ActorKind.Character
                    ? typeof(CharacterMotionProfile)
                    : typeof(MonsterMotionProfile), false);
            }
            EditorGUILayout.Space(6f);
        }

        private static void DrawCreationGuide(ResourceEntry entry)
        {
            EditorGUILayout.HelpBox(
                "이 폴더에는 아직 모션 프로필이 없습니다. 중앙의 Create Profile from Art를 누르면 " +
                "하위 모션 폴더를 읽어 프레임을 자동 등록합니다.", MessageType.Info);
            DrawDetectedFolderSummary(entry);
        }

        private void DrawCharacterInspector(ResourceEntry entry)
        {
            activeProfileObject.Update();
            switch (workspace)
            {
                case Workspace.Overview:
                    DrawOverview(activeProfileObject, "Character Setup");
                    DrawCharacterPlacement();
                    break;
                case Workspace.Idle:
                    DrawClipEditor(activeProfileObject.FindProperty("baseIdle"), false, -1, false);
                    break;
                case Workspace.IdleEvents:
                    DrawSelectedIdleEventEditor();
                    break;
                case Workspace.Attack:
                    DrawAttackEditor();
                    break;
                case Workspace.Movement:
                    DrawMovementWorkspace();
                    break;
            }
            if (activeProfileObject.ApplyModifiedProperties()) EditorUtility.SetDirty(entry.CharacterProfile);
        }

        /// <summary>이 캐릭터의 위치 연출 두 가지를 한 화면에서 편집한다. 둘 다 런타임에서는 같은
        /// 컨트롤러(AttackMovement)가 하나의 Transform에 적용하므로 서로의 위치를 덮어쓰지 않는다 -
        /// Attack Movement는 실제 타격에서, Charge Movement는 누적 입력 충전 중에만 동작한다.</summary>
        private void DrawMovementWorkspace()
        {
            EditorGUILayout.LabelField("Attack Movement (실제 타격 시)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(activeProfileObject.FindProperty("attackMovement"), GUIContent.none, true);
            EditorGUILayout.HelpBox("Hit Frame에 도달할 때 1회 재생됩니다. 키 입력이나 충전 진행으로는 발동하지 않습니다.", MessageType.Info);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Charge Movement (누적 입력 충전 중)", EditorStyles.boldLabel);
            SerializedProperty charge = activeProfileObject.FindProperty("chargeMovement");
            SerializedProperty enabled = charge.FindPropertyRelative("enableChargeMovement");
            EditorGUILayout.PropertyField(enabled, new GUIContent("Enable Charge Movement"));
            using (new EditorGUI.DisabledScope(!enabled.boolValue))
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(charge.FindPropertyRelative("chargeMoveDistance"),
                    new GUIContent("Charge Move Distance", "입력 1회당 움찔할 거리. 음수면 뒤로 당겨진다. Attack Movement보다 작게 잡는 것을 권장."));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUILayout.PropertyField(charge.FindPropertyRelative("chargeMoveInDuration"),
                    new GUIContent("Charge Move In Duration", "움찔이 나가는 시간(초). 이후 곧바로 기준점으로 돌아오기 시작한다."));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUILayout.PropertyField(charge.FindPropertyRelative("chargeMoveReturnDuration"),
                    new GUIContent("Charge Move Return Duration", "움찔이 기준점으로 돌아오는 시간(초). 충전 취소 복귀에도 같은 값을 쓴다."));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            }
            EditorGUILayout.HelpBox("누적 입력이 1회 쌓일 때마다 움찔 1회가 재생됩니다(Required Inputs가 5면 4회). " +
                                    "발사를 일으키는 마지막 입력에서는 움찔 없이 Attack Movement만 나가고, 충전이 취소되면 원위치로 돌아옵니다. " +
                                    "Use Accumulated Input이 꺼진 공격에는 영향이 없습니다.", MessageType.Info);
        }

        private void DrawMonsterInspector(ResourceEntry entry)
        {
            activeProfileObject.Update();
            switch (workspace)
            {
                case Workspace.Overview:
                    DrawOverview(activeProfileObject, "Monster Setup");
                    DrawMonsterPlacement();
                    break;
                case Workspace.Idle:
                    DrawClipEditor(activeProfileObject.FindProperty("baseIdle"), false, -1, false);
                    break;
                case Workspace.IdleEvents:
                    DrawMonsterIdleEventSettings();
                    DrawSelectedIdleEventEditor();
                    break;
                case Workspace.Hit:
                    DrawMonsterHitReaction();
                    DrawClipEditor(activeProfileObject.FindProperty("hit"), false, -1, true);
                    break;
                case Workspace.Defeat:
                    DrawClipEditor(activeProfileObject.FindProperty("defeat"), false, -1, false);
                    break;
            }
            if (activeProfileObject.ApplyModifiedProperties()) EditorUtility.SetDirty(entry.MonsterProfile);
        }

        /// <summary>런타임(TargetCombatController)이 Base Idle 사이에 Idle Event를 자동으로 굴리는
        /// 주기/확률 - Character Motion Profile의 같은 개념(idleEventCheckInterval/idleEventChance)과
        /// 동일한 규칙을 쓴다. 프리뷰 수동 선택 목록(아래 DrawSelectedIdleEventEditor)과는 별개로,
        /// 이 값은 순수하게 런타임 자동 재생에만 쓰인다.</summary>
        private void DrawMonsterIdleEventSettings()
        {
            EditorGUILayout.LabelField("Auto-Play (Runtime)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(activeProfileObject.FindProperty("idleEventCheckInterval"), new GUIContent("Idle Event Check Interval"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            EditorGUILayout.PropertyField(activeProfileObject.FindProperty("idleEventChance"), new GUIContent("Idle Event Chance"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            EditorGUILayout.Space(6f);
        }

        /// <summary>hitReaction을 통째로 재귀 PropertyField하지 않고 필드별로 직접 그린다 - Damage
        /// Number만 접이식으로 따로 빼기 위함이다(통째로 그리면 접기/펼치기를 걸 수 없다). 기존 Hit
        /// Reaction 필드들의 순서/구성은 그대로 유지한다.</summary>
        private void DrawMonsterHitReaction()
        {
            SerializedProperty reaction = activeProfileObject.FindProperty("hitReaction");

            EditorGUILayout.PropertyField(reaction.FindPropertyRelative("holdFrame"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            EditorGUILayout.PropertyField(reaction.FindPropertyRelative("recoveryFrame"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            EditorGUILayout.PropertyField(reaction.FindPropertyRelative("recoveryDuration"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            EditorGUILayout.PropertyField(reaction.FindPropertyRelative("holdTimeout"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            EditorGUILayout.PropertyField(reaction.FindPropertyRelative("shakeStrength"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            EditorGUILayout.PropertyField(reaction.FindPropertyRelative("shakeFrequency"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            EditorGUILayout.PropertyField(reaction.FindPropertyRelative("shakeDecayDuration"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());

            EditorGUILayout.Space(6f);
            SerializedProperty damageNumberOffset = reaction.FindPropertyRelative("damageNumberOffset");
            // isExpanded는 damageNumberOffset 자신의 "자식 펼치기" 용도가 아니라, 이 접이식 섹션 자체의
            // 열림 상태를 저장하는 용도로 그대로 재사용한다 - SerializedProperty에 붙어 프로필 에셋과
            // 함께 자동으로 영속화되므로 창을 닫았다 열어도 접힘 상태가 유지된다.
            damageNumberOffset.isExpanded = EditorGUILayout.Foldout(damageNumberOffset.isExpanded,
                damageNumberOffset.isExpanded ? "Damage Number ▼" : "Damage Number ▶", true);
            if (damageNumberOffset.isExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(damageNumberOffset, new GUIContent("Offset"));
                EditorGUILayout.PropertyField(reaction.FindPropertyRelative("damageNumberRandomHorizontalJitter"), new GUIContent("Random Horizontal Jitter"));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUILayout.PropertyField(reaction.FindPropertyRelative("damageNumberRiseDistance"), new GUIContent("Rise Distance"));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUILayout.PropertyField(reaction.FindPropertyRelative("damageNumberDuration"), new GUIContent("Duration"));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUILayout.PropertyField(reaction.FindPropertyRelative("damageNumberTextColor"), new GUIContent("Text Color"));
                EditorGUILayout.PropertyField(reaction.FindPropertyRelative("damageNumberFontSize"), new GUIContent("Font Size"));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUILayout.PropertyField(reaction.FindPropertyRelative("damageNumberSortingOrder"), new GUIContent("Sorting Order"));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUI.indentLevel--;
            }
        }

        private static void DrawOverview(SerializedObject profile, string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(profile.FindProperty("displayName"), new GUIContent("Display Name"));
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(profile.FindProperty("resourceFolderPath"), new GUIContent("Resource Folder"));
            }
        }

        private void DrawCharacterPlacement()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Presentation / Placement", EditorStyles.boldLabel);
            SerializedProperty preview = activeProfileObject.FindProperty("preview");
            EditorGUILayout.PropertyField(preview.FindPropertyRelative("characterOffset"), new GUIContent("Actor Offset"));
            EditorGUILayout.PropertyField(preview.FindPropertyRelative("characterScale"), new GUIContent("Actor Scale"));

            DrawCombatStageLayoutSection();
        }

        private void DrawMonsterPlacement()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Presentation / Placement", EditorStyles.boldLabel);
            SerializedProperty preview = activeProfileObject.FindProperty("preview");
            EditorGUILayout.PropertyField(preview.FindPropertyRelative("actorOffset"), new GUIContent("Actor Offset"));
            EditorGUILayout.PropertyField(preview.FindPropertyRelative("actorScale"), new GUIContent("Actor Scale"));
            EditorGUILayout.PropertyField(activeProfileObject.FindProperty("spriteFlipX"), new GUIContent("Sprite Flip X"));
            EditorGUILayout.PropertyField(preview.FindPropertyRelative("receivePointOffset"), new GUIContent("Receive Point Offset"));

            DrawCombatStageLayoutSection();
        }

        /// <summary>Character/Monster Overview 양쪽에서 공유하는 무대 배치값 - 같은 CombatStageLayout
        /// 에셋의 SerializedObject를 그대로 쓰므로, 어느 탭에서 고치든 즉시 서로/Preview에 반영된다.</summary>
        private void DrawCombatStageLayoutSection()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Combat Stage Layout (Shared)", EditorStyles.boldLabel);
            SerializedObject layoutObject = GetStageLayoutObject();
            layoutObject.Update();
            EditorGUILayout.PropertyField(layoutObject.FindProperty("characterSlotPosition"), new GUIContent("Character Slot Position"));
            EditorGUILayout.PropertyField(layoutObject.FindProperty("monsterSlotPosition"), new GUIContent("Monster Slot Position"));
            if (layoutObject.ApplyModifiedProperties()) EditorUtility.SetDirty(stageLayout);
        }

        private void DrawSelectedIdleEventEditor()
        {
            SerializedProperty events = activeProfileObject.FindProperty("idleEvents");
            if (events.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Idle Event가 없습니다. 중앙의 + 버튼으로 추가하세요.", MessageType.Info);
                return;
            }
            selectedIdleEventIndex = Mathf.Clamp(selectedIdleEventIndex, 0, events.arraySize - 1);
            DrawClipEditor(events.GetArrayElementAtIndex(selectedIdleEventIndex), false, -1, false);
        }

        private void DrawClipEditor(SerializedProperty clip, bool attack, int hitFrame, bool hitReactionPreview)
        {
            if (clip == null) return;
            DrawMotionNameEditor(clip.FindPropertyRelative("displayName"));
            DrawDescriptionEditor(clip.FindPropertyRelative("editorDescription"));
            SerializedProperty fps = clip.FindPropertyRelative("animationFps");
            SerializedProperty frames = clip.FindPropertyRelative("frames");
            EditorGUILayout.PropertyField(fps, new GUIContent("Frames Per Second"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            DrawFrameSection(activeProfileObject, frames, null);
        }

        private void DrawAttackEditor()
        {
            if (selectedAttack == null)
            {
                EditorGUILayout.HelpBox("중앙에서 공격 모션을 선택하거나 새로 만드세요.", MessageType.Info);
                return;
            }

            attackObject.Update();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(selectedAttack.name, EditorStyles.boldLabel);
                DrawMotionDeleteButtonLayout();
            }
            DrawDescriptionEditor(attackObject.FindProperty("editorDescription"));
            SerializedProperty frames = attackObject.FindProperty("frames");
            SerializedProperty overlayFrames = attackObject.FindProperty("overlayFrames");
            SerializedProperty fps = attackObject.FindProperty("animationFps");
            SerializedProperty hit = attackObject.FindProperty("hitFrameIndex");
            SerializedProperty cast = attackObject.FindProperty("castFrameIndex");

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(fps, new GUIContent("FPS"));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUILayout.PropertyField(hit, new GUIContent("Hit Frame"));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            }
            EditorGUILayout.PropertyField(attackObject.FindProperty("endFrameDuration"), new GUIContent("End Hold"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());

            EditorGUILayout.Space(6f);
            DrawInputResponseSection(frames, hit, cast);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Cast Presentation", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(cast, new GUIContent("Cast Frame"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            EditorGUILayout.PropertyField(attackObject.FindProperty("castEffectPrefab"), new GUIContent("Effect Prefab"));
            EditorGUILayout.PropertyField(attackObject.FindProperty("castEffectOffset"), new GUIContent("Effect Offset"));
            EditorGUILayout.PropertyField(attackObject.FindProperty("castEffectScale"), new GUIContent("Effect Scale"));
            EditorGUILayout.PropertyField(attackObject.FindProperty("castSound"), new GUIContent("Cast Sound"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Hit Presentation", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(attackObject.FindProperty("hitEffectPrefab"), new GUIContent("Effect Prefab"));
            EditorGUILayout.PropertyField(attackObject.FindProperty("hitEffectOffset"), new GUIContent("Effect Offset"));
            EditorGUILayout.PropertyField(attackObject.FindProperty("hitEffectScale"), new GUIContent("Effect Scale"));

            // Jitter는 "이 공격이 직접 정한다 / 맞는 몬스터의 기본값에 맡긴다" 두 상태가 있고, 값 0도
            // 정당한 값(랜덤 없이 정확히 한 점)이라 토글 없이는 둘을 구분할 수 없다.
            SerializedProperty overrideJitter = attackObject.FindProperty("overrideHitEffectJitter");
            EditorGUILayout.PropertyField(overrideJitter,
                new GUIContent("Override Jitter", "끄면 맞는 몬스터의 HitEffectSpawner에 설정된 Spawn Jitter를 그대로 씁니다."));
            using (new EditorGUI.DisabledScope(!overrideJitter.boolValue))
            {
                EditorGUILayout.PropertyField(attackObject.FindProperty("hitEffectJitter"),
                    new GUIContent("Effect Jitter", "이펙트가 흩어지는 범위(X/Y 각각 ±값). Preview의 주황색 점선 사각형으로 확인합니다."));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            }
            if (!overrideJitter.boolValue)
            {
                Vector2? monsterJitter = GetPreviewMonsterJitter();
                string note = monsterJitter.HasValue
                    ? $"기본값 사용 - 몬스터 설정 ±{monsterJitter.Value.x:0.###}, ±{monsterJitter.Value.y:0.###}"
                    : "기본값 사용 - 열려 있는 씬에서 몬스터를 찾지 못해 범위를 표시할 수 없습니다";
                EditorGUILayout.LabelField(" ", note, EditorStyles.miniLabel);
            }

            EditorGUILayout.PropertyField(attackObject.FindProperty("hitSound"), new GUIContent("Hit Sound"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Projectile Presentation", EditorStyles.boldLabel);
            SerializedProperty projectilePrefab = attackObject.FindProperty("projectilePrefab");
            SerializedProperty projectileLaunchOffset = attackObject.FindProperty("projectileLaunchOffset");
            SerializedProperty projectileScale = attackObject.FindProperty("projectileScale");
            EditorGUILayout.PropertyField(projectilePrefab, new GUIContent("Projectile Prefab"));
            EditorGUILayout.PropertyField(projectileLaunchOffset, new GUIContent("Launch Offset", "Actor Origin 기준 로컬 X/Y. Preview의 청록색 Launch Point로 확인합니다."));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            EditorGUILayout.PropertyField(projectileScale, new GUIContent("Projectile Scale"));
            RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            if (projectilePrefab.objectReferenceValue != null)
            {
                GameObject prefab = projectilePrefab.objectReferenceValue as GameObject;
                if (prefab != null && prefab.GetComponent<ProjectileMover>() == null)
                {
                    EditorGUILayout.HelpBox("발사체 프리팹 루트에 ProjectileMover가 필요합니다.", MessageType.Error);
                }
                EditorGUILayout.HelpBox("발사체는 Cast Frame의 Launch Point에서 출발해 Hit Frame의 Receive Point에 도착합니다. 이미지의 오른쪽(+X)이 머리입니다.", MessageType.Info);
                if (hit.intValue <= cast.intValue)
                {
                    EditorGUILayout.HelpBox("발사체를 표시하려면 Cast Frame이 Hit Frame보다 앞서야 합니다.", MessageType.Warning);
                }
            }

            DrawFrameSection(attackObject, frames, hit, cast, overlayFrames);

            if (frames.arraySize > 0 && (hit.intValue < 0 || hit.intValue >= frames.arraySize))
            {
                EditorGUILayout.HelpBox($"Hit Frame은 0~{frames.arraySize - 1} 범위여야 합니다.", MessageType.Warning);
            }
            if (frames.arraySize > 0 && (cast.intValue < 0 || cast.intValue >= frames.arraySize))
            {
                EditorGUILayout.HelpBox($"Cast Frame은 0~{frames.arraySize - 1} 범위여야 합니다.", MessageType.Warning);
            }
            if (attackObject.ApplyModifiedProperties()) EditorUtility.SetDirty(selectedAttack);
        }

        /// <summary>이 공격이 입력에 어떻게 반응할지(Direct / Accumulated)를 정하는 영역. 체크가 꺼져
        /// 있으면 Direct Input 전용 설정(Queue Window)만, 켜져 있으면 누적 입력 설정만 펼친다 - 서로
        /// 다른 모드의 값이 같은 화면에 섞여 보이지 않게 한다.</summary>
        private void DrawInputResponseSection(SerializedProperty frames, SerializedProperty hit, SerializedProperty cast)
        {
            SerializedProperty accumulated = attackObject.FindProperty("useAccumulatedInput");
            inputResponseExpanded = EditorGUILayout.Foldout(inputResponseExpanded, "Input Response", true, EditorStyles.foldoutHeader);
            if (!inputResponseExpanded) return;

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(accumulated, new GUIContent("Use Accumulated Input",
                    "켜면 입력을 모아서 한 번 타격하는 누적 입력 공격(궁수/마법사)이 되고, 꺼두면 " +
                    "키 입력 1회 = 타격 1회인 기존 Direct Input 공격이다."));

                if (!accumulated.boolValue)
                {
                    EditorGUILayout.PropertyField(attackObject.FindProperty("queueExpireTimeout"),
                        new GUIContent("Queue Window (Direct)", "Direct Input 전용 - 마지막 입력 이후 이 시간(초) 동안 " +
                                                                "새 입력이 없으면 남은 공격 예약을 취소한다. 0.15~0.25 권장."));
                    RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                    EditorGUILayout.LabelField(" ", "키 입력 1회 = 타격 1회 (기존 동작)", EditorStyles.miniLabel);
                    return;
                }

                SerializedProperty required = attackObject.FindProperty("requiredInputsToStrike");
                EditorGUILayout.PropertyField(required, new GUIContent("Required Inputs to Strike",
                    "공격 시작(첫 입력 1회 포함)부터 타격까지 필요한 총 입력 수. 1 이상."));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUILayout.PropertyField(attackObject.FindProperty("noInputGraceTime"), new GUIContent("No-Input Grace Time",
                    "입력이 끊긴 뒤 현재 충전 자세를 그대로 유지하는 시간(초). 이 동안에는 충전량이 줄지 않는다."));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUILayout.PropertyField(attackObject.FindProperty("chargeDecayDuration"), new GUIContent("Charge Decay Duration",
                    "유예 시간이 지난 뒤 가득 찬 충전량이 0까지 줄어드는 데 걸리는 시간(초). 0이면 즉시 초기화."));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUILayout.PropertyField(attackObject.FindProperty("carryOverflowInputs"), new GUIContent("Carry Overflow Inputs",
                    "타격 이후 Recovery 중에 들어온 입력을 다음 공격 충전으로 넘긴다(권장: On)."));

                if (required.intValue < 1)
                {
                    EditorGUILayout.HelpBox("Required Inputs to Strike는 1 이상이어야 합니다.", MessageType.Error);
                }
                if (frames.arraySize > 0 && hit.intValue <= 0)
                {
                    EditorGUILayout.HelpBox("Hit Frame이 0이면 충전 진행을 보여줄 Windup 프레임이 없습니다 - " +
                                            "첫 입력에서 바로 타격 프레임이 나옵니다. Hit Frame을 1 이상으로 두세요.", MessageType.Warning);
                }
                else if (frames.arraySize > 0)
                {
                    EditorGUILayout.LabelField(" ", $"충전 구간 Frame 0~{Mathf.Max(0, hit.intValue - 1)} / 타격 Frame {hit.intValue} / " +
                                                    $"이후 Recovery는 FPS·End Hold 사용", EditorStyles.miniLabel);
                }
                if (attackObject.FindProperty("projectilePrefab").objectReferenceValue != null && cast.intValue < hit.intValue)
                {
                    EditorGUILayout.HelpBox("누적 입력 공격에서 Cast Frame이 Hit Frame보다 앞이면, 발사체는 충전 도중 " +
                                            "그 프레임에 도달하는 순간 출발하고 비행 시간은 (Hit-Cast)/FPS로 고정됩니다 - " +
                                            "충전 속도와 무관하므로 발사체가 타격보다 먼저 도착할 수 있습니다.", MessageType.Warning);
                }
            }
        }

        private void DrawMotionNameEditor(SerializedProperty name)
        {
            // 이름 행의 오른쪽 끝을 삭제 버튼 몫으로 떼어낸다 - 행 구성과 높이는 그대로다.
            Rect row = EditorGUILayout.GetControlRect();
            Rect rect = new Rect(row.x, row.y, Mathf.Max(120f, row.width - DeleteButtonWidth - 4f), row.height);
            Rect deleteRect = new Rect(rect.xMax + 4f, row.y, DeleteButtonWidth, row.height);
            // 텍스트 필드 영역만 포커스 대상으로 등록한다 - 삭제 버튼 클릭이 텍스트 입력으로 취급되면 안 된다.
            RegisterTextInputPointerDown(rect);
            bool focused = IsFocusedControl(MotionNameControlName);
            Color previous = GUI.backgroundColor;
            if (focused) GUI.backgroundColor = ActiveTextFieldTint;
            GUI.SetNextControlName(MotionNameControlName);
            EditorGUI.BeginChangeCheck();
            string value = EditorGUI.TextField(rect, new GUIContent("Motion Name"), name.stringValue);
            if (EditorGUI.EndChangeCheck()) name.stringValue = value;
            GUI.backgroundColor = previous;
            if (focused) DrawActiveTextFieldBorder(rect);
            DrawMotionDeleteButton(deleteRect);
        }

        private void DrawDescriptionEditor(SerializedProperty description)
        {
            if (description == null) return;
            EditorGUILayout.LabelField("Description", EditorStyles.miniBoldLabel);
            Rect outer = GUILayoutUtility.GetRect(0f, 64f, GUILayout.ExpandWidth(true));
            GUI.Box(outer, GUIContent.none, EditorStyles.helpBox);

            Rect viewport = new Rect(outer.x + 3f, outer.y + 3f, outer.width - 6f, outer.height - 6f);
            RegisterTextInputPointerDown(viewport);
            float textWidth = Mathf.Max(80f, viewport.width - 18f);
            GUIStyle style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            float textHeight = Mathf.Max(54f, style.CalcHeight(new GUIContent(description.stringValue + " "), textWidth));
            descriptionScroll = GUI.BeginScrollView(viewport, descriptionScroll,
                new Rect(0f, 0f, textWidth, textHeight), false, textHeight > viewport.height);
            EditorGUI.BeginChangeCheck();
            bool focused = IsFocusedControl(DescriptionControlName);
            Color previous = GUI.backgroundColor;
            if (focused) GUI.backgroundColor = ActiveTextFieldTint;
            GUI.SetNextControlName(DescriptionControlName);
            string value = GUI.TextArea(new Rect(0f, 0f, textWidth, textHeight), description.stringValue, style);
            if (EditorGUI.EndChangeCheck()) description.stringValue = value;
            GUI.backgroundColor = previous;
            GUI.EndScrollView();
            if (focused) DrawActiveTextFieldBorder(outer);
        }

        private static void DrawActiveTextFieldBorder(Rect rect)
        {
            const float thickness = 1.5f;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), ActiveTextFieldBorder);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), ActiveTextFieldBorder);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), ActiveTextFieldBorder);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), ActiveTextFieldBorder);
        }

        private void DrawFrameSection(SerializedObject owner, SerializedProperty frames, SerializedProperty hitFrame, SerializedProperty castFrame = null, SerializedProperty overlayFrames = null)
        {
            if (frameList == null || frameListOwner != owner || frameListPropertyPath != frames.propertyPath)
            {
                frameList = BuildFrameList(owner, frames, hitFrame, castFrame, overlayFrames);
                frameListOwner = owner;
                frameListPropertyPath = frames.propertyPath;
            }
            frameList.DoLayoutList();
            DrawActorFrameDropZone(frames, overlayFrames);
            if (overlayFrames != null) DrawOverlayFrameControls(frames, overlayFrames);
        }

        /// <summary>본체 프레임 Drop Zone. 여기서 추가한 만큼 - 오버레이 배열이 활성 상태(비어 있지 않음)
        /// 라면 - 오버레이에도 같은 수의 null 슬롯을 덧붙여 인덱스 쌍이 어긋나지 않게 한다.</summary>
        private static void DrawActorFrameDropZone(SerializedProperty frames, SerializedProperty overlayFrames)
        {
            List<Sprite> dropped = DrawSpriteDropZone("Drop Sprites Here");
            if (dropped == null || dropped.Count == 0) return;
            for (int i = 0; i < dropped.Count; i++) AppendObjectReference(frames, dropped[i]);
            if (overlayFrames == null || overlayFrames.arraySize == 0) return;
            for (int i = 0; i < dropped.Count; i++) AppendObjectReference(overlayFrames, null);
        }

        /// <summary>오버레이 전용 Drop Zone과 길이 맞춤 버튼. 오버레이는 본체 frames와 인덱스만 공유하므로
        /// 여기에 FPS/시작 프레임 같은 재생 필드는 만들지 않는다. 드롭 기본 동작은 Replace다 - 오버레이는
        /// 본체 프레임과 1:1로 다시 굽는 일이 잦아서, 이미 들어있는 배열 뒤에 붙으면 인덱스가 통째로 밀린다.</summary>
        private void DrawOverlayFrameControls(SerializedProperty frames, SerializedProperty overlayFrames)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string mode = overlayDropAppends ? "Append" : "Replace";
                List<Sprite> dropped = DrawSpriteDropZone($"Drop Overlay Sprites Here ({mode})");
                if (dropped != null && dropped.Count > 0)
                {
                    if (!overlayDropAppends) overlayFrames.ClearArray();
                    for (int i = 0; i < dropped.Count; i++) AppendObjectReference(overlayFrames, dropped[i]);
                }

                var appendContent = new GUIContent("Append",
                    "끄면(기본) 드롭한 스프라이트로 오버레이 배열을 통째로 교체한다. 켜면 기존 배열 뒤에 순서대로 덧붙인다.");
                overlayDropAppends = GUILayout.Toggle(overlayDropAppends, appendContent, EditorStyles.miniButton,
                    GUILayout.Width(64f), GUILayout.Height(34f));

                var matchContent = new GUIContent("Match Overlay Length",
                    "오버레이 배열 길이를 본체 프레임 수에 맞춘다. 늘어난 요소는 null(그 프레임에는 오버레이 없음)이고, 줄어들면 뒤쪽 요소가 사라진다.");
                if (GUILayout.Button(matchContent, GUILayout.Width(150f), GUILayout.Height(34f)))
                {
                    while (overlayFrames.arraySize > frames.arraySize) RemoveArrayElementAt(overlayFrames, overlayFrames.arraySize - 1);
                    PadArrayWithNulls(overlayFrames, frames.arraySize);
                }
            }
            if (overlayFrames.arraySize > 0 && overlayFrames.arraySize != frames.arraySize)
            {
                EditorGUILayout.HelpBox(
                    $"Overlay 프레임 수({overlayFrames.arraySize})가 본체 프레임 수({frames.arraySize})와 다릅니다. " +
                    "런타임과 Preview는 범위 밖 인덱스를 '오버레이 없음'으로 처리합니다. Match Overlay Length로 맞출 수 있습니다.",
                    MessageType.Warning);
            }
        }

        private ReorderableList BuildFrameList(SerializedObject owner, SerializedProperty frames, SerializedProperty hitFrame, SerializedProperty castFrame = null, SerializedProperty overlayFrames = null)
        {
            bool hasOverlayColumn = overlayFrames != null;
            var list = new ReorderableList(owner, frames, true, true, true, true) { elementHeight = 48f };
            list.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, "Sprite Frames");
                if (!hasOverlayColumn) return;
                // 헤더 rect는 행 rect와 좌우 여백이 달라 열 위치를 정확히 맞출 수 없다 - 각 행이 왼쪽
                // Actor, 오른쪽 Overlay 순서라는 것만 알려주는 힌트로 둔다.
                EditorGUI.LabelField(new Rect(rect.xMax - 168f, rect.y, 166f, rect.height),
                    "Actor  |  Frame Overlay", EditorStyles.miniLabel);
            };
            list.drawElementCallback = (rect, index, active, focused) =>
            {
                SerializedProperty element = frames.GetArrayElementAtIndex(index);
                Sprite sprite = element.objectReferenceValue as Sprite;
                bool isHit = hitFrame != null && hitFrame.intValue == index;
                bool isCast = castFrame != null && castFrame.intValue == index;
                if (isHit || isCast) EditorGUI.DrawRect(rect, new Color(1f, 0.3f, 0.2f, 0.15f));
                FrameRow row = LayoutFrameRow(rect, hasOverlayColumn);
                DrawThumbnail(row.ActorThumb, sprite);
                EditorGUI.ObjectField(row.ActorField, element, typeof(Sprite), GUIContent.none);
                if (hasOverlayColumn)
                {
                    // 같은 인덱스가 곧 오버레이 쌍이다. 오버레이 배열이 짧아 인덱스가 없으면 그 자리는
                    // "오버레이 없음"이라 비활성 슬롯으로 보여준다(본체 프레임은 그대로 유지된다).
                    if (index < overlayFrames.arraySize)
                    {
                        SerializedProperty overlayElement = overlayFrames.GetArrayElementAtIndex(index);
                        DrawThumbnail(row.OverlayThumb, overlayElement.objectReferenceValue as Sprite);
                        EditorGUI.ObjectField(row.OverlayField, overlayElement, typeof(Sprite), GUIContent.none);
                    }
                    else
                    {
                        DrawThumbnail(row.OverlayThumb, null);
                        using (new EditorGUI.DisabledScope(true))
                            EditorGUI.LabelField(row.OverlayField, "—", EditorStyles.miniLabel);
                    }
                }
                string tagText = isCast && isHit ? "CAST · HIT" : isHit ? "HIT" : isCast ? "CAST" : "#" + index;
                GUIStyle tagStyle = isCast && isHit ? CastHitTagStyle : isHit ? HitTagStyle : isCast ? CastTagStyle : EditorStyles.miniLabel;
                EditorGUI.LabelField(row.Tag, tagText, tagStyle);
            };
            if (hasOverlayColumn)
            {
                // 평행 배열이므로 인덱스 정합성은 에디터가 책임진다 - 본체에 가한 추가/삭제/재정렬을
                // 오버레이 배열에도 같은 인덱스로 그대로 적용한다. 단 오버레이가 아예 비어 있으면
                // ("이 공격에는 오버레이 없음") 본체 편집만으로 길이가 생기지 않도록 그대로 둔다.
                list.onAddCallback = l =>
                {
                    AppendObjectReference(frames, null);
                    if (overlayFrames.arraySize > 0) AppendObjectReference(overlayFrames, null);
                };
                list.onRemoveCallback = l =>
                {
                    int index = l.index >= 0 && l.index < frames.arraySize ? l.index : frames.arraySize - 1;
                    if (index < 0) return;
                    RemoveArrayElementAt(frames, index);
                    RemoveArrayElementAt(overlayFrames, index);
                    l.index = Mathf.Clamp(index, 0, frames.arraySize - 1);
                };
                list.onReorderCallbackWithDetails = (l, oldIndex, newIndex) =>
                {
                    if (overlayFrames.arraySize == 0) return;
                    // 길이가 어긋나 있으면 옮길 인덱스가 아예 없을 수 있다 - 먼저 본체 길이까지 null로
                    // 늘려서 모든 인덱스에 짝을 만들어 두고, 그 다음 본체와 같은 이동을 그대로 적용한다.
                    // 이렇게 해야 길이가 다른 상태에서 재정렬해도 기존 대응 관계가 깨지지 않는다.
                    PadArrayWithNulls(overlayFrames, frames.arraySize);
                    if (oldIndex >= overlayFrames.arraySize || newIndex >= overlayFrames.arraySize) return;
                    overlayFrames.MoveArrayElement(oldIndex, newIndex);
                };
            }
            list.onSelectCallback = l =>
            {
                previewPlaying = false;
                previewFrameIndex = Mathf.Max(0, l.index);
                previewElapsedTime = previewFrameIndex / Mathf.Max(0.01f, GetPreviewFps());
            };
            return list;
        }

        /// <summary>Sprite Frames 한 행의 열 배치. 헤더 라벨과 각 행이 항상 같은 x/width를 쓰도록 한 곳에서
        /// 계산한다. 오버레이 열이 없으면 본체 필드가 그 공간을 그대로 차지한다.</summary>
        private struct FrameRow
        {
            public Rect ActorThumb;
            public Rect ActorField;
            public Rect OverlayThumb;
            public Rect OverlayField;
            public Rect Tag;
        }

        private static FrameRow LayoutFrameRow(Rect rect, bool hasOverlay)
        {
            const float thumbSize = 40f;
            const float tagWidth = 84f;
            float line = EditorGUIUtility.singleLineHeight;
            var row = new FrameRow
            {
                ActorThumb = new Rect(rect.x + 2f, rect.y + 4f, thumbSize, thumbSize),
                Tag = new Rect(rect.xMax - tagWidth - 2f, rect.y + 14f, tagWidth, line)
            };

            float fieldsLeft = rect.x + 2f + thumbSize + 6f;
            float fieldsWidth = Mathf.Max(60f, row.Tag.x - 6f - fieldsLeft);
            if (!hasOverlay)
            {
                row.ActorField = new Rect(fieldsLeft, rect.y + 14f, fieldsWidth, line);
                return row;
            }

            // 남은 폭을 [본체 필드][간격][오버레이 썸네일][간격][오버레이 필드]로 나눈다.
            float fieldWidth = Mathf.Max(40f, (fieldsWidth - thumbSize - 12f) * 0.5f);
            row.ActorField = new Rect(fieldsLeft, rect.y + 14f, fieldWidth, line);
            row.OverlayThumb = new Rect(row.ActorField.xMax + 6f, rect.y + 4f, thumbSize, thumbSize);
            row.OverlayField = new Rect(row.OverlayThumb.xMax + 6f, rect.y + 14f, fieldWidth, line);
            return row;
        }

        /// <summary>오브젝트 참조 배열에서 index를 실제로 한 칸 제거한다. SerializedProperty는 참조가 남아
        /// 있으면 DeleteArrayElementAtIndex가 값만 null로 만들고 길이를 줄이지 않으므로 먼저 비운다.</summary>
        private static void RemoveArrayElementAt(SerializedProperty array, int index)
        {
            if (array == null || index < 0 || index >= array.arraySize) return;
            SerializedProperty element = array.GetArrayElementAtIndex(index);
            if (element.propertyType == SerializedPropertyType.ObjectReference) element.objectReferenceValue = null;
            array.DeleteArrayElementAtIndex(index);
        }

        private static void DrawThumbnail(Rect rect, Sprite sprite)
        {
            if (sprite == null)
            {
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.15f));
                return;
            }
            Texture2D preview = AssetPreview.GetAssetPreview(sprite) ?? AssetPreview.GetMiniThumbnail(sprite);
            if (preview != null) GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
        }

        /// <summary>Drop Zone 하나를 그리고, 이번 이벤트에서 드롭이 완료됐다면 드롭된 Sprite를 드롭 순서대로
        /// 돌려준다(그 외에는 null). 배열에 어떻게 반영할지는 - append냐 replace냐, 짝 배열도 함께 늘릴지 -
        /// 호출한 쪽이 정한다.</summary>
        private static List<Sprite> DrawSpriteDropZone(string label)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, label, EditorStyles.helpBox);
            Event evt = Event.current;
            if (!rect.Contains(evt.mousePosition) || (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)) return null;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            List<Sprite> dropped = null;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                dropped = new List<Sprite>();
                foreach (UnityEngine.Object item in DragAndDrop.objectReferences)
                {
                    if (item is Sprite sprite) dropped.Add(sprite);
                    else
                    {
                        string path = AssetDatabase.GetAssetPath(item);
                        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        {
                            if (asset is Sprite subSprite) dropped.Add(subSprite);
                        }
                    }
                }
            }
            evt.Use();
            return dropped;
        }

        /// <summary>배열 길이가 length에 못 미치면 그만큼 null 요소를 덧붙인다(이미 길면 자르지 않는다).</summary>
        private static void PadArrayWithNulls(SerializedProperty array, int length)
        {
            while (array.arraySize < length) AppendObjectReference(array, null);
        }

        private static void AppendObjectReference(SerializedProperty array, UnityEngine.Object value)
        {
            int index = array.arraySize;
            array.arraySize++;
            array.GetArrayElementAtIndex(index).objectReferenceValue = value;
        }

        private void RebuildFrameList()
        {
            frameList = null;
            frameListOwner = null;
            frameListPropertyPath = null;
        }

        private void DrawPersistentPreview()
        {
            // 누적 입력 공격을 보고 있을 때만 시뮬레이션 readout + 조작 버튼 두 줄이 더 붙는다.
            AttackMotionDefinition accumulatedAttack = GetAccumulatedPreviewAttack();
            float extraHeight = accumulatedAttack != null ? 40f : 0f;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(LeftWorkspaceWidth), GUILayout.Height(StageHeight + 116f + extraHeight)))
            {
                EditorGUILayout.LabelField("Animation Preview", EditorStyles.boldLabel);
                PreviewMotion main = GetMainPreviewMotion();
                PreviewMotion opponent = GetOpponentPreviewMotion();

                if (main == null || main.Frames.Length == 0)
                {
                    Rect emptyStage = GUILayoutUtility.GetRect(StageWidth, StageHeight, GUILayout.ExpandWidth(false));
                    DrawStageBackground(emptyStage);
                    GUI.Label(new Rect(emptyStage.x, emptyStage.y + emptyStage.height * 0.45f, emptyStage.width, 22f),
                        SelectedResource == null ? "Select a character or monster" : "No Idle frames found",
                        EditorStyles.centeredGreyMiniLabel);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Zoom", GUILayout.Width(38f));
                    previewZoom = EditorGUILayout.Slider(previewZoom, ZoomMin, ZoomMax);
                    if (GUILayout.Button("Fit", GUILayout.Width(48f))) previewZoom = ComputeActiveFitZoom();
                    debugDamageNumberOverlay = GUILayout.Toggle(debugDamageNumberOverlay,
                        new GUIContent("DMG Debug", "Damage Number Preview의 활성 상태/elapsed/duration/anchor 좌표를 화면에 표시합니다(개발용)."),
                        EditorStyles.miniButton, GUILayout.Width(74f));
                }

                DrawPairedStage(main, opponent);
                GUILayout.Space(PreviewControlSpacing);
                DrawTimelineScrubber(main, opponent);
                GUILayout.Space(PreviewControlSpacing);
                DrawPlaybackControls(main, opponent);
                GUILayout.Space(3f);
                previewFrameIndex = GetFrameIndex(main, (float)previewElapsedTime, previewLoop);
                string hitText = main.HitFrame >= 0 ? (main.HitFrame + 1).ToString() : "-";
                EditorGUILayout.LabelField($"{main.Label}  |  Frame {previewFrameIndex + 1}/{main.Frames.Length}   FPS {main.Fps:0.##}   Hit {hitText}", EditorStyles.centeredGreyMiniLabel);
                if (accumulatedAttack != null) DrawChargeSimulationControls(accumulatedAttack);
            }
        }

        private void DrawTimelineScrubber(PreviewMotion main, PreviewMotion opponent)
        {
            PreviewMotion driver = GetTimelineDriver(main, opponent);
            float duration = Mathf.Max(0.001f, GetPreviewDuration());
            float fps = driver != null ? Mathf.Max(0.01f, driver.Fps) : 6f;
            int frameCount = Mathf.Max(1, Mathf.CeilToInt(duration * fps));
            int current = Mathf.Clamp(Mathf.RoundToInt((float)previewElapsedTime * fps), 0, frameCount - 1);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                Rect sliderRect = GUILayoutUtility.GetRect(TimelineSliderWidth, 14f, GUILayout.Width(TimelineSliderWidth));
                int changed = Mathf.RoundToInt(GUI.HorizontalSlider(sliderRect, current, 0f, frameCount - 1));
                GUILayout.Label($"{current + 1}/{frameCount}", EditorStyles.miniLabel, GUILayout.Width(48f));
                GUILayout.FlexibleSpace();
                if (changed != current)
                {
                    previewPlaying = false;
                    previewElapsedTime = changed / fps;
                    previewFrameIndex = GetFrameIndex(main, (float)previewElapsedTime, false);
                    Repaint();
                }
            }
        }

        private void DrawPlaybackControls(PreviewMotion main, PreviewMotion opponent)
        {
            PreviewMotion driver = GetTimelineDriver(main, opponent);
            float fps = driver != null ? Mathf.Max(0.01f, driver.Fps) : 6f;
            int frameCount = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.001f, GetPreviewDuration()) * fps));
            int timelineFrame = Mathf.Clamp(Mathf.RoundToInt((float)previewElapsedTime * fps), 0, frameCount - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("|<", "단축키 : Shift + 왼쪽 방향키"), GUILayout.Width(30f))) SetPreviewTimelineFrame(0, fps, main);
                if (GUILayout.Button(new GUIContent("<", "단축키 : 왼쪽 방향키"), GUILayout.Width(30f))) SetPreviewTimelineFrame((timelineFrame - 1 + frameCount) % frameCount, fps, main);
                var playContent = new GUIContent(previewPlaying ? "Pause" : "Play", "단축키 : 스페이스바");
                if (GUILayout.Button(playContent, GUILayout.Width(62f))) TogglePreviewPlayback();
                if (GUILayout.Button(new GUIContent("Stop", "단축키 : Shift + 스페이스바"), GUILayout.Width(50f))) StopPreview();
                if (GUILayout.Button(new GUIContent(">", "단축키 : 오른쪽 방향키"), GUILayout.Width(30f))) SetPreviewTimelineFrame((timelineFrame + 1) % frameCount, fps, main);
                if (GUILayout.Button(new GUIContent(">|", "단축키 : Shift + 오른쪽 방향키"), GUILayout.Width(30f))) SetPreviewTimelineFrame(frameCount - 1, fps, main);
                previewLoop = GUILayout.Toggle(previewLoop, new GUIContent("Loop", "단축키 : X"), GUILayout.Width(54f));
                GUILayout.FlexibleSpace();
            }
        }

        private static PreviewMotion GetTimelineDriver(PreviewMotion main, PreviewMotion opponent)
        {
            return main != null && main.Kind == PreviewMotionKind.Hit && opponent != null && opponent.Kind == PreviewMotionKind.Attack
                ? opponent
                : main;
        }

        private void SetPreviewTimelineFrame(int index, float fps, PreviewMotion main)
        {
            previewPlaying = false;
            chargeSimRunning = false; // 손으로 프레임을 잡으면 시뮬레이션이 그 위를 덮어쓰지 않게 멈춘다.
            previewElapsedTime = Mathf.Max(0, index) / Mathf.Max(0.01f, fps);
            previewFrameIndex = GetFrameIndex(main, (float)previewElapsedTime, false);
            Repaint();
        }

        private void DrawPairedStage(PreviewMotion main, PreviewMotion opponent)
        {
            Rect stage = GUILayoutUtility.GetRect(StageWidth, StageHeight, GUILayout.ExpandWidth(false));
            DrawStageBackground(stage);
            GUI.BeginGroup(stage);

            ResourceEntry character = actorKind == ActorKind.Character ? SelectedResource : GetSelectedPreviewTarget();
            ResourceEntry monster = actorKind == ActorKind.Monster ? SelectedResource : GetSelectedPreviewTarget();
            List<PreviewMotion> characterChoices = BuildPreviewMotions(character);
            List<PreviewMotion> monsterChoices = BuildPreviewMotions(monster);
            DrawMotionChoiceButtons(new Rect(8f, 8f, 170f, 126f), characterChoices, ActorKind.Character);
            DrawMotionChoiceButtons(new Rect(stage.width - 178f, 8f, 170f, 126f), monsterChoices, ActorKind.Monster);

            PreviewMotion characterMotion = actorKind == ActorKind.Character ? main : opponent;
            PreviewMotion monsterMotion = actorKind == ActorKind.Monster ? main : opponent;
            PreviewMotion attack = characterMotion != null && characterMotion.Kind == PreviewMotionKind.Attack ? characterMotion : null;
            bool synchronizedHit = attack != null && monsterMotion != null && monsterMotion.Kind == PreviewMotionKind.Hit;
            float time = (float)previewElapsedTime;
            float hitTime = attack != null ? Mathf.Clamp(attack.HitFrame, 0, Mathf.Max(0, attack.Frames.Length - 1)) / Mathf.Max(0.01f, attack.Fps) : float.MaxValue;
            bool hitStarted = synchronizedHit && time >= hitTime;
            // Attack과 짝지어진 상태면 Hit Frame 도달 시점부터, 몬스터 Hit 모션 자체를 단독으로 보고
            // 있으면(짝 없이 Hit 탭만 재생) 그 클립 재생 시작(0)부터를 "피격 반응 중"으로 본다.
            bool monsterInHitReaction = hitStarted || (!synchronizedHit && monsterMotion != null && monsterMotion.Kind == PreviewMotionKind.Hit);
            float shakeStartTime = synchronizedHit ? hitTime : 0f;

            Sprite characterSprite = GetFrame(characterMotion, time, characterMotion != null && characterMotion.Kind == PreviewMotionKind.Idle);
            Sprite monsterSprite;
            if (hitStarted)
            {
                monsterSprite = GetFrame(monsterMotion, time - hitTime, false);
            }
            else if (synchronizedHit)
            {
                PreviewMotion idle = FindFirstMotion(monsterChoices, PreviewMotionKind.Idle);
                monsterSprite = GetFrame(idle, time, true);
            }
            else
            {
                monsterSprite = GetFrame(monsterMotion, time, monsterMotion != null && monsterMotion.Kind == PreviewMotionKind.Idle);
            }

            float groundY = stage.height * GroundRatio;
            Vector2 baseAnchor = new Vector2(stage.width * 0.38f, groundY);
            float ppu = characterSprite != null ? characterSprite.pixelsPerUnit : 100f;
            float worldToScreen = ppu * previewZoom;

            Vector2 characterActorOffset = Vector2.zero;
            float characterScale = 1f;
            float moveDistance = 0f;
            float moveOut = 0.14f;
            float moveBack = 0.05f;

            if (character?.CharacterProfile != null)
            {
                CharacterMotionProfile.PreviewSettings preview = character.CharacterProfile.Preview;
                characterActorOffset = preview.ActorOffset;
                characterScale = preview.ActorScale;
                CharacterMotionProfile.AttackMovementSettings movement = character.CharacterProfile.AttackMovement;
                moveDistance = movement.MoveDistance;
                moveOut = movement.MoveOutDuration;
                moveBack = movement.MoveBackDuration;
            }

            Vector2 receiveOffset = new Vector2(0f, 0.35f);
            Vector2 monsterActorOffset = Vector2.zero;
            float monsterScale = 1f;
            bool monsterFlipX = false;
            float shakeOffsetX = 0f;
            if (monster?.MonsterProfile != null)
            {
                receiveOffset = monster.MonsterProfile.Preview.ReceivePointOffset;
                monsterActorOffset = monster.MonsterProfile.Preview.ActorOffset;
                monsterScale = monster.MonsterProfile.Preview.ActorScale;
                monsterFlipX = monster.MonsterProfile.SpriteFlipX;
                shakeOffsetX = EvaluateHitShake(monster.MonsterProfile.HitReaction, time, shakeStartTime, monsterInHitReaction);
            }

            // 런타임(AttackMovement/TargetCombatController)과 같은 공식:
            //   characterPosition = characterSlot + characterProfile.ActorOffset + attackMovementOffset
            //   monsterPosition   = monsterSlot   + monsterProfile.ActorOffset   + hitShakeOffset
            CombatStageLayout layout = GetStageLayout();
            Vector2 characterSlot = layout.CharacterSlotPosition;
            Vector2 monsterSlot = layout.MonsterSlotPosition;

            float moveX = attack != null ? EvaluateMovement(time, moveDistance, moveOut, moveBack) : 0f;
            Vector2 characterAnchor = baseAnchor + WorldToScreen(characterSlot + characterActorOffset + new Vector2(moveX, 0f), worldToScreen);
            // Shake는 targetAnchor 하나에만 더한다 - Receive Point/Hit Effect/Marker가 모두 targetAnchor를
            // 기준으로 계산되므로(아래), 스프라이트를 포함해 전부 함께 흔들린 뒤의 위치로 그려진다.
            Vector2 targetAnchor = baseAnchor + WorldToScreen(monsterSlot + monsterActorOffset + new Vector2(shakeOffsetX, 0f), worldToScreen);
            Vector2 receivePoint = targetAnchor + WorldToScreen(receiveOffset, worldToScreen);

            if (monsterSprite != null) DrawSprite(monsterSprite, previewZoom * monsterScale, targetAnchor, hitStarted ? new Color(1f, 0.68f, 0.68f) : Color.white, monsterFlipX);
            if (characterSprite != null) DrawSprite(characterSprite, previewZoom * characterScale, characterAnchor, Color.white);

            // Frame-synced Overlay: 별도 시간 계산 없이 공격 본체가 지금 보여주는 프레임 인덱스를 그대로
            // 써서 오버레이 프레임을 고른다(스크럽/한 프레임 이동/Play 모두 같은 인덱스를 쓴다). 오버레이는
            // 본체와 같은 캔버스/Pivot/PPU로 제작하는 것이 전제라 캐릭터와 완전히 같은 anchor/zoom/scale로
            // 겹치고 별도 Offset/Scale을 주지 않는다. 그리기 순서는 Monster → Character → Frame Overlay →
            // Cast/Hit Presentation이다.
            Sprite overlaySprite = GetAttackOverlayFrame(attack, time);
            if (overlaySprite != null) DrawSprite(overlaySprite, previewZoom * characterScale, characterAnchor, Color.white);

            // Projectile Preview: 런타임과 마찬가지로 Cast 순간의 캐릭터 위치에서 시작점을 스냅샷으로
            // 잡고, 몬스터의 Receive Point + 공격별 Hit Effect Offset을 도착점으로 쓴다. Launch Offset은
            // 캐릭터 Actor Scale을 상속하고, 도착점의 자식 앵커/오프셋은 몬스터 Actor Scale을 상속한다.
            // 경로와 Launch Point는 타임라인 위치와 무관하게 항상 보여서 X/Y를 편집하며 바로 맞출 수 있다.
            if (attack?.Attack != null && attack.Attack.ProjectilePrefab != null && monsterSprite != null)
            {
                float castTime = Mathf.Clamp(attack.Attack.CastFrameIndex, 0, Mathf.Max(0, attack.Frames.Length - 1))
                                 / Mathf.Max(0.01f, attack.Fps);
                float projectileHitTime = Mathf.Clamp(attack.HitFrame, 0, Mathf.Max(0, attack.Frames.Length - 1))
                                          / Mathf.Max(0.01f, attack.Fps);
                float moveXAtCast = EvaluateMovement(castTime, moveDistance, moveOut, moveBack);
                Vector2 castCharacterAnchor = baseAnchor + WorldToScreen(
                    characterSlot + characterActorOffset + new Vector2(moveXAtCast, 0f), worldToScreen);
                Vector2 launchPoint = castCharacterAnchor + WorldToScreen(
                    attack.Attack.ProjectileLaunchOffset * characterScale, worldToScreen);
                Vector2 projectileTarget = baseAnchor + WorldToScreen(
                    monsterSlot + monsterActorOffset
                    + (receiveOffset + attack.Attack.HitEffectOffset) * monsterScale,
                    worldToScreen);

                DrawProjectilePreview(attack.Attack, time, castTime, projectileHitTime, launchPoint,
                    projectileTarget, worldToScreen);
            }

            // Receive Point 강조와 "HIT FRAME" 라벨은 예전처럼 정확히 그 프레임일 때만 켠다 - 이건
            // 연출이 아니라 편집 보조 표시라서 이펙트 재생 구간 내내 떠 있으면 안 된다.
            bool exactHit = attack != null && GetFrameIndex(attack, time, false) == Mathf.Clamp(attack.HitFrame, 0, attack.Frames.Length - 1);

            // Cast/Hit Effect Preview: 런타임과 같은 규칙으로 "그 프레임에 도달한 순간부터 이펙트 자신의
            // 길이만큼" 재생한다. 예전에는 정확히 그 프레임일 때만 프리팹의 SpriteRenderer에 꽂힌 그림
            // 한 장을 그려서, 6프레임짜리 이펙트도 0번 프레임이 한 순간 스쳐 지나갈 뿐이었다 - 이펙트가
            // 실제로 어떻게 터지는지 에디터에서 볼 수 없었다.
            if (attack?.Attack != null && attack.Frames.Length > 0)
            {
                float fps = Mathf.Max(0.01f, attack.Fps);

                float castStartTime = Mathf.Clamp(attack.Attack.CastFrameIndex, 0, attack.Frames.Length - 1) / fps;
                DrawEffectPreview(attack.Attack.CastEffectPrefab, time - castStartTime, attack.Attack.CastEffectScale,
                    characterAnchor + WorldToScreen(attack.Attack.CastEffectOffset, worldToScreen), previewZoom);

                float effectHitTime = Mathf.Clamp(attack.HitFrame, 0, attack.Frames.Length - 1) / fps;
                Vector2 hitEffectAnchor = receivePoint + WorldToScreen(attack.Attack.HitEffectOffset, worldToScreen);
                DrawEffectPreview(attack.Attack.HitEffectPrefab, time - effectHitTime, attack.Attack.HitEffectScale,
                    hitEffectAnchor, previewZoom);

                // 랜덤 출력 범위 가이드는 이펙트 재생 여부와 무관하게 항상 띄운다 - Effect Offset처럼
                // 타임라인 위치를 맞추지 않고도 X/Y를 편집하며 바로 확인할 수 있어야 하기 때문이다
                // (Launch Point/발사체 경로를 항상 그리는 것과 같은 규칙).
                if (attack.Attack.HitEffectPrefab != null)
                {
                    Vector2? jitterRange = ResolveEffectiveHitEffectJitter(attack.Attack);
                    if (jitterRange.HasValue) DrawJitterRangeGuide(hitEffectAnchor, jitterRange.Value, worldToScreen);
                }
            }

            // "타격 순간" 시점은 Hit Shake와 완전히 같은 규칙(shakeStartTime)을 그대로 재사용한다 -
            // Character+Monster 동기화면 attack의 Hit Frame, Monster Hit 탭 단독 프리뷰면 그 클립
            // 0번 프레임. hasHitContext는 monsterMotion이 애초에 Hit 모션을 보여주고 있는지만 본다
            // (attack 유무와 무관 - 짝지어져 있든 아니든 monsterMotion.Kind==Hit이면 참) - 순수 Idle 등
            // 히트와 무관한 프리뷰에서 우연히 time이 [0,Duration) 구간에 들어와도 뜨지 않게 막아준다.
            bool hasHitContext = monsterMotion != null && monsterMotion.Kind == PreviewMotionKind.Hit;
            bool damageNumberProfileAvailable = monster?.MonsterProfile != null;
            bool damageNumberActive = false;
            float damageNumberElapsedDebug = 0f;
            float damageNumberDurationDebug = 0f;
            Vector2 damageNumberAnchorDebug = Vector2.zero;

            if (hasHitContext && damageNumberProfileAvailable)
            {
                MonsterMotionProfile.HitReactionSettings reaction = monster.MonsterProfile.HitReaction;
                float damageNumberElapsed = time - shakeStartTime;
                float damageNumberDuration = reaction.DamageNumberDuration;
                damageNumberElapsedDebug = damageNumberElapsed;
                damageNumberDurationDebug = damageNumberDuration;

                if (damageNumberElapsed >= 0f && damageNumberElapsed < damageNumberDuration)
                {
                    damageNumberActive = true;

                    // 시작 위치는 "타격 순간"에 고정해서 스냅샷을 뜬다 - 실제 DamageNumberPopup도 Spawn
                    // 시점의 transform.position만 한 번 잡고 그 뒤로는 몬스터의 Shake를 더 이상 따라가지
                    // 않는다(RiseAndFade가 자기만의 start/end 사이를 보간할 뿐이다). 타격 순간의 Shake
                    // Offset은 사인 함수가 0에서 시작하므로 항상 0이지만, 공식을 그대로 재사용해 계산의
                    // 의미를 분명히 하고 나중에 Shake 수식이 바뀌어도 깨지지 않게 한다. Jitter는 매
                    // 프레임 달라지면 안 되므로(결정론적 요구사항) Preview에서는 항상 0을 쓴다.
                    float shakeOffsetAtHit = EvaluateHitShake(reaction, shakeStartTime, shakeStartTime, true);
                    Vector2 startAnchor = baseAnchor + WorldToScreen(
                        monsterSlot + monsterActorOffset + new Vector2(shakeOffsetAtHit, 0f) + reaction.DamageNumberOffset,
                        worldToScreen);

                    float t = Mathf.Clamp01(damageNumberElapsed / damageNumberDuration);
                    Vector2 riseOffset = WorldToScreen(new Vector2(0f, reaction.DamageNumberRiseDistance * t), worldToScreen);
                    Vector2 damageNumberAnchor = startAnchor + riseOffset;
                    damageNumberAnchorDebug = damageNumberAnchor;

                    Color color = reaction.DamageNumberTextColor;
                    color.a = 1f - t; // 실제 DamageNumberPopup.RiseAndFade()와 동일한 페이드 규칙

                    // 버그의 실제 원인: reaction.DamageNumberFontSize는 TMP(월드 스페이스) 폰트 크기
                    // 단위다(예: Scarecrow는 씬 값을 그대로 옮겨서 3) - 이 값을 IMGUI GUIStyle.fontSize에
                    // 그대로 넣으면 "3픽셀 글자"가 되어 사실상 안 보인다(렌더링 조건은 맞았지만 크기가
                    // 문제였다). previewZoom을 곱해 확대/축소에 자연스럽게 반응하게 하되, 항상 최소
                    // 가독 크기(10px)를 보장하고 너무 커지지 않게(48px) 막는다.
                    float pixelFontSize = Mathf.Clamp(reaction.DamageNumberFontSize * previewZoom * 2f, 10f, 48f);
                    DrawDamageNumberPreview(damageNumberAnchor, color, pixelFontSize);
                }
            }

            if (debugDamageNumberOverlay)
            {
                DrawDamageNumberDebugOverlay(stage, hasHitContext, damageNumberProfileAvailable, damageNumberActive,
                    damageNumberElapsedDebug, damageNumberDurationDebug, damageNumberAnchorDebug);
            }

            DrawMarker(characterAnchor, new Color(1f, 0.9f, 0.1f));
            if (monsterSprite != null)
            {
                DrawMarker(targetAnchor, new Color(0.2f, 0.8f, 1f));
                DrawReceivePoint(receivePoint, exactHit);
            }
            if (exactHit) EditorGUI.LabelField(new Rect(0f, 4f, stage.width, 20f), "HIT FRAME", HitLabelStyle);
            DrawInlineTargetSelector(stage);
            GUI.EndGroup();
        }

        private void DrawInlineTargetSelector(Rect stage)
        {
            List<ResourceEntry> targets = GetPreviewTargets();
            string targetName = targets.Count > 0
                ? targets[Mathf.Clamp(selectedPreviewTargetIndex, 0, targets.Count - 1)].Name
                : "없음";
            const float buttonWidth = 154f;
            const float buttonHeight = 22f;
            Rect button = new Rect(8f, stage.height - buttonHeight - 8f, buttonWidth, buttonHeight);
            if (GUI.Button(button, $"대상 : {targetName}", EditorStyles.miniButton))
            {
                targetDropdownOpen = !targetDropdownOpen;
            }

            if (!targetDropdownOpen) return;
            float listHeight = Mathf.Min(150f, Mathf.Max(28f, targets.Count * 23f + 6f));
            Rect panel = new Rect(button.x, button.y - listHeight - 3f, button.width, listHeight);
            GUI.Box(panel, GUIContent.none, EditorStyles.helpBox);

            Rect inner = new Rect(panel.x + 3f, panel.y + 3f, panel.width - 6f, panel.height - 6f);
            float contentHeight = Mathf.Max(inner.height, targets.Count * 23f);
            targetDropdownScroll = GUI.BeginScrollView(inner, targetDropdownScroll,
                new Rect(0f, 0f, inner.width - 14f, contentHeight), false, contentHeight > inner.height);
            if (targets.Count == 0)
            {
                GUI.Label(new Rect(4f, 3f, inner.width - 8f, 20f), "선택 가능한 대상 없음", EditorStyles.centeredGreyMiniLabel);
            }
            for (int i = 0; i < targets.Count; i++)
            {
                Color old = GUI.backgroundColor;
                if (i == selectedPreviewTargetIndex) GUI.backgroundColor = new Color(0.35f, 0.72f, 1f);
                if (GUI.Button(new Rect(0f, i * 23f, inner.width - 16f, 21f), targets[i].Name, EditorStyles.miniButton))
                {
                    selectedPreviewTargetIndex = i;
                    targetDropdownOpen = false;
                    SelectDefaultOpponentMotion();
                    RestartPreview();
                }
                GUI.backgroundColor = old;
            }
            GUI.EndScrollView();

            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && !panel.Contains(evt.mousePosition) && !button.Contains(evt.mousePosition))
            {
                targetDropdownOpen = false;
                evt.Use();
                Repaint();
            }
        }

        private List<ResourceEntry> GetPreviewTargets()
        {
            return actorKind == ActorKind.Character ? previewMonsters : previewCharacters;
        }

        private ResourceEntry GetSelectedPreviewTarget()
        {
            List<ResourceEntry> targets = GetPreviewTargets();
            return targets.Count == 0 ? null : targets[Mathf.Clamp(selectedPreviewTargetIndex, 0, targets.Count - 1)];
        }

        private PreviewMotion GetMainPreviewMotion()
        {
            if (SelectedResource == null) return null;
            if (workspace == Workspace.Attack && selectedAttack != null) return CreateAttackMotion(selectedAttack);

            SerializedProperty clip = GetActiveClip();
            if (clip != null)
            {
                PreviewMotionKind kind = workspace == Workspace.Hit ? PreviewMotionKind.Hit
                    : workspace == Workspace.Defeat ? PreviewMotionKind.Defeat
                    : workspace == Workspace.IdleEvents ? PreviewMotionKind.IdleEvent
                    : PreviewMotionKind.Idle;
                return CreateMotion(clip, kind);
            }

            return new PreviewMotion
            {
                Label = "Base Idle",
                Kind = PreviewMotionKind.Idle,
                Frames = rawIdlePreviewFrames.ToArray(),
                Fps = 6f
            };
        }

        /// <summary>DrawPairedStage의 캐릭터 측 공격 모션 판정과 동일한 규칙(actorKind에 따라 main/opponent
        /// 중 캐릭터 쪽을 고르고, Kind가 Attack일 때만 반환)으로 "지금 재생 중인 공격"을 찾는다 - 오디오
        /// Cue 판정(OnEditorUpdate/TogglePreviewPlayback)이 그리기 코드와 다른 판정을 하지 않도록 공유한다.</summary>
        private PreviewMotion GetActivePreviewAttackMotion()
        {
            PreviewMotion main = GetMainPreviewMotion();
            PreviewMotion opponent = GetOpponentPreviewMotion();
            PreviewMotion characterMotion = actorKind == ActorKind.Character ? main : opponent;
            return characterMotion != null && characterMotion.Kind == PreviewMotionKind.Attack ? characterMotion : null;
        }

        private PreviewMotion GetOpponentPreviewMotion()
        {
            List<PreviewMotion> motions = BuildPreviewMotions(GetSelectedPreviewTarget());
            if (motions.Count == 0) return null;
            selectedOpponentMotionIndex = Mathf.Clamp(selectedOpponentMotionIndex, 0, motions.Count - 1);
            return motions[selectedOpponentMotionIndex];
        }

        private List<PreviewMotion> BuildPreviewMotions(ResourceEntry entry)
        {
            var result = new List<PreviewMotion>();
            if (entry == null) return result;
            if (entry.CharacterProfile != null)
            {
                AddCharacterClip(result, entry.CharacterProfile.BaseIdle, PreviewMotionKind.Idle);
                foreach (CharacterMotionProfile.FrameClip clip in entry.CharacterProfile.IdleEvents)
                    AddCharacterClip(result, clip, PreviewMotionKind.IdleEvent);
                var seen = new HashSet<AttackMotionDefinition>();
                AddPoolMotions(result, entry.CharacterProfile.Tier1Pool, seen);
                AddPoolMotions(result, entry.CharacterProfile.Tier2Pool, seen);
                AddPoolMotions(result, entry.CharacterProfile.Tier3Pool, seen);
            }
            else if (entry.MonsterProfile != null)
            {
                AddMonsterClip(result, entry.MonsterProfile.BaseIdle, PreviewMotionKind.Idle);
                foreach (MonsterMotionProfile.FrameClip clip in entry.MonsterProfile.IdleEvents)
                    AddMonsterClip(result, clip, PreviewMotionKind.IdleEvent);
                AddMonsterClip(result, entry.MonsterProfile.Hit, PreviewMotionKind.Hit);
                AddMonsterClip(result, entry.MonsterProfile.Defeat, PreviewMotionKind.Defeat);
            }

            if (result.Count == 0)
            {
                AddRawMotion(result, entry, "idle", "Base Idle", PreviewMotionKind.Idle, 6f);
                if (entry.Kind == ActorKind.Character)
                {
                    foreach (string folder in AssetDatabase.GetSubFolders(entry.FolderPath))
                    {
                        string name = Path.GetFileName(folder);
                        if (name.StartsWith("attack", StringComparison.OrdinalIgnoreCase))
                            AddRawMotion(result, folder, ToDisplayName(name), PreviewMotionKind.Attack, 18f);
                    }
                }
                else
                {
                    AddRawMotion(result, entry, "hit", "Hit", PreviewMotionKind.Hit, 6f);
                    AddRawMotion(result, entry, "defeat", "Defeat", PreviewMotionKind.Defeat, 6f);
                }
            }
            return result;
        }

        private static void AddCharacterClip(List<PreviewMotion> result, CharacterMotionProfile.FrameClip clip, PreviewMotionKind kind)
        {
            if (clip == null || clip.Frames.Length == 0) return;
            result.Add(new PreviewMotion { Label = clip.DisplayName, Kind = kind, Frames = clip.Frames, Fps = clip.AnimationFps });
        }

        private static void AddMonsterClip(List<PreviewMotion> result, MonsterMotionProfile.FrameClip clip, PreviewMotionKind kind)
        {
            if (clip == null || clip.Frames.Length == 0) return;
            result.Add(new PreviewMotion { Label = clip.DisplayName, Kind = kind, Frames = clip.Frames, Fps = clip.AnimationFps });
        }

        private static void AddPoolMotions(List<PreviewMotion> result, ComboTierAttackPool pool, HashSet<AttackMotionDefinition> seen)
        {
            if (pool == null) return;
            foreach (AttackMotionDefinition attack in pool.Motions)
            {
                if (attack == null || attack.Frames.Length == 0 || !seen.Add(attack)) continue;
                result.Add(CreateAttackMotion(attack));
            }
        }

        private static PreviewMotion CreateAttackMotion(AttackMotionDefinition attack)
        {
            return attack == null ? null : new PreviewMotion
            {
                Label = attack.name,
                Kind = PreviewMotionKind.Attack,
                Frames = attack.Frames,
                Fps = Mathf.Max(0.01f, attack.AnimationFps),
                HitFrame = attack.HitFrameIndex,
                Attack = attack
            };
        }

        private static PreviewMotion CreateMotion(SerializedProperty clip, PreviewMotionKind kind)
        {
            SerializedProperty name = clip.FindPropertyRelative("displayName");
            SerializedProperty frames = clip.FindPropertyRelative("frames");
            SerializedProperty fps = clip.FindPropertyRelative("animationFps");
            return new PreviewMotion
            {
                Label = string.IsNullOrWhiteSpace(name.stringValue) ? kind.ToString() : name.stringValue,
                Kind = kind,
                Frames = ReadSpriteArray(frames),
                Fps = Mathf.Max(0.01f, fps.floatValue)
            };
        }

        private static Sprite[] ReadSpriteArray(SerializedProperty frames)
        {
            if (frames == null) return Array.Empty<Sprite>();
            var sprites = new Sprite[frames.arraySize];
            for (int i = 0; i < frames.arraySize; i++) sprites[i] = frames.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
            return sprites;
        }

        private static void AddRawMotion(List<PreviewMotion> result, ResourceEntry entry, string folderName, string label, PreviewMotionKind kind, float fps)
        {
            string folder = FindMotionFolder(entry.FolderPath, folderName);
            AddRawMotion(result, folder, label, kind, fps);
        }

        private static void AddRawMotion(List<PreviewMotion> result, string folder, string label, PreviewMotionKind kind, float fps)
        {
            List<Sprite> sprites = LoadSprites(folder);
            if (sprites.Count == 0) return;
            result.Add(new PreviewMotion { Label = label, Kind = kind, Frames = sprites.ToArray(), Fps = fps, HitFrame = kind == PreviewMotionKind.Attack ? 1 : -1 });
        }

        private static PreviewMotion FindFirstMotion(List<PreviewMotion> motions, PreviewMotionKind kind)
        {
            return motions.Find(motion => motion.Kind == kind);
        }

        private void SelectDefaultOpponentMotion()
        {
            List<PreviewMotion> motions = BuildPreviewMotions(GetSelectedPreviewTarget());
            PreviewMotionKind desired = actorKind == ActorKind.Character && workspace == Workspace.Attack
                ? PreviewMotionKind.Hit
                : actorKind == ActorKind.Monster && workspace == Workspace.Hit
                    ? PreviewMotionKind.Attack
                    : PreviewMotionKind.Idle;
            int index = motions.FindIndex(motion => motion.Kind == desired);
            selectedOpponentMotionIndex = index >= 0 ? index : 0;
        }

        private void DrawMotionChoiceButtons(Rect area, List<PreviewMotion> motions, ActorKind side)
        {
            var visible = new List<PreviewMotion>();
            foreach (PreviewMotion motion in motions)
            {
                bool attackHitPair = actorKind == ActorKind.Character && workspace == Workspace.Attack
                    || actorKind == ActorKind.Monster && workspace == Workspace.Hit;
                bool relevant;
                if (attackHitPair)
                {
                    relevant = side == ActorKind.Character
                        ? motion.Kind == PreviewMotionKind.Attack
                        : motion.Kind == PreviewMotionKind.Hit;
                }
                else
                {
                    relevant = side == ActorKind.Character
                        ? motion.Kind == PreviewMotionKind.Idle || motion.Kind == PreviewMotionKind.Attack
                        : motion.Kind == PreviewMotionKind.Idle || motion.Kind == PreviewMotionKind.Hit || motion.Kind == PreviewMotionKind.Defeat;
                }
                if (relevant) visible.Add(motion);
            }

            float contentHeight = Mathf.Max(area.height, visible.Count * 21f);
            Vector2 scroll = side == ActorKind.Character ? characterMotionScroll : monsterMotionScroll;
            scroll = GUI.BeginScrollView(area, scroll, new Rect(0f, 0f, area.width - 14f, contentHeight), false, contentHeight > area.height);
            float y = 0f;
            foreach (PreviewMotion motion in visible)
            {

                bool isCurrentSide = actorKind == side;
                bool selected = isCurrentSide ? IsMainMotionSelected(motion) : IsOpponentMotionSelected(motions, motion);
                Color old = GUI.backgroundColor;
                if (selected) GUI.backgroundColor = new Color(0.35f, 0.72f, 1f);
                if (GUI.Button(new Rect(0f, y, area.width - 16f, 19f), motion.Label, EditorStyles.miniButton))
                {
                    if (isCurrentSide) SelectMainMotion(motion);
                    else
                    {
                        selectedOpponentMotionIndex = motions.IndexOf(motion);
                        RestartPreview();
                    }
                }
                GUI.backgroundColor = old;
                y += 21f;
            }
            GUI.EndScrollView();
            if (side == ActorKind.Character) characterMotionScroll = scroll;
            else monsterMotionScroll = scroll;
        }

        private bool IsMainMotionSelected(PreviewMotion motion)
        {
            if (motion.Kind == PreviewMotionKind.Attack) return workspace == Workspace.Attack && motion.Attack == selectedAttack;
            return motion.Kind == PreviewMotionKind.Idle && (workspace == Workspace.Idle || workspace == Workspace.Overview || workspace == Workspace.Movement)
                || motion.Kind == PreviewMotionKind.Hit && workspace == Workspace.Hit
                || motion.Kind == PreviewMotionKind.Defeat && workspace == Workspace.Defeat;
        }

        private bool IsOpponentMotionSelected(List<PreviewMotion> motions, PreviewMotion motion)
        {
            return motions.IndexOf(motion) == Mathf.Clamp(selectedOpponentMotionIndex, 0, Mathf.Max(0, motions.Count - 1));
        }

        private void SelectMainMotion(PreviewMotion motion)
        {
            if (motion.Kind == PreviewMotionKind.Attack)
            {
                workspace = Workspace.Attack;
                CharacterMotionProfile profile = SelectedResource?.CharacterProfile;
                for (int tier = 1; tier <= 3 && profile != null; tier++)
                {
                    ComboTierAttackPool pool = GetPool(profile, tier);
                    if (pool == null || !ContainsMotion(pool, motion.Attack)) continue;
                    activeTier = tier;
                    activePool = pool;
                    poolObject = new SerializedObject(pool);
                    break;
                }
                SelectAttack(motion.Attack);
                SelectDefaultOpponentMotion();
                RestartPreview();
            }
            else
            {
                workspace = motion.Kind == PreviewMotionKind.Hit ? Workspace.Hit
                    : motion.Kind == PreviewMotionKind.Defeat ? Workspace.Defeat
                    : Workspace.Idle;
                SelectAttack(null);
                RebuildFrameList();
                SelectDefaultOpponentMotion();
                RestartPreview();
            }
        }

        private static bool ContainsMotion(ComboTierAttackPool pool, AttackMotionDefinition attack)
        {
            foreach (AttackMotionDefinition candidate in pool.Motions)
                if (candidate == attack) return true;
            return false;
        }

        private static Sprite GetFrame(PreviewMotion motion, float time, bool loop)
        {
            if (motion == null || motion.Frames.Length == 0) return null;
            return motion.Frames[GetFrameIndex(motion, time, loop)];
        }

        /// <summary>런타임 PlayerCharacterAnimator.ApplyAttackOverlayFrame과 같은 규칙 - 본체 프레임
        /// 인덱스를 그대로 쓰고, 오버레이 배열이 짧아 인덱스가 범위 밖이거나 그 요소가 null이면 오버레이가
        /// 없는 프레임으로 본다.</summary>
        private static Sprite GetAttackOverlayFrame(PreviewMotion attack, float time)
        {
            if (attack?.Attack == null) return null;
            Sprite[] overlayFrames = attack.Attack.OverlayFrames;
            int index = GetFrameIndex(attack, time, false);
            return index >= 0 && index < overlayFrames.Length ? overlayFrames[index] : null;
        }

        private static int GetFrameIndex(PreviewMotion motion, float time, bool loop)
        {
            if (motion == null || motion.Frames.Length == 0) return 0;
            int index = Mathf.FloorToInt(Mathf.Max(0f, time) * Mathf.Max(0.01f, motion.Fps));
            return loop ? index % motion.Frames.Length : Mathf.Clamp(index, 0, motion.Frames.Length - 1);
        }

        private float GetPreviewDuration()
        {
            PreviewMotion main = GetMainPreviewMotion();
            PreviewMotion opponent = GetOpponentPreviewMotion();
            if (main == null) return 0f;
            PreviewMotion attack = main.Kind == PreviewMotionKind.Attack ? main : opponent != null && opponent.Kind == PreviewMotionKind.Attack ? opponent : null;
            PreviewMotion hit = main.Kind == PreviewMotionKind.Hit ? main : opponent != null && opponent.Kind == PreviewMotionKind.Hit ? opponent : null;

            // 타임라인은 "공격 모션이 끝날 때"가 아니라 "화면에서 마지막 연출이 사라질 때"까지 덮어야
            // 한다 - 공격 모션(3프레임 @18fps = 0.167초)보다 타격 이펙트(예: 6프레임 @12fps = 0.5초)가
            // 훨씬 길 수 있어서, 이걸 반영하지 않으면 폭발이 한창일 때 타임라인이 끝나 끝까지 스크럽할
            // 수 없다. Cast Effect도 같은 이유로 함께 본다.
            float presentationEnd = 0f;
            if (attack?.Attack != null && attack.Frames.Length > 0)
            {
                float fps = Mathf.Max(0.01f, attack.Fps);
                float castTime = Mathf.Clamp(attack.Attack.CastFrameIndex, 0, attack.Frames.Length - 1) / fps;
                float hitEffectTime = Mathf.Clamp(attack.HitFrame, 0, attack.Frames.Length - 1) / fps;
                presentationEnd = Mathf.Max(
                    castTime + GetEffectPreviewDuration(attack.Attack.CastEffectPrefab),
                    hitEffectTime + GetEffectPreviewDuration(attack.Attack.HitEffectPrefab));
            }

            if (attack != null && hit != null)
            {
                float hitTime = Mathf.Clamp(attack.HitFrame, 0, Mathf.Max(0, attack.Frames.Length - 1)) / Mathf.Max(0.01f, attack.Fps);
                return Mathf.Max(Mathf.Max(attack.Duration, hitTime + hit.Duration), presentationEnd);
            }
            return Mathf.Max(Mathf.Max(main.Duration, opponent?.Duration ?? 0f), presentationEnd);
        }

        private void RestartPreview()
        {
            PreviewMotion motion = GetMainPreviewMotion();
            previewElapsedTime = 0d;
            previewFrameIndex = 0;
            previewCastCueFired = false;
            previewHitCueFired = false;
            previewLoop = motion != null && motion.Kind == PreviewMotionKind.Idle;
            previewPlaying = motion != null && motion.Frames.Length > 0;
            previewLastStepTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void TogglePreviewPlayback()
        {
            // 자동 재생과 입력 시뮬레이션은 동시에 돌지 않는다 - Play를 누르면 시뮬레이션은 멈춘다.
            chargeSimRunning = false;
            if (previewPlaying)
            {
                previewPlaying = false;
                return;
            }
            float duration = GetPreviewDuration();
            if (duration <= 0f) return;
            if (previewElapsedTime >= duration) previewElapsedTime = 0d;
            previewPlaying = true;
            previewLastStepTime = EditorApplication.timeSinceStartup;
            SeedPreviewAudioCueFlags();
        }

        /// <summary>Play를 누른 시점의 previewElapsedTime을 기준으로 Cast/Hit Cue가 "이미 지나간
        /// 상태"인지 미리 판정해둔다 - 정방향 재생 중 그 지점을 실제로 통과할 때만 한 번 재생되게
        /// 하기 위함이다(이미 지난 지점에서 시작하면 이번 재생에서는 다시 울리지 않는다). Cue
        /// 시각과 정확히 같은 지점에서 시작하면 "아직 안 지남"으로 두어 이번 재생에서 정상적으로 한 번 울린다.</summary>
        private void SeedPreviewAudioCueFlags()
        {
            PreviewMotion attack = GetActivePreviewAttackMotion();
            if (attack?.Attack == null || attack.Frames.Length == 0)
            {
                previewCastCueFired = true;
                previewHitCueFired = true;
                return;
            }
            float fps = Mathf.Max(0.01f, attack.Fps);
            float castTime = Mathf.Clamp(attack.Attack.CastFrameIndex, 0, attack.Frames.Length - 1) / fps;
            float hitTime = Mathf.Clamp(attack.HitFrame, 0, attack.Frames.Length - 1) / fps;
            previewCastCueFired = previewElapsedTime > castTime;
            previewHitCueFired = previewElapsedTime > hitTime;
        }

        private void StopPreview()
        {
            previewPlaying = false;
            previewElapsedTime = 0d;
            previewFrameIndex = 0;
            ResetChargeSimulation();
            Repaint();
        }

        // ---- Accumulated Input 입력 시뮬레이션 ----

        /// <summary>지금 Preview에 걸려 있는 캐릭터 공격이 누적 입력 모드일 때만 그 에셋을 돌려준다.
        /// (Direct Input 공격이나 Idle/Hit 모션을 보고 있으면 null - 시뮬레이션 UI 자체가 뜨지 않는다.)</summary>
        private AttackMotionDefinition GetAccumulatedPreviewAttack()
        {
            AttackMotionDefinition attack = GetActivePreviewAttackMotion()?.Attack;
            return attack != null && attack.UseAccumulatedInput && attack.Frames.Length > 0 ? attack : null;
        }

        /// <summary>런타임 PlayerCharacterAnimator.AdvanceCharging과 같은 순서로 한 스텝 진행한다 -
        /// (1) 자동 입력 주입, (2) 충전 완료면 타격, (3) 유예 시간 초과분만큼 감쇠, (4) 프레임 환산.</summary>
        private void AdvanceChargeSimulation()
        {
            AttackMotionDefinition attack = GetAccumulatedPreviewAttack();
            if (attack == null)
            {
                chargeSimRunning = false;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            // 창이 잠깐 멈춰 있었어도 한 번에 몰아서 감쇠하지 않도록 delta를 제한한다.
            float delta = (float)Math.Max(0d, Math.Min(0.25d, now - chargeSimLastStepTime));
            chargeSimLastStepTime = now;

            if (chargeSimAutoInterval > 0d)
            {
                if (chargeSimNextAutoInput < now - 1d) chargeSimNextAutoInput = now; // 밀린 입력을 몰아치지 않는다
                while (now >= chargeSimNextAutoInput)
                {
                    AddChargeSimulationInput(attack);
                    chargeSimNextAutoInput += chargeSimAutoInterval;
                }
            }

            int required = attack.RequiredInputsToStrike;
            float fps = Mathf.Max(0.01f, attack.AnimationFps);
            int lastFrame = attack.Frames.Length - 1;
            int hitFrame = Mathf.Clamp(attack.HitFrameIndex, 0, lastFrame);
            int frame = 0;

            switch (chargeSimPhase)
            {
                case ChargeSimPhase.Charging:
                    if (chargeSimInputs >= required)
                    {
                        chargeSimInputs = 0f;
                        chargeSimCarried = 0f;
                        chargeSimStrikes++;
                        chargeSimPhase = ChargeSimPhase.Recovery;
                        chargeSimRecoveryTimer = 0f;
                        frame = hitFrame;
                        break;
                    }

                    if (now - chargeSimLastInputTime > attack.NoInputGraceTime)
                    {
                        chargeSimInputs = attack.ChargeDecayDuration <= 0f
                            ? 0f
                            : chargeSimInputs - required * delta / attack.ChargeDecayDuration;
                        if (chargeSimInputs <= 0f)
                        {
                            chargeSimInputs = 0f;
                            EndChargeSimCycle();
                            break;
                        }
                    }
                    frame = Mathf.Clamp(Mathf.FloorToInt(chargeSimInputs / required * hitFrame), 0, Mathf.Max(0, hitFrame - 1));
                    break;

                case ChargeSimPhase.Recovery:
                    chargeSimRecoveryTimer += delta;
                    frame = Mathf.Min(hitFrame + Mathf.FloorToInt(chargeSimRecoveryTimer * fps), lastFrame);
                    float tailStart = (lastFrame - hitFrame) / fps;
                    if (chargeSimRecoveryTimer >= tailStart + Mathf.Max(0f, attack.EndFrameDuration))
                    {
                        // 이월된 입력이 있으면 Idle을 거치지 않고 다음 충전으로 이어간다(런타임과 동일).
                        if (chargeSimCarried > 0f) BeginChargeSimCycle(attack, chargeSimCarried);
                        else EndChargeSimCycle();
                        frame = 0;
                    }
                    break;
            }

            previewPlaying = false; // 시뮬레이션과 자동 재생이 동시에 previewElapsedTime을 밀지 않게 한다.
            previewFrameIndex = Mathf.Clamp(frame, 0, lastFrame);
            previewElapsedTime = previewFrameIndex / fps;
            EvaluatePreviewAudioCues();
            Repaint();
        }

        private void AddChargeSimulationInput(AttackMotionDefinition attack)
        {
            chargeSimLastInputTime = EditorApplication.timeSinceStartup;
            int required = attack.RequiredInputsToStrike;

            switch (chargeSimPhase)
            {
                case ChargeSimPhase.Charging:
                    chargeSimInputs = Mathf.Min(chargeSimInputs + 1f, required);
                    break;
                case ChargeSimPhase.Recovery:
                    // Recovery는 끊지 않고, 이 입력은 다음 공격 충전으로 이월한다.
                    if (attack.CarryOverflowInputs) chargeSimCarried = Mathf.Min(chargeSimCarried + 1f, required);
                    break;
                default:
                    BeginChargeSimCycle(attack, 1f);
                    break;
            }
        }

        private void BeginChargeSimCycle(AttackMotionDefinition attack, float startInputs)
        {
            chargeSimPhase = ChargeSimPhase.Charging;
            chargeSimInputs = Mathf.Clamp(startInputs, 0f, attack.RequiredInputsToStrike);
            chargeSimCarried = 0f;
            chargeSimRecoveryTimer = 0f;
            // 새 공격 인스턴스 - Cast/Hit Cue를 이번 사이클에 다시 한 번 울릴 수 있게 되돌린다.
            previewCastCueFired = false;
            previewHitCueFired = false;
        }

        /// <summary>충전이 완전히 풀렸거나 Recovery가 끝나 Idle로 돌아간 상태. 자동 입력이 켜져 있으면
        /// 다음 입력이 곧 새 사이클을 시작하므로 계속 돌리고, 수동/Stop 상태면 여기서 멈춘다.</summary>
        private void EndChargeSimCycle()
        {
            chargeSimPhase = ChargeSimPhase.Idle;
            chargeSimInputs = 0f;
            chargeSimCarried = 0f;
            chargeSimRecoveryTimer = 0f;
            chargeSimRunning = chargeSimAutoInterval > 0d;
        }

        private void StartChargeSimulationInput(double interval)
        {
            chargeSimAutoInterval = interval;
            chargeSimResumeInterval = interval;
            chargeSimRunning = true;
            previewPlaying = false;
            chargeSimLastStepTime = EditorApplication.timeSinceStartup;
            chargeSimNextAutoInput = chargeSimLastStepTime; // 즉시 첫 입력
        }

        /// <summary>자동 입력만 멈추고 시뮬레이션은 계속 돌린다 - 유예 시간과 감쇠가 실제로 어떻게
        /// 동작하는지(자세 유지 -> 서서히 감소 -> Idle 복귀) 확인하는 것이 이 버튼의 목적이다.</summary>
        private void StopChargeSimulationInput()
        {
            chargeSimAutoInterval = 0d;
            if (chargeSimPhase == ChargeSimPhase.Idle) return;
            chargeSimRunning = true;
            chargeSimLastStepTime = EditorApplication.timeSinceStartup;
        }

        private void ResetChargeSimulation()
        {
            chargeSimRunning = false;
            chargeSimPhase = ChargeSimPhase.Idle;
            chargeSimInputs = 0f;
            chargeSimCarried = 0f;
            chargeSimStrikes = 0;
            chargeSimRecoveryTimer = 0f;
            chargeSimAutoInterval = 0d;
        }

        private void DrawChargeSimulationControls(AttackMotionDefinition attack)
        {
            int required = attack.RequiredInputsToStrike;
            string phase = chargeSimPhase == ChargeSimPhase.Charging ? "Charging"
                : chargeSimPhase == ChargeSimPhase.Recovery ? "Recovery"
                : "Idle";
            string mode = chargeSimAutoInterval <= 0d ? "Manual"
                : chargeSimAutoInterval <= FastInputInterval ? "Fast"
                : "Slow";

            EditorGUILayout.LabelField(
                $"Input: {Mathf.CeilToInt(chargeSimInputs)} / {required}   Phase: {phase}   " +
                $"Charge: {Mathf.RoundToInt(Mathf.Clamp01(chargeSimInputs / required) * 100f)}%   " +
                $"Carry: {Mathf.FloorToInt(chargeSimCarried)}   Strikes: {chargeSimStrikes}   [{mode}]",
                EditorStyles.centeredGreyMiniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Add Input", "입력 1회를 넣는다."), GUILayout.Width(74f)))
                {
                    previewPlaying = false;
                    chargeSimRunning = true;
                    chargeSimLastStepTime = EditorApplication.timeSinceStartup;
                    AddChargeSimulationInput(attack);
                }
                if (GUILayout.Button(new GUIContent("Fast Input", $"{FastInputInterval:0.00}초 간격 연타"), GUILayout.Width(74f)))
                    StartChargeSimulationInput(FastInputInterval);
                if (GUILayout.Button(new GUIContent("Slow Input", $"{SlowInputInterval:0.00}초 간격 입력"), GUILayout.Width(76f)))
                    StartChargeSimulationInput(SlowInputInterval);
                if (GUILayout.Button(new GUIContent("Stop Input", "입력만 멈춘다 - 유예/감쇠는 계속 진행된다."), GUILayout.Width(76f)))
                    StopChargeSimulationInput();
                if (GUILayout.Button(new GUIContent("Resume Input", "마지막 자동 입력 속도로 다시 시작한다."), GUILayout.Width(92f)))
                    StartChargeSimulationInput(chargeSimResumeInterval);
                if (GUILayout.Button(new GUIContent("Reset", "충전/이월/타격 횟수를 모두 초기화한다."), GUILayout.Width(56f)))
                    ResetChargeSimulation();
                GUILayout.FlexibleSpace();
            }
        }

        private static void DrawStageBackground(Rect stage)
        {
            EditorGUI.DrawRect(stage, new Color(0.12f, 0.12f, 0.12f));
            GUI.BeginGroup(stage);
            float groundY = stage.height * GroundRatio;
            EditorGUI.DrawRect(new Rect(0f, groundY, stage.width, 1f), new Color(0.2f, 1f, 0.2f, 0.55f));
            Color border = new Color(1f, 1f, 1f, 0.25f);
            EditorGUI.DrawRect(new Rect(0f, 0f, stage.width, 1f), border);
            EditorGUI.DrawRect(new Rect(0f, stage.height - 1f, stage.width, 1f), border);
            EditorGUI.DrawRect(new Rect(0f, 0f, 1f, stage.height), border);
            EditorGUI.DrawRect(new Rect(stage.width - 1f, 0f, 1f, stage.height), border);
            GUI.EndGroup();
        }

        private static Vector2 WorldToScreen(Vector2 value, float pixelsPerUnit)
        {
            return new Vector2(value.x * pixelsPerUnit, -value.y * pixelsPerUnit);
        }

        private static float EvaluateMovement(float time, float distance, float outDuration, float backDuration)
        {
            if (time <= 0f) return 0f;
            if (time < outDuration) return distance * Mathf.Clamp01(time / outDuration);
            if (time < outDuration + backDuration) return Mathf.Lerp(distance, 0f, (time - outDuration) / backDuration);
            return 0f;
        }

        /// <summary>TargetCombatController.UpdateShake()와 동일한 수식 - 항상 time(=현재 프레임 시각)만
        /// 놓고 새로 계산하는 순수 함수라 재생 여부/이전 프레임과 무관하게 결정론적이다(누적 오차 없음,
        /// 타임라인을 아무 지점으로 드래그해도 그 지점 기준으로 바로 정확한 값이 나온다).</summary>
        private static float EvaluateHitShake(MonsterMotionProfile.HitReactionSettings reaction, float time, float hitStartTime, bool active)
        {
            if (!active || reaction == null) return 0f;
            if (reaction.ShakeStrength <= 0f || reaction.ShakeDecayDuration <= 0f) return 0f;

            float elapsed = time - hitStartTime;
            if (elapsed < 0f || elapsed >= reaction.ShakeDecayDuration) return 0f;

            float remaining = 1f - elapsed / reaction.ShakeDecayDuration;
            return Mathf.Sin(elapsed * reaction.ShakeFrequency * Mathf.PI * 2f) * reaction.ShakeStrength * remaining;
        }

        private static void DrawMarker(Vector2 anchor, Color color)
        {
            const float size = 5f;
            EditorGUI.DrawRect(new Rect(anchor.x - size, anchor.y - 1f, size * 2f, 2f), color);
            EditorGUI.DrawRect(new Rect(anchor.x - 1f, anchor.y - size, 2f, size * 2f), color);
        }

        // 몬스터의 기본 Jitter 조회 캐시 - 씬 탐색을 OnGUI마다 반복하지 않기 위한 것이다.
        private HitEffectSpawner cachedMonsterHitEffectSpawner;
        private double monsterHitEffectSpawnerLookupTime = -999d;

        /// <summary>Hit Effect의 랜덤 출력 범위를 몬스터 기본값에 맡겼을 때 그 값을 보여주기 위한 조회.
        /// 이 값은 프로필 에셋이 아니라 <b>씬에 놓인 몬스터의 HitEffectSpawner</b>에 있으므로 열려 있는
        /// 씬에서 찾는다 - 씬에 몬스터가 없으면 null이고, 그때는 범위를 표시하지 않는다(0으로 표시하면
        /// "랜덤 없음"과 구분되지 않아 오히려 잘못된 정보가 된다).
        ///
        /// FindObjectsOfType은 OnGUI마다 돌리기엔 비싸므로 찾은 결과를 캐시하고 1초에 한 번만 갱신한다 -
        /// 인스펙터에서 몬스터 값을 바꿔도 곧 반영된다.</summary>
        private Vector2? GetPreviewMonsterJitter()
        {
            const double refreshInterval = 1d;
            double now = EditorApplication.timeSinceStartup;
            if (cachedMonsterHitEffectSpawner == null || now - monsterHitEffectSpawnerLookupTime > refreshInterval)
            {
                monsterHitEffectSpawnerLookupTime = now;
                cachedMonsterHitEffectSpawner = ResolveMonsterHitEffectSpawner();
            }

            return cachedMonsterHitEffectSpawner != null ? cachedMonsterHitEffectSpawner.DefaultJitterRange : (Vector2?)null;
        }

        private static HitEffectSpawner ResolveMonsterHitEffectSpawner()
        {
            // 비활성 포함 - 대기 중인 몬스터(Monster_Standby)만 있을 수도 있다.
            var controllers = UnityEngine.Object.FindObjectsOfType<TargetCombatController>(true);
            for (int i = 0; i < controllers.Length; i++)
            {
                var spawner = controllers[i].GetComponent<HitEffectSpawner>();
                if (spawner != null) return spawner;
            }
            return null;
        }

        /// <summary>이번 공격이 실제로 쓰게 될 Hit Effect 랜덤 범위. Override가 켜져 있으면 공격이 정한
        /// 값, 아니면 몬스터 기본값이다(런타임 HitEffectSpawner.ResolveJitterRange와 같은 판단).</summary>
        private Vector2? ResolveEffectiveHitEffectJitter(AttackMotionDefinition attack)
        {
            if (attack == null) return null;
            if (attack.OverrideHitEffectJitter) return attack.HitEffectJitter;
            return GetPreviewMonsterJitter();
        }

        /// <summary>Hit Effect가 흩어질 수 있는 범위를 점선 사각형으로 그린다. 실제 랜덤값을 그리지
        /// 않는 이유는 프리뷰가 결정론적이어야 하기 때문이다 - 같은 스크럽 위치에서 매번 다른 자리에
        /// 이펙트가 찍히면 Offset을 눈으로 맞출 수 없다. 대신 "이 안에서 튄다"를 범위로 보여준다.
        /// 범위가 0이면(랜덤 없음) 그릴 사각형이 없으므로 아무것도 그리지 않는다.</summary>
        private static void DrawJitterRangeGuide(Vector2 anchor, Vector2 jitterRange, float worldToScreen)
        {
            if (jitterRange.x <= 0f && jitterRange.y <= 0f) return;

            float halfWidth = jitterRange.x * worldToScreen;
            float halfHeight = jitterRange.y * worldToScreen;
            Vector3 topLeft = new Vector3(anchor.x - halfWidth, anchor.y - halfHeight);
            Vector3 topRight = new Vector3(anchor.x + halfWidth, anchor.y - halfHeight);
            Vector3 bottomRight = new Vector3(anchor.x + halfWidth, anchor.y + halfHeight);
            Vector3 bottomLeft = new Vector3(anchor.x - halfWidth, anchor.y + halfHeight);

            Color oldColor = Handles.color;
            Handles.color = new Color(1f, 0.62f, 0.2f, 0.85f);
            const float dashSize = 4f;
            Handles.DrawDottedLine(topLeft, topRight, dashSize);
            Handles.DrawDottedLine(topRight, bottomRight, dashSize);
            Handles.DrawDottedLine(bottomRight, bottomLeft, dashSize);
            Handles.DrawDottedLine(bottomLeft, topLeft, dashSize);
            Handles.color = oldColor;
        }

        /// <summary>IHitEffectPlayback을 구현하지 않아 자기 길이를 알려주지 못하는 이펙트 프리팹을
        /// 프리뷰에서 얼마 동안 보여줄지. HitEffectSpawner가 그런 prefab에 쓰는 폴백 회수 시간(기본
        /// defaultDuration)과 같은 의미다 - 런타임과 프리뷰가 서로 다른 시간을 쓰지 않도록 맞춰둔다.</summary>
        private const float EffectPreviewFallbackDuration = 0.15f;

        /// <summary>이펙트 프리팹을 "재생 시작 후 elapsed초" 상태로 그린다. 재생이 끝났거나(elapsed가
        /// 이펙트 길이 이상) 아직 시작 전이면(elapsed &lt; 0) 아무것도 그리지 않는다. 배율/앵커 규칙은
        /// 기존 Cast/Hit Effect 표시와 동일하게 유지한다(previewZoom * Effect Scale).</summary>
        private static void DrawEffectPreview(GameObject prefab, float elapsed, float scale, Vector2 anchor, float previewZoom)
        {
            if (prefab == null || elapsed < 0f) return;

            Sprite sprite = GetEffectPreviewSprite(prefab, elapsed);
            if (sprite == null) return;

            DrawSprite(sprite, previewZoom * Mathf.Max(0.01f, scale), anchor, Color.white);
        }

        /// <summary>런타임이 그 시점에 보여줄 Sprite를 그대로 되묻는다 - 프레임 선택 규칙을 에디터가
        /// 따로 구현하지 않으므로(이펙트 컴포넌트의 GetFrameAt이 단일 소스다) 프리뷰와 런타임이
        /// 어긋날 수 없다. 재생 컴포넌트가 아예 없는 prefab은 길이를 알 수 없으므로 폴백 시간 동안
        /// 프리팹에 꽂힌 그림 한 장을 보여준다.</summary>
        private static Sprite GetEffectPreviewSprite(GameObject prefab, float elapsed)
        {
            var playback = prefab.GetComponentInChildren<IHitEffectPlayback>(true);
            if (playback != null) return playback.GetFrameAt(elapsed);

            if (elapsed >= EffectPreviewFallbackDuration) return null;
            return prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite;
        }

        /// <summary>이펙트 프리팹이 스스로 보고하는 재생 길이(초). 타임라인이 이펙트가 끝나기 전에
        /// 잘리지 않도록 프리뷰 전체 길이를 계산할 때 쓴다.</summary>
        private static float GetEffectPreviewDuration(GameObject prefab)
        {
            if (prefab == null) return 0f;

            var playback = prefab.GetComponentInChildren<IHitEffectPlayback>(true);
            return playback != null ? Mathf.Max(0f, playback.Duration) : EffectPreviewFallbackDuration;
        }

        /// <summary>발사체 경로/시작점을 항상 표시하고, Cast~Hit 구간에는 프리팹의 현재 Sprite를 런타임과
        /// 같은 정규화 진행도로 보간해 그린다. ProjectileSpriteAnimation이 있으면 frames/Fade 설정까지
        /// 읽고, 없으면 첫 SpriteRenderer의 현재 Sprite를 단일 이미지로 사용한다.</summary>
        private static void DrawProjectilePreview(AttackMotionDefinition attack, float time, float castTime, float hitTime,
            Vector2 launchPoint, Vector2 targetPoint, float worldToScreen)
        {
            Color oldHandlesColor = Handles.color;
            Handles.color = new Color(0.25f, 0.95f, 1f, 0.42f);
            Handles.DrawAAPolyLine(2f, launchPoint, targetPoint);
            Handles.color = oldHandlesColor;

            Color launchColor = new Color(0.15f, 0.95f, 1f);
            DrawMarker(launchPoint, launchColor);
            GUI.Label(new Rect(launchPoint.x + 7f, launchPoint.y - 17f, 92f, 18f), "Launch Point", EditorStyles.miniLabel);

            if (hitTime <= castTime || time < castTime || time >= hitTime) return;

            float progress = Mathf.InverseLerp(castTime, hitTime, time);
            GameObject prefab = attack.ProjectilePrefab;
            if (prefab == null) return;

            SpriteRenderer renderer;
            Color tint;
            Sprite sprite = GetProjectilePreviewSprite(prefab, progress, hitTime - castTime, out renderer, out tint);
            if (sprite == null || renderer == null) return;

            Vector2 projectilePoint = Vector2.Lerp(launchPoint, targetPoint, progress);
            Vector2 direction = targetPoint - launchPoint;
            float angle = direction.sqrMagnitude > Mathf.Epsilon
                ? Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg
                : 0f;

            float prefabScale = GetRelativePreviewScale(renderer.transform, prefab.transform);
            float spritePpu = Mathf.Max(0.01f, sprite.pixelsPerUnit);
            float spriteZoom = worldToScreen / spritePpu
                               * Mathf.Max(0.01f, attack.ProjectileScale)
                               * prefabScale;
            DrawRotatedSprite(sprite, spriteZoom, projectilePoint, tint, angle);
        }

        private static Sprite GetProjectilePreviewSprite(GameObject prefab, float progress, float flightDuration,
            out SpriteRenderer renderer, out Color tint)
        {
            ProjectileSpriteAnimation animation = prefab.GetComponentInChildren<ProjectileSpriteAnimation>(true);
            SerializedObject serialized = animation != null ? new SerializedObject(animation) : null;
            serialized?.Update();
            renderer = serialized?.FindProperty("spriteRenderer")?.objectReferenceValue as SpriteRenderer;
            if (renderer == null)
            {
                renderer = animation != null
                    ? animation.GetComponent<SpriteRenderer>()
                    : prefab.GetComponentInChildren<SpriteRenderer>(true);
            }
            tint = renderer != null ? renderer.color : Color.white;
            if (renderer == null) return null;

            Sprite sprite = renderer.sprite;
            if (animation == null) return sprite;

            SerializedProperty frames = serialized.FindProperty("frames");
            if (frames != null && frames.arraySize > 0)
            {
                int index = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(progress) * frames.arraySize), 0, frames.arraySize - 1);
                // 런타임은 null 프레임에서 직전 Sprite를 유지한다. 첫 프레임부터 현재 인덱스까지 순서대로
                // 훑어 같은 결과를 만든다(프리뷰는 에디터 전용이고 배열도 작으므로 할당 없는 짧은 순회면 충분).
                for (int i = 0; i <= index; i++)
                {
                    Sprite candidate = frames.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                    if (candidate != null) sprite = candidate;
                }
            }

            float fadeIn = serialized.FindProperty("fadeInRatio").floatValue;
            float fadeOut = serialized.FindProperty("fadeOutRatio").floatValue;
            float minFadeDuration = serialized.FindProperty("minFadeFlightDuration").floatValue;
            if ((fadeIn > 0f || fadeOut > 0f) && flightDuration >= minFadeDuration)
            {
                float alphaMultiplier = 1f;
                if (fadeIn > 0f && progress < fadeIn) alphaMultiplier = progress / fadeIn;
                else if (fadeOut > 0f && progress > 1f - fadeOut) alphaMultiplier = (1f - progress) / fadeOut;
                tint.a *= Mathf.Clamp01(alphaMultiplier);
            }
            return sprite;
        }

        private static float GetRelativePreviewScale(Transform child, Transform root)
        {
            float scale = 1f;
            Transform current = child;
            while (current != null)
            {
                scale *= Mathf.Abs(current.localScale.x);
                if (current == root) break;
                current = current.parent;
            }
            return Mathf.Max(0.0001f, scale);
        }

        private static void DrawRotatedSprite(Sprite sprite, float zoom, Vector2 anchor, Color tint, float angleDegrees)
        {
            Matrix4x4 oldMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angleDegrees, anchor);
            DrawSprite(sprite, zoom, anchor, tint);
            GUI.matrix = oldMatrix;
        }

        /// <summary>고정 문자열 "1"을 Monster Profile의 색상/크기·현재 페이드 알파로 표시한다 - 호출부가
        /// time만으로 계산한 anchor/color를 그대로 그리기만 하므로, 재생 상태나 이전 프레임과 무관하게
        /// 같은 시각(같은 스크럽 위치)에서는 항상 같은 위치·색·투명도로 결정론적으로 나타난다.</summary>
        private void DrawDamageNumberPreview(Vector2 anchor, Color color, float fontSize)
        {
            const float width = 40f;
            const float height = 24f;
            GUIStyle style = DamageNumberPreviewStyle;
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(fontSize));
            style.normal.textColor = color;
            EditorGUI.LabelField(new Rect(anchor.x - width * 0.5f, anchor.y - height * 0.5f, width, height), "1", style);
        }

        /// <summary>개발용 진단 오버레이 - "조건이 안 맞아서 안 그려짐"과 "그려지긴 하는데 안 보임"을
        /// 구분하기 위한 것이다. active가 false인데 profile은 있다면 elapsed/duration 관계를 보고
        /// 타이밍 문제인지 알 수 있고, active가 true인데 화면에 숫자가 안 보인다면 anchor 좌표가
        /// Stage Rect(0~stage.width, 0~stage.height) 밖인지, 혹은 렌더링/스타일 쪽 문제인지로 좁혀진다.</summary>
        private void DrawDamageNumberDebugOverlay(Rect stage, bool hasHitContext, bool profileAvailable, bool active,
            float elapsed, float duration, Vector2 anchor)
        {
            string status = !hasHitContext ? "inactive (no Hit context)"
                : !profileAvailable ? "inactive (no Monster Profile)"
                : active ? "active" : "inactive (outside elapsed window)";

            string text = $"Damage Preview: {status}\nelapsed {elapsed:0.###} / duration {duration:0.###}\nanchor ({anchor.x:0.#}, {anchor.y:0.#}) / stage {stage.width:0}x{stage.height:0}";

            // 좌하단은 DrawInlineTargetSelector의 "대상: 이름" 버튼이 이미 쓰고 있으므로 우하단에 둔다.
            Rect box = new Rect(stage.width - 266f, stage.height - 54f, 260f, 48f);
            EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.65f));
            GUI.Label(new Rect(box.x + 4f, box.y + 2f, box.width - 8f, box.height - 4f), text, EditorStyles.whiteMiniLabel);

            if (active)
            {
                EditorGUI.DrawRect(new Rect(anchor.x - 3f, anchor.y - 3f, 6f, 6f), Color.yellow);
            }
        }

        private static void DrawReceivePoint(Vector2 point, bool isHit)
        {
            Color color = isHit ? new Color(1f, 0.2f, 0.1f) : new Color(1f, 0.45f, 0.2f);
            const float radius = 8f;
            EditorGUI.DrawRect(new Rect(point.x - radius, point.y - 1f, radius * 2f, 2f), color);
            EditorGUI.DrawRect(new Rect(point.x - 1f, point.y - radius, 2f, radius * 2f), color);
            GUI.Label(new Rect(point.x + 6f, point.y - 17f, 90f, 18f), "Receive Point", EditorStyles.miniLabel);
        }

        private static void DrawSprite(Sprite sprite, float zoom, Vector2 anchor, Color tint, bool flipX = false)
        {
            Texture2D texture = sprite.texture;
            if (texture == null) return;
            FilterMode oldFilter = texture.filterMode;
            Color oldColor = GUI.color;
            texture.filterMode = FilterMode.Point;
            GUI.color = tint;
            Rect rect = sprite.rect;
            Vector2 pivot = sprite.pivot;
            float width = rect.width * zoom;
            float height = rect.height * zoom;
            float px = rect.width > 0f ? pivot.x / rect.width : 0.5f;
            float py = rect.height > 0f ? pivot.y / rect.height : 0f;
            // 좌우 반전 시에도 Pivot이 anchor에 그대로 고정되도록, 반전된 이미지 안에서의 pivot 위치(1-px)를
            // 기준으로 왼쪽 모서리를 다시 계산한다 - SpriteRenderer.flipX와 같은 방식(프레임 자체는 그대로).
            float drawX = flipX ? anchor.x - (1f - px) * width : anchor.x - px * width;
            Rect drawRect = new Rect(drawX, anchor.y - height * (1f - py), width, height);
            Rect uv = rect;
            uv.x /= texture.width;
            uv.width /= texture.width;
            uv.y /= texture.height;
            uv.height /= texture.height;
            if (flipX)
            {
                uv.x += uv.width;
                uv.width = -uv.width;
            }
            GUI.DrawTextureWithTexCoords(drawRect, texture, uv);
            GUI.color = oldColor;
            texture.filterMode = oldFilter;
        }

        private static float ComputeFitZoom(SerializedProperty frames)
        {
            if (frames == null || frames.arraySize == 0) return DefaultZoom;
            Vector2 anchor = new Vector2(StageWidth * 0.38f, StageHeight * GroundRatio);
            float min = float.MaxValue;
            bool found = false;
            for (int i = 0; i < frames.arraySize; i++)
            {
                Sprite sprite = frames.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                if (sprite == null) continue;
                min = Mathf.Min(min, MaxZoomForSprite(sprite, anchor));
                found = true;
            }
            return Mathf.Clamp(found ? min : DefaultZoom, ZoomMin, ZoomMax);
        }

        private static float ComputeFitZoom(List<Sprite> frames)
        {
            if (frames == null || frames.Count == 0) return DefaultZoom;
            Vector2 anchor = new Vector2(StageWidth * 0.38f, StageHeight * GroundRatio);
            float min = float.MaxValue;
            bool found = false;
            for (int i = 0; i < frames.Count; i++)
            {
                Sprite sprite = frames[i];
                if (sprite == null) continue;
                min = Mathf.Min(min, MaxZoomForSprite(sprite, anchor));
                found = true;
            }
            return Mathf.Clamp(found ? min : DefaultZoom, ZoomMin, ZoomMax);
        }

        private static float MaxZoomForSprite(Sprite sprite, Vector2 anchor)
        {
            Rect rect = sprite.rect;
            if (rect.width <= 0f || rect.height <= 0f) return DefaultZoom;
            float px = sprite.pivot.x / rect.width;
            float py = sprite.pivot.y / rect.height;
            float left = Mathf.Max(1f, anchor.x - FitMargin);
            float right = Mathf.Max(1f, StageWidth - anchor.x - FitMargin);
            float up = Mathf.Max(1f, anchor.y - FitMargin);
            float down = Mathf.Max(1f, StageHeight - anchor.y - FitMargin);
            float zoom = float.MaxValue;
            if (px * rect.width > 0f) zoom = Mathf.Min(zoom, left / (px * rect.width));
            if ((1f - px) * rect.width > 0f) zoom = Mathf.Min(zoom, right / ((1f - px) * rect.width));
            if ((1f - py) * rect.height > 0f) zoom = Mathf.Min(zoom, up / ((1f - py) * rect.height));
            if (py * rect.height > 0f) zoom = Mathf.Min(zoom, down / (py * rect.height));
            return float.IsInfinity(zoom) ? DefaultZoom : zoom;
        }

        private void CreateProfileFromArt(ResourceEntry entry)
        {
            EnsureAssetFolder(entry.DataFolderPath);
            if (entry.Kind == ActorKind.Character) CreateCharacterProfile(entry);
            else CreateMonsterProfile(entry);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ScanArtFolders();
            int index = resources.FindIndex(item => item.FolderPath == entry.FolderPath);
            if (index >= 0)
            {
                selectedResourceIndex = index;
                SelectResource(index);
            }
        }

        private static void CreateCharacterProfile(ResourceEntry entry)
        {
            string profilePath = AssetDatabase.GenerateUniqueAssetPath($"{entry.DataFolderPath}/{entry.Name}_MotionProfile.asset");
            var profile = CreateInstance<CharacterMotionProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("displayName").stringValue = entry.Name;
            serialized.FindProperty("resourceFolderPath").stringValue = entry.FolderPath;

            string idleFolder = FindMotionFolder(entry.FolderPath, "idle");
            PopulateClip(serialized.FindProperty("baseIdle"), "Base Idle", LoadSprites(idleFolder), 6f);
            PopulateIdleEvents(serialized.FindProperty("idleEvents"), entry.FolderPath);

            var tierMotions = new Dictionary<int, List<AttackMotionDefinition>>
            {
                { 1, new List<AttackMotionDefinition>() },
                { 2, new List<AttackMotionDefinition>() },
                { 3, new List<AttackMotionDefinition>() },
            };

            var existingPools = new Dictionary<int, ComboTierAttackPool>();
            for (int tier = 1; tier <= 3; tier++)
            {
                ComboTierAttackPool existing = FindLegacyPool(entry.Name, tier);
                if (existing == null) continue;
                existingPools[tier] = existing;
                serialized.FindProperty($"tier{tier}Pool").objectReferenceValue = existing;
            }

            foreach (string folder in AssetDatabase.GetSubFolders(entry.FolderPath))
            {
                string folderName = Path.GetFileName(folder);
                if (!folderName.StartsWith("attack", StringComparison.OrdinalIgnoreCase)) continue;
                int tier = folderName.IndexOf("tier3", StringComparison.OrdinalIgnoreCase) >= 0 ? 3
                    : folderName.IndexOf("tier2", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1;
                if (existingPools.ContainsKey(tier)) continue;
                string motionPath = AssetDatabase.GenerateUniqueAssetPath($"{entry.DataFolderPath}/{entry.Name}_{SanitizeFileName(folderName)}.asset");
                var motion = CreateInstance<AttackMotionDefinition>();
                AssetDatabase.CreateAsset(motion, motionPath);
                var motionSerialized = new SerializedObject(motion);
                SetObjectArray(motionSerialized.FindProperty("frames"), LoadSprites(folder));
                motionSerialized.ApplyModifiedProperties();
                tierMotions[tier].Add(motion);
            }

            for (int tier = 1; tier <= 3; tier++)
            {
                if (existingPools.ContainsKey(tier)) continue;
                if (tierMotions[tier].Count == 0) continue;
                ComboTierAttackPool pool = CreatePool(entry, tier, tierMotions[tier]);
                serialized.FindProperty($"tier{tier}Pool").objectReferenceValue = pool;
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
        }

        private static ComboTierAttackPool FindLegacyPool(string actorName, int tier)
        {
            string folder = $"Assets/Data/{actorName}";
            if (!AssetDatabase.IsValidFolder(folder)) return null;
            string tierTag = "Tier" + tier;
            foreach (string guid in AssetDatabase.FindAssets("t:ComboTierAttackPool", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path).IndexOf(tierTag, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return AssetDatabase.LoadAssetAtPath<ComboTierAttackPool>(path);
                }
            }
            return null;
        }

        private static void CreateMonsterProfile(ResourceEntry entry)
        {
            string profilePath = AssetDatabase.GenerateUniqueAssetPath($"{entry.DataFolderPath}/{entry.Name}_MotionProfile.asset");
            var profile = CreateInstance<MonsterMotionProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("displayName").stringValue = entry.Name;
            serialized.FindProperty("resourceFolderPath").stringValue = entry.FolderPath;
            PopulateClip(serialized.FindProperty("baseIdle"), "Base Idle", LoadSprites(FindMotionFolder(entry.FolderPath, "idle")), 6f);
            PopulateIdleEvents(serialized.FindProperty("idleEvents"), entry.FolderPath);
            PopulateClip(serialized.FindProperty("hit"), "Hit", LoadSprites(FindMotionFolder(entry.FolderPath, "hit")), 6f);
            PopulateClip(serialized.FindProperty("defeat"), "Defeat", LoadSprites(FindMotionFolder(entry.FolderPath, "defeat")), 6f);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
        }

        private static ComboTierAttackPool CreatePool(ResourceEntry entry, int tier, List<AttackMotionDefinition> motions)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath($"{entry.DataFolderPath}/{entry.Name}_Tier{tier}AttackPool.asset");
            var pool = CreateInstance<ComboTierAttackPool>();
            AssetDatabase.CreateAsset(pool, path);
            var serialized = new SerializedObject(pool);
            SerializedProperty array = serialized.FindProperty("motions");
            array.arraySize = motions.Count;
            for (int i = 0; i < motions.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = motions[i];
            serialized.ApplyModifiedProperties();
            return pool;
        }

        private static void PopulateIdleEvents(SerializedProperty events, string actorFolder)
        {
            events.ClearArray();
            foreach (string folder in AssetDatabase.GetSubFolders(actorFolder))
            {
                string name = Path.GetFileName(folder);
                if (!name.StartsWith("idle_", StringComparison.OrdinalIgnoreCase)) continue;
                int index = events.arraySize;
                events.InsertArrayElementAtIndex(index);
                PopulateClip(events.GetArrayElementAtIndex(index), ToDisplayName(name), LoadSprites(folder), 6f);
            }
        }

        private static void PopulateClip(SerializedProperty clip, string name, List<Sprite> sprites, float fps)
        {
            clip.FindPropertyRelative("displayName").stringValue = name;
            clip.FindPropertyRelative("editorDescription").stringValue = string.Empty;
            clip.FindPropertyRelative("animationFps").floatValue = fps;
            SetObjectArray(clip.FindPropertyRelative("frames"), sprites);
        }

        private static void SetObjectArray(SerializedProperty array, List<Sprite> sprites)
        {
            array.ClearArray();
            for (int i = 0; i < sprites.Count; i++) AppendObjectReference(array, sprites[i]);
        }

        private static List<Sprite> LoadSprites(string folder)
        {
            var result = new List<Sprite>();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return result;
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path)?.Replace('\\', '/') == folder) paths.Add(path);
            }
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Sprite sprite) result.Add(sprite);
                }
            }
            result.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static string FindMotionFolder(string actorFolder, string exactName)
        {
            if (string.IsNullOrEmpty(actorFolder) || !AssetDatabase.IsValidFolder(actorFolder)) return null;
            foreach (string folder in AssetDatabase.GetSubFolders(actorFolder))
            {
                if (string.Equals(Path.GetFileName(folder), exactName, StringComparison.OrdinalIgnoreCase)) return folder;
            }
            return null;
        }

        private static void EnsureAssetFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private void CreateAttackAsset(int tier)
        {
            ResourceEntry entry = SelectedResource;
            if (entry?.CharacterProfile == null) return;
            EnsureAssetFolder(entry.DataFolderPath);
            string path = EditorUtility.SaveFilePanelInProject("Create Attack Motion", entry.Name + "_Attack", "asset", "공격 모션 이름을 지정하세요.", entry.DataFolderPath);
            if (string.IsNullOrEmpty(path)) return;

            var motion = CreateInstance<AttackMotionDefinition>();
            AssetDatabase.CreateAsset(motion, path);
            ComboTierAttackPool pool = GetPool(entry.CharacterProfile, tier);
            if (pool == null)
            {
                pool = CreatePool(entry, tier, new List<AttackMotionDefinition>());
                activeProfileObject.Update();
                activeProfileObject.FindProperty($"tier{tier}Pool").objectReferenceValue = pool;
                activeProfileObject.ApplyModifiedProperties();
            }
            var serializedPool = new SerializedObject(pool);
            SerializedProperty motions = serializedPool.FindProperty("motions");
            int index = motions.arraySize;
            motions.arraySize++;
            motions.GetArrayElementAtIndex(index).objectReferenceValue = motion;
            serializedPool.ApplyModifiedProperties();
            EditorUtility.SetDirty(pool);
            activePool = pool;
            poolObject = serializedPool;
            SelectAttack(motion);
        }

        private void SaveActiveProfile()
        {
            activeProfileObject?.ApplyModifiedProperties();
            attackObject?.ApplyModifiedProperties();
            poolObject?.ApplyModifiedProperties();
            if (SelectedResource?.ProfileObject != null) EditorUtility.SetDirty(SelectedResource.ProfileObject);
            if (selectedAttack != null) EditorUtility.SetDirty(selectedAttack);
            if (activePool != null) EditorUtility.SetDirty(activePool);
            AssetDatabase.SaveAssets();
            CaptureSavedSnapshots(SelectedResource);
            Repaint();
        }

        private void SyncActiveFramesFromArt()
        {
            ResourceEntry entry = SelectedResource;
            if (entry == null || !entry.HasProfile) return;
            if (!EditorUtility.DisplayDialog(
                    "Sync Frames from Art",
                    "아트 하위 폴더의 현재 Sprite 목록으로 프레임 배열만 갱신합니다. FPS, Hit Frame, Movement, Effect 설정은 유지됩니다.\n\n" +
                    "Overlay 프레임은 자동으로 자르거나 늘리지 않고 그대로 보존합니다. Sync 결과 본체 프레임 수가 달라지면 " +
                    "어긋난 공격 모션을 따로 알려드립니다.",
                    "Sync",
                    "Cancel")) return;

            activeProfileObject.Update();
            var overlayMismatches = new List<string>();
            SetObjectArray(activeProfileObject.FindProperty("baseIdle").FindPropertyRelative("frames"),
                LoadSprites(FindMotionFolder(entry.FolderPath, "idle")));
            SyncIdleEventFrames(activeProfileObject.FindProperty("idleEvents"), entry.FolderPath);

            if (entry.Kind == ActorKind.Character)
            {
                SyncAttackFrames(entry, overlayMismatches);
            }
            else
            {
                SetObjectArray(activeProfileObject.FindProperty("hit").FindPropertyRelative("frames"),
                    LoadSprites(FindMotionFolder(entry.FolderPath, "hit")));
                SetObjectArray(activeProfileObject.FindProperty("defeat").FindPropertyRelative("frames"),
                    LoadSprites(FindMotionFolder(entry.FolderPath, "defeat")));
            }

            activeProfileObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(entry.ProfileObject);
            RebuildFrameList();
            RestartPreview();

            // Overlay는 손대지 않으므로(작업물을 조용히 잘라내지 않는다) Sync로 본체 프레임 수가 바뀌면
            // 쌍이 어긋난 채로 남는다 - 어떤 모션이 어떻게 어긋났는지 여기서 분명히 알린다.
            if (overlayMismatches.Count == 0) return;
            EditorUtility.DisplayDialog(
                "Overlay 길이 확인 필요",
                "Sync로 본체 프레임 수가 바뀌었지만 Overlay 배열은 보존했습니다. 아래 공격 모션은 지금 쌍이 어긋나 있습니다.\n\n" +
                string.Join("\n", overlayMismatches) +
                "\n\n각 공격의 Sprite Frames 아래 Match Overlay Length 버튼으로 맞추거나, Overlay 슬롯을 직접 다시 배치하세요.",
                "확인");
        }

        private static void SyncIdleEventFrames(SerializedProperty events, string actorFolder)
        {
            foreach (string folder in AssetDatabase.GetSubFolders(actorFolder))
            {
                string folderName = Path.GetFileName(folder);
                if (!folderName.StartsWith("idle_", StringComparison.OrdinalIgnoreCase)) continue;
                string displayName = ToDisplayName(folderName);
                SerializedProperty matching = null;
                for (int i = 0; i < events.arraySize; i++)
                {
                    SerializedProperty candidate = events.GetArrayElementAtIndex(i);
                    if (string.Equals(candidate.FindPropertyRelative("displayName").stringValue, displayName, StringComparison.OrdinalIgnoreCase))
                    {
                        matching = candidate;
                        break;
                    }
                }
                if (matching == null)
                {
                    int index = events.arraySize;
                    events.InsertArrayElementAtIndex(index);
                    matching = events.GetArrayElementAtIndex(index);
                    matching.FindPropertyRelative("displayName").stringValue = displayName;
                    matching.FindPropertyRelative("animationFps").floatValue = 6f;
                }
                SetObjectArray(matching.FindPropertyRelative("frames"), LoadSprites(folder));
            }
        }

        /// <summary>아트 폴더의 현재 Sprite 목록으로 각 공격의 본체 frames만 다시 채운다. overlayFrames는
        /// 건드리지 않는다 - Sync는 인덱스를 통째로 다시 매기는 작업이라 여기서 함께 잘라내면 이미 배치해둔
        /// 오버레이가 조용히 사라진다. 대신 길이가 어긋나게 된 모션을 overlayMismatches에 모아 호출한 쪽이
        /// 경고할 수 있게 한다.</summary>
        private static void SyncAttackFrames(ResourceEntry entry, List<string> overlayMismatches)
        {
            var foldersByTier = new Dictionary<int, List<string>>
            {
                { 1, new List<string>() },
                { 2, new List<string>() },
                { 3, new List<string>() },
            };
            foreach (string folder in AssetDatabase.GetSubFolders(entry.FolderPath))
            {
                string name = Path.GetFileName(folder);
                if (!name.StartsWith("attack", StringComparison.OrdinalIgnoreCase)) continue;
                int tier = name.IndexOf("tier3", StringComparison.OrdinalIgnoreCase) >= 0 ? 3
                    : name.IndexOf("tier2", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 1;
                foldersByTier[tier].Add(folder);
            }

            for (int tier = 1; tier <= 3; tier++)
            {
                foldersByTier[tier].Sort(StringComparer.OrdinalIgnoreCase);
                ComboTierAttackPool pool = GetPool(entry.CharacterProfile, tier);
                if (pool == null) continue;
                int count = Mathf.Min(foldersByTier[tier].Count, pool.Motions.Count);
                for (int i = 0; i < count; i++)
                {
                    AttackMotionDefinition motion = pool.Motions[i];
                    if (motion == null) continue;
                    var serializedMotion = new SerializedObject(motion);
                    SerializedProperty motionFrames = serializedMotion.FindProperty("frames");
                    int overlayCount = serializedMotion.FindProperty("overlayFrames").arraySize;
                    int beforeCount = motionFrames.arraySize;
                    SetObjectArray(motionFrames, LoadSprites(foldersByTier[tier][i]));
                    serializedMotion.ApplyModifiedProperties();
                    EditorUtility.SetDirty(motion);
                    if (overlayCount > 0 && motionFrames.arraySize != beforeCount)
                    {
                        overlayMismatches.Add($"• {motion.name}: 본체 {beforeCount} → {motionFrames.arraySize} 프레임, Overlay {overlayCount}개 유지");
                    }
                }
            }
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value.Replace(' ', '_');
        }

        private static string ToDisplayName(string folderName)
        {
            string value = folderName.Replace('_', ' ').Replace('-', ' ');
            return string.IsNullOrWhiteSpace(value) ? "Motion" : value;
        }

        private void OnEditorUpdate()
        {
            if (chargeSimRunning)
            {
                // 누적 입력 시뮬레이션이 도는 동안에는 그것이 프레임을 결정한다(시간 기반 자동 재생 대신).
                AdvanceChargeSimulation();
                return;
            }
            if (!previewPlaying) return;
            float duration = GetPreviewDuration();
            if (duration <= 0f)
            {
                previewPlaying = false;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double delta = Math.Max(0d, now - previewLastStepTime);
            previewLastStepTime = now;
            previewElapsedTime += delta;
            if (previewElapsedTime >= duration)
            {
                if (previewLoop)
                {
                    previewElapsedTime %= duration;
                    // 새 루프 구간 시작 - 이번 패스에서 다시 정방향으로 통과하므로 Cue를 다시 재생할 수 있게 한다.
                    previewCastCueFired = false;
                    previewHitCueFired = false;
                }
                else
                {
                    previewElapsedTime = duration;
                    previewPlaying = false;
                }
            }

            EvaluatePreviewAudioCues();

            PreviewMotion main = GetMainPreviewMotion();
            previewFrameIndex = GetFrameIndex(main, (float)previewElapsedTime, previewLoop);
            Repaint();
        }

        /// <summary>재생 중(autoplay) 프레임마다 호출된다 - 프레임 드래그/스크럽은 previewPlaying을
        /// false로 만들기 때문에 이 메서드 자체를 타지 않는다(스크럽 중 반복 재생 방지는 그래서 별도
        /// 처리 없이 이 게이트만으로 충분하다). Cast/Hit 각각 공격당(또는 루프 패스당) 정확히 한 번만,
        /// 그 Cue 시각을 정방향으로 지나가는 순간에 재생한다.</summary>
        private void EvaluatePreviewAudioCues()
        {
            PreviewMotion attack = GetActivePreviewAttackMotion();
            if (attack?.Attack == null || attack.Frames.Length == 0) return;

            float fps = Mathf.Max(0.01f, attack.Fps);
            float castTime = Mathf.Clamp(attack.Attack.CastFrameIndex, 0, attack.Frames.Length - 1) / fps;
            float hitTime = Mathf.Clamp(attack.HitFrame, 0, attack.Frames.Length - 1) / fps;

            if (!previewCastCueFired && previewElapsedTime >= castTime)
            {
                previewCastCueFired = true;
                PlayPreviewClip(attack.Attack.CastSound);
            }
            if (!previewHitCueFired && previewElapsedTime >= hitTime)
            {
                previewHitCueFired = true;
                PlayPreviewClip(attack.Attack.HitSound);
            }
        }

        /// <summary>UnityEditor.AudioUtil은 internal이라 리플렉션으로 호출한다 - Unity 버전에 따라
        /// PlayPreviewClip(최신) 또는 PlayClip(구버전) 중 있는 쪽을 쓴다. 둘 다 없거나 clip이 비어
        /// 있으면 조용히 무시한다(기본 Cast/Hit 사운드는 없다는 규칙과 동일하게).</summary>
        private static void PlayPreviewClip(AudioClip clip)
        {
            if (clip == null) return;

            Type audioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            if (audioUtilType == null) return;

            MethodInfo method = audioUtilType.GetMethod(
                "PlayPreviewClip",
                BindingFlags.Static | BindingFlags.Public,
                null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
            if (method != null)
            {
                method.Invoke(null, new object[] { clip, 0, false });
                return;
            }

            method = audioUtilType.GetMethod("PlayClip", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(AudioClip) }, null);
            method?.Invoke(null, new object[] { clip });
        }

        private SerializedProperty GetActiveFrames()
        {
            if (workspace == Workspace.Attack && attackObject != null)
            {
                attackObject.Update();
                return attackObject.FindProperty("frames");
            }
            SerializedProperty clip = GetActiveClip();
            return clip?.FindPropertyRelative("frames");
        }

        private SerializedProperty GetActiveFps()
        {
            if (workspace == Workspace.Attack && attackObject != null) return attackObject.FindProperty("animationFps");
            return GetActiveClip()?.FindPropertyRelative("animationFps");
        }

        private SerializedProperty GetActiveClip()
        {
            if (activeProfileObject == null) return null;
            activeProfileObject.Update();
            if (workspace == Workspace.Idle) return activeProfileObject.FindProperty("baseIdle");
            if (workspace == Workspace.IdleEvents)
            {
                SerializedProperty events = activeProfileObject.FindProperty("idleEvents");
                if (selectedIdleEventIndex >= 0 && selectedIdleEventIndex < events.arraySize) return events.GetArrayElementAtIndex(selectedIdleEventIndex);
            }
            if (workspace == Workspace.Hit) return activeProfileObject.FindProperty("hit");
            if (workspace == Workspace.Defeat) return activeProfileObject.FindProperty("defeat");
            // Overview, Movement 또는 아직 선택된 공격이 없는 상태에서도 프리뷰는 Base Idle을 유지한다.
            return activeProfileObject.FindProperty("baseIdle");
        }

        private int GetPreviewFrameCount()
        {
            SerializedProperty frames = GetActiveFrames();
            return frames != null ? frames.arraySize : rawIdlePreviewFrames.Count;
        }

        private float GetPreviewFps()
        {
            SerializedProperty fps = GetActiveFps();
            return fps != null ? Mathf.Max(0.01f, fps.floatValue) : 6f;
        }

        private int GetPreviewHitFrame()
        {
            if (!IsAttackPreview() || attackObject == null) return -1;
            attackObject.Update();
            return attackObject.FindProperty("hitFrameIndex").intValue;
        }

        private bool IsAttackPreview()
        {
            return actorKind == ActorKind.Character && workspace == Workspace.Attack && selectedAttack != null;
        }

        private Sprite GetPreviewSprite(int index)
        {
            SerializedProperty frames = GetActiveFrames();
            if (frames != null && index >= 0 && index < frames.arraySize)
            {
                return frames.GetArrayElementAtIndex(index).objectReferenceValue as Sprite;
            }
            return index >= 0 && index < rawIdlePreviewFrames.Count ? rawIdlePreviewFrames[index] : null;
        }

        private float ComputeActiveFitZoom()
        {
            SerializedProperty frames = GetActiveFrames();
            return frames != null ? ComputeFitZoom(frames) : ComputeFitZoom(rawIdlePreviewFrames);
        }
    }
}
