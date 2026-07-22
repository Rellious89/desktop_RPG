using System;
using System.Collections.Generic;
using System.IO;
using Character;
using Enemy;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

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

        private enum ActorKind { Character, Monster }
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

        private SerializedObject activeProfileObject;
        private AttackMotionDefinition selectedAttack;
        private SerializedObject attackObject;
        private ComboTierAttackPool activePool;
        private SerializedObject poolObject;
        private ReorderableList frameList;
        private SerializedObject frameListOwner;
        private string frameListPropertyPath;

        private bool previewPlaying;
        private bool previewLoop = true;
        private int previewFrameIndex;
        private double previewLastStepTime;
        private double previewElapsedTime;
        private float previewZoom = DefaultZoom;

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
        private HistoryAction pendingHistoryAction;
        private GUIStyle hitLabelStyle;
        private GUIStyle hitTagStyle;
        private GUIStyle toolbarStatusStyle;

        private ResourceEntry SelectedResource => selectedResourceIndex >= 0 && selectedResourceIndex < resources.Count
            ? resources[selectedResourceIndex]
            : null;

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
                Frames = ReadSpriteArray(serialized.FindProperty("frames"))
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
            return AppendFrameChanges(changes, slot, before.Frames, after.Frames) || changed;
        }

        private static bool AppendFrameChanges(List<string> changes, string slot, Sprite[] before, Sprite[] after)
        {
            if (before.Length != after.Length)
            {
                changes.Add($"{slot}: 프레임 수 변경 ({before.Length} → {after.Length})");
                return true;
            }
            bool changed = false;
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] == after[i]) continue;
                changes.Add($"{slot}: {i + 1}번 프레임 스프라이트 변경");
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
            RebuildFrameList();
            RestartPreview();
            Repaint();
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
            DrawWorkspaceButton(Workspace.Movement, "Attack Movement");
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
            RebuildFrameList();
            if (motion != null) RestartPreview();
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
                    EditorGUILayout.LabelField("Attack Movement", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(activeProfileObject.FindProperty("attackMovement"), GUIContent.none, true);
                    EditorGUILayout.HelpBox("이 값은 캐릭터별로 저장되며, 프로필이 연결된 AttackMovement가 우선 사용합니다.", MessageType.Info);
                    break;
            }
            if (activeProfileObject.ApplyModifiedProperties()) EditorUtility.SetDirty(entry.CharacterProfile);
        }

        private void DrawMonsterInspector(ResourceEntry entry)
        {
            activeProfileObject.Update();
            switch (workspace)
            {
                case Workspace.Overview:
                    DrawOverview(activeProfileObject, "Monster Setup");
                    EditorGUILayout.PropertyField(activeProfileObject.FindProperty("preview"), new GUIContent("Placement & Receive Point"), true);
                    break;
                case Workspace.Idle:
                    DrawClipEditor(activeProfileObject.FindProperty("baseIdle"), false, -1, false);
                    break;
                case Workspace.IdleEvents:
                    DrawSelectedIdleEventEditor();
                    break;
                case Workspace.Hit:
                    EditorGUILayout.PropertyField(activeProfileObject.FindProperty("hitReaction"), GUIContent.none, true);
                    DrawClipEditor(activeProfileObject.FindProperty("hit"), false, -1, true);
                    break;
                case Workspace.Defeat:
                    DrawClipEditor(activeProfileObject.FindProperty("defeat"), false, -1, false);
                    break;
            }
            if (activeProfileObject.ApplyModifiedProperties()) EditorUtility.SetDirty(entry.MonsterProfile);
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
            EditorGUILayout.LabelField("Combat Preview Placement", EditorStyles.boldLabel);
            SerializedProperty preview = activeProfileObject.FindProperty("preview");
            EditorGUILayout.PropertyField(preview.FindPropertyRelative("characterOffset"));
            EditorGUILayout.PropertyField(preview.FindPropertyRelative("targetOffset"));
            EditorGUILayout.PropertyField(preview.FindPropertyRelative("characterScale"));
            EditorGUILayout.PropertyField(preview.FindPropertyRelative("targetScale"));
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
            EditorGUILayout.LabelField(selectedAttack.name, EditorStyles.boldLabel);
            DrawDescriptionEditor(attackObject.FindProperty("editorDescription"));
            SerializedProperty frames = attackObject.FindProperty("frames");
            SerializedProperty fps = attackObject.FindProperty("animationFps");
            SerializedProperty hit = attackObject.FindProperty("hitFrameIndex");

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(fps, new GUIContent("FPS"));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUILayout.PropertyField(hit, new GUIContent("Hit Frame"));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(attackObject.FindProperty("endFrameDuration"), new GUIContent("End Hold"));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
                EditorGUILayout.PropertyField(attackObject.FindProperty("queueExpireTimeout"), new GUIContent("Queue Window"));
                RegisterTextInputPointerDown(GUILayoutUtility.GetLastRect());
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Hit Presentation", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(attackObject.FindProperty("hitEffectPrefab"), new GUIContent("Effect Prefab"));
            EditorGUILayout.PropertyField(attackObject.FindProperty("hitEffectOffset"), new GUIContent("Effect Offset"));
            EditorGUILayout.PropertyField(attackObject.FindProperty("hitEffectScale"), new GUIContent("Effect Scale"));
            EditorGUILayout.PropertyField(attackObject.FindProperty("hitSound"), new GUIContent("Hit Sound"));

            DrawFrameSection(attackObject, frames, hit);

            if (frames.arraySize > 0 && (hit.intValue < 0 || hit.intValue >= frames.arraySize))
            {
                EditorGUILayout.HelpBox($"Hit Frame은 0~{frames.arraySize - 1} 범위여야 합니다.", MessageType.Warning);
            }
            if (attackObject.ApplyModifiedProperties()) EditorUtility.SetDirty(selectedAttack);
        }

        private void DrawMotionNameEditor(SerializedProperty name)
        {
            Rect rect = EditorGUILayout.GetControlRect();
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

        private void DrawFrameSection(SerializedObject owner, SerializedProperty frames, SerializedProperty hitFrame)
        {
            if (frameList == null || frameListOwner != owner || frameListPropertyPath != frames.propertyPath)
            {
                frameList = BuildFrameList(owner, frames, hitFrame);
                frameListOwner = owner;
                frameListPropertyPath = frames.propertyPath;
            }
            frameList.DoLayoutList();
            DrawFrameDropZone(frames);
        }

        private ReorderableList BuildFrameList(SerializedObject owner, SerializedProperty frames, SerializedProperty hitFrame)
        {
            var list = new ReorderableList(owner, frames, true, true, true, true) { elementHeight = 48f };
            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Sprite Frames");
            list.drawElementCallback = (rect, index, active, focused) =>
            {
                SerializedProperty element = frames.GetArrayElementAtIndex(index);
                Sprite sprite = element.objectReferenceValue as Sprite;
                bool isHit = hitFrame != null && hitFrame.intValue == index;
                if (isHit) EditorGUI.DrawRect(rect, new Color(1f, 0.3f, 0.2f, 0.15f));
                Rect thumb = new Rect(rect.x + 2f, rect.y + 4f, 40f, 40f);
                Rect field = new Rect(rect.x + 48f, rect.y + 14f, rect.width - 108f, EditorGUIUtility.singleLineHeight);
                Rect tag = new Rect(rect.xMax - 54f, rect.y + 14f, 52f, EditorGUIUtility.singleLineHeight);
                DrawThumbnail(thumb, sprite);
                EditorGUI.ObjectField(field, element, typeof(Sprite), GUIContent.none);
                EditorGUI.LabelField(tag, isHit ? "HIT" : "#" + index, isHit ? HitTagStyle : EditorStyles.miniLabel);
            };
            list.onSelectCallback = l =>
            {
                previewPlaying = false;
                previewFrameIndex = Mathf.Max(0, l.index);
                previewElapsedTime = previewFrameIndex / Mathf.Max(0.01f, GetPreviewFps());
            };
            return list;
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

        private static void DrawFrameDropZone(SerializedProperty frames)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, "Drop Sprites Here", EditorStyles.helpBox);
            Event evt = Event.current;
            if (!rect.Contains(evt.mousePosition) || (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (UnityEngine.Object item in DragAndDrop.objectReferences)
                {
                    if (item is Sprite sprite) AppendObjectReference(frames, sprite);
                    else
                    {
                        string path = AssetDatabase.GetAssetPath(item);
                        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        {
                            if (asset is Sprite subSprite) AppendObjectReference(frames, subSprite);
                        }
                    }
                }
            }
            evt.Use();
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
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(LeftWorkspaceWidth), GUILayout.Height(StageHeight + 116f)))
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

            Vector2 characterOffset = Vector2.zero;
            Vector2 targetOffset = new Vector2(1.15f, 0f);
            float characterScale = 1f;
            float targetScale = 1f;
            float moveDistance = 0f;
            float moveOut = 0.14f;
            float moveBack = 0.05f;

            if (character?.CharacterProfile != null)
            {
                CharacterMotionProfile.PreviewSettings preview = character.CharacterProfile.Preview;
                characterOffset = preview.CharacterOffset;
                targetOffset = preview.TargetOffset;
                characterScale = preview.CharacterScale;
                targetScale = preview.TargetScale;
                CharacterMotionProfile.AttackMovementSettings movement = character.CharacterProfile.AttackMovement;
                if (movement.OverrideComponentValues)
                {
                    moveDistance = movement.MoveDistance;
                    moveOut = movement.MoveOutDuration;
                    moveBack = movement.MoveBackDuration;
                }
            }

            float moveX = attack != null ? EvaluateMovement(time, moveDistance, moveOut, moveBack) : 0f;
            Vector2 characterAnchor = baseAnchor + WorldToScreen(characterOffset + new Vector2(moveX, 0f), worldToScreen);
            Vector2 targetAnchor = baseAnchor + WorldToScreen(targetOffset, worldToScreen);

            Vector2 receiveOffset = new Vector2(0f, 0.35f);
            float monsterScale = 1f;
            if (monster?.MonsterProfile != null)
            {
                receiveOffset = monster.MonsterProfile.Preview.ReceivePointOffset;
                monsterScale = monster.MonsterProfile.Preview.ActorScale;
            }
            Vector2 receivePoint = targetAnchor + WorldToScreen(receiveOffset, worldToScreen);

            if (monsterSprite != null) DrawSprite(monsterSprite, previewZoom * targetScale * monsterScale, targetAnchor, hitStarted ? new Color(1f, 0.68f, 0.68f) : Color.white);
            if (characterSprite != null) DrawSprite(characterSprite, previewZoom * characterScale, characterAnchor, Color.white);

            bool exactHit = attack != null && GetFrameIndex(attack, time, false) == Mathf.Clamp(attack.HitFrame, 0, attack.Frames.Length - 1);
            if (exactHit && attack.Attack != null)
            {
                GameObject prefab = attack.Attack.HitEffectPrefab;
                Sprite effectSprite = prefab != null ? prefab.GetComponentInChildren<SpriteRenderer>()?.sprite : null;
                if (effectSprite != null)
                {
                    Vector2 effectOffset = attack.Attack.HitEffectOffset;
                    float effectScale = attack.Attack.HitEffectScale;
                    DrawSprite(effectSprite, previewZoom * Mathf.Max(0.01f, effectScale), receivePoint + WorldToScreen(effectOffset, worldToScreen), Color.white);
                }
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
            if (attack != null && hit != null)
            {
                float hitTime = Mathf.Clamp(attack.HitFrame, 0, Mathf.Max(0, attack.Frames.Length - 1)) / Mathf.Max(0.01f, attack.Fps);
                return Mathf.Max(attack.Duration, hitTime + hit.Duration);
            }
            return Mathf.Max(main.Duration, opponent?.Duration ?? 0f);
        }

        private void RestartPreview()
        {
            PreviewMotion motion = GetMainPreviewMotion();
            previewElapsedTime = 0d;
            previewFrameIndex = 0;
            previewLoop = motion != null && motion.Kind == PreviewMotionKind.Idle;
            previewPlaying = motion != null && motion.Frames.Length > 0;
            previewLastStepTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void TogglePreviewPlayback()
        {
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
        }

        private void StopPreview()
        {
            previewPlaying = false;
            previewElapsedTime = 0d;
            previewFrameIndex = 0;
            Repaint();
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
            if (time <= 0f || distance <= 0f) return 0f;
            if (time < outDuration) return distance * Mathf.Clamp01(time / outDuration);
            if (time < outDuration + backDuration) return Mathf.Lerp(distance, 0f, (time - outDuration) / backDuration);
            return 0f;
        }

        private static void DrawMarker(Vector2 anchor, Color color)
        {
            const float size = 5f;
            EditorGUI.DrawRect(new Rect(anchor.x - size, anchor.y - 1f, size * 2f, 2f), color);
            EditorGUI.DrawRect(new Rect(anchor.x - 1f, anchor.y - size, 2f, size * 2f), color);
        }

        private static void DrawReceivePoint(Vector2 point, bool isHit)
        {
            Color color = isHit ? new Color(1f, 0.2f, 0.1f) : new Color(1f, 0.45f, 0.2f);
            const float radius = 8f;
            EditorGUI.DrawRect(new Rect(point.x - radius, point.y - 1f, radius * 2f, 2f), color);
            EditorGUI.DrawRect(new Rect(point.x - 1f, point.y - radius, 2f, radius * 2f), color);
            GUI.Label(new Rect(point.x + 6f, point.y - 17f, 90f, 18f), "Receive Point", EditorStyles.miniLabel);
        }

        private static void DrawSprite(Sprite sprite, float zoom, Vector2 anchor, Color tint)
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
            Rect drawRect = new Rect(anchor.x - px * width, anchor.y - height * (1f - py), width, height);
            Rect uv = rect;
            uv.x /= texture.width;
            uv.width /= texture.width;
            uv.y /= texture.height;
            uv.height /= texture.height;
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
                    "아트 하위 폴더의 현재 Sprite 목록으로 프레임 배열만 갱신합니다. FPS, Hit Frame, Movement, Effect 설정은 유지됩니다.",
                    "Sync",
                    "Cancel")) return;

            activeProfileObject.Update();
            SetObjectArray(activeProfileObject.FindProperty("baseIdle").FindPropertyRelative("frames"),
                LoadSprites(FindMotionFolder(entry.FolderPath, "idle")));
            SyncIdleEventFrames(activeProfileObject.FindProperty("idleEvents"), entry.FolderPath);

            if (entry.Kind == ActorKind.Character)
            {
                SyncAttackFrames(entry);
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

        private static void SyncAttackFrames(ResourceEntry entry)
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
                    SetObjectArray(serializedMotion.FindProperty("frames"), LoadSprites(foldersByTier[tier][i]));
                    serializedMotion.ApplyModifiedProperties();
                    EditorUtility.SetDirty(motion);
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
                if (previewLoop) previewElapsedTime %= duration;
                else
                {
                    previewElapsedTime = duration;
                    previewPlaying = false;
                }
            }
            PreviewMotion main = GetMainPreviewMotion();
            previewFrameIndex = GetFrameIndex(main, (float)previewElapsedTime, previewLoop);
            Repaint();
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
