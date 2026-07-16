using System;
using System.Collections.Generic;
using System.IO;
using Character;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace CharacterEditor
{
    /// <summary>
    /// 공격 모션(AttackMotionDefinition/ComboTierAttackPool) 제작 전용 툴. 런타임 전투 코드는
    /// 전혀 건드리지 않고, 기존 ScriptableObject 에셋을 SerializedObject/SerializedProperty로
    /// 직접 읽고 쓴다 - 데이터를 복제하거나 별도로 캐시하지 않는다.
    ///
    /// 좌측 캐릭터 라이브러리는 Assets/Data/&lt;캐릭터명&gt;/ 폴더 구조와, 그 폴더 안 ComboTierAttackPool
    /// 에셋 파일명에 "Tier1"/"Tier2"/"Tier3" 문자열이 포함되어 있는지로 자동 인식한다(현재
    /// CatKnight_Tier1AttackPool.asset 등 실제 명명 규칙과 일치). 이 규칙에 맞지 않는 폴더/에셋은
    /// 목록에 나타나지 않는다.
    /// </summary>
    public class MotionEditorWindow : EditorWindow
    {
        private const string DataRootPath = "Assets/Data";

        private enum MotionCategory { Idle, Attack, Skill, Hit, Defeat }

        private class CharacterEntry
        {
            public string Name;
            public ComboTierAttackPool Tier1;
            public ComboTierAttackPool Tier2;
            public ComboTierAttackPool Tier3;
        }

        [MenuItem("Tools/KeyBuddy/Motion Editor")]
        private static void Open()
        {
            var window = GetWindow<MotionEditorWindow>("Motion Editor");
            window.minSize = new Vector2(760, 420);
        }

        // ---- 좌측: 캐릭터 라이브러리 ----
        private readonly List<CharacterEntry> characters = new List<CharacterEntry>();
        private int selectedCharacterIndex = -1;
        private MotionCategory selectedCategory = MotionCategory.Attack;

        // ---- 중앙: 선택된 티어 풀의 모션 목록 ----
        private ComboTierAttackPool activePool;
        private SerializedObject poolSerializedObject;
        private ReorderableList motionList;

        // ---- 우측: 선택된 모션 편집 ----
        private AttackMotionDefinition selectedMotion;
        private SerializedObject motionSerializedObject;
        private ReorderableList frameList;

        // ---- 미리보기(에디터 전용, 씬/런타임 상태 변경 없음) ----
        private bool previewPlaying;
        private bool previewLoop = true;
        private int previewFrameIndex;
        private double previewLastStepTime;

        // Preview Stage: 프레임마다 이미지 크기에 맞춰 따로 확대/축소하지 않고, 고정된 영역 안에서
        // 모든 프레임을 같은 Anchor Point(바닥 중앙)에 Pivot 기준으로 정렬해 그린다.
        private const float PreviewStageWidth = 260f;
        private const float PreviewStageHeight = 220f;
        private const float PreviewGroundRatio = 0.78f; // 스테이지 높이 기준 위에서부터 이 비율 지점이 바닥 기준선이다.
        private const float PreviewZoomMin = 0.25f;
        private const float PreviewZoomMax = 8f;
        private const float PreviewDefaultZoom = 4f;
        private const float StageFitMargin = 6f; // Fit 계산 시 Stage 가장자리에 딱 붙지 않도록 두는 여백(픽셀).
        private float previewZoom = PreviewDefaultZoom;

        private Vector2 leftScroll;
        private Vector2 midScroll;
        private Vector2 rightScroll;

        private GUIStyle hitLabelStyle;
        private GUIStyle hitTagStyle;

        private void OnEnable()
        {
            RefreshCharacterList();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            SaveAllDirtyAssets(); // 툴 창을 닫아도 지금까지의 편집 내용이 디스크에 남도록 보장한다.
        }

        private void OnLostFocus()
        {
            SaveAllDirtyAssets(); // 에디터의 다른 창으로 포커스가 넘어갈 때도 즉시 저장해둔다.
        }

        private void OnGUI()
        {
            DrawToolbar();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawLeftPanel();
                DrawMiddlePanel();
                DrawRightPanel();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    RefreshCharacterList();
                }

                if (GUILayout.Button("Save Now", EditorStyles.toolbarButton, GUILayout.Width(70)))
                {
                    SaveAllDirtyAssets();
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("KeyBuddy Motion Editor - MVP: Attack만 지원", EditorStyles.miniLabel, GUILayout.Width(280));
            }
        }

        // ==================== 좌측: 캐릭터 라이브러리 ====================

        private void RefreshCharacterList()
        {
            characters.Clear();

            if (AssetDatabase.IsValidFolder(DataRootPath))
            {
                foreach (string folderPath in AssetDatabase.GetSubFolders(DataRootPath))
                {
                    ComboTierAttackPool tier1 = FindPoolInFolder(folderPath, 1);
                    ComboTierAttackPool tier2 = FindPoolInFolder(folderPath, 2);
                    ComboTierAttackPool tier3 = FindPoolInFolder(folderPath, 3);

                    if (tier1 == null && tier2 == null && tier3 == null) continue;

                    characters.Add(new CharacterEntry
                    {
                        Name = Path.GetFileName(folderPath),
                        Tier1 = tier1,
                        Tier2 = tier2,
                        Tier3 = tier3,
                    });
                }
            }

            if (selectedCharacterIndex >= characters.Count) selectedCharacterIndex = characters.Count - 1;
            if (selectedCharacterIndex < 0 && characters.Count > 0) selectedCharacterIndex = 0;
        }

        private static ComboTierAttackPool FindPoolInFolder(string folderPath, int tier)
        {
            string tierTag = "Tier" + tier;
            foreach (string guid in AssetDatabase.FindAssets("t:ComboTierAttackPool", new[] { folderPath }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (fileName.IndexOf(tierTag, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return AssetDatabase.LoadAssetAtPath<ComboTierAttackPool>(path);
                }
            }
            return null;
        }

        private void DrawLeftPanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(190)))
            {
                EditorGUILayout.LabelField("Library", EditorStyles.boldLabel);

                leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

                if (characters.Count == 0)
                {
                    EditorGUILayout.HelpBox($"{DataRootPath} 아래에서 ComboTierAttackPool 에셋을 찾지 못했습니다.", MessageType.Info);
                }

                for (int i = 0; i < characters.Count; i++)
                {
                    DrawCharacterNode(i);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawCharacterNode(int index)
        {
            CharacterEntry entry = characters[index];
            bool isSelectedCharacter = index == selectedCharacterIndex;

            EditorGUILayout.LabelField(entry.Name, EditorStyles.boldLabel);

            EditorGUI.indentLevel++;

            foreach (MotionCategory category in Enum.GetValues(typeof(MotionCategory)))
            {
                // MVP 범위: Attack만 실제로 편집 가능하다. 나머지는 향후 확장을 위해 항목만 보여준다.
                bool supported = category == MotionCategory.Attack;

                using (new EditorGUI.DisabledScope(!supported))
                {
                    if (GUILayout.Button(category.ToString(), EditorStyles.label))
                    {
                        selectedCharacterIndex = index;
                        selectedCategory = category;
                    }
                }

                if (supported && isSelectedCharacter && selectedCategory == category)
                {
                    EditorGUI.indentLevel++;
                    DrawTierButton(entry.Tier1, "Tier 1 Pool");
                    DrawTierButton(entry.Tier2, "Tier 2 Pool");
                    DrawTierButton(entry.Tier3, "Tier 3 Pool");
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
        }

        private void DrawTierButton(ComboTierAttackPool pool, string label)
        {
            using (new EditorGUI.DisabledScope(pool == null))
            {
                bool isSelected = pool != null && activePool == pool;
                Color previousColor = GUI.backgroundColor;
                if (isSelected) GUI.backgroundColor = Color.cyan;

                if (GUILayout.Button(label))
                {
                    SelectPool(pool);
                }

                GUI.backgroundColor = previousColor;
            }
        }

        private void SelectPool(ComboTierAttackPool pool)
        {
            if (activePool == pool) return;

            activePool = pool;
            poolSerializedObject = pool != null ? new SerializedObject(pool) : null;
            motionList = poolSerializedObject != null ? BuildMotionList(poolSerializedObject) : null;
            SetSelectedMotion(null);
        }

        // ==================== 중앙: 선택된 풀의 모션 목록 ====================

        private ReorderableList BuildMotionList(SerializedObject serializedPool)
        {
            SerializedProperty motionsProp = serializedPool.FindProperty("motions");

            var list = new ReorderableList(serializedPool, motionsProp, true, true, true, true);

            list.drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, activePool != null ? activePool.name : "Motions");
            };

            list.drawElementCallback = (rect, index, active, focused) =>
            {
                SerializedProperty element = motionsProp.GetArrayElementAtIndex(index);
                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.ObjectField(rect, element, typeof(AttackMotionDefinition), GUIContent.none);
            };

            list.onSelectCallback = l =>
            {
                SerializedProperty element = motionsProp.GetArrayElementAtIndex(l.index);
                SetSelectedMotion(element.objectReferenceValue as AttackMotionDefinition);
            };

            // 기본 Add는 마지막 항목을 복제하는 경우가 있어, 항상 빈 슬롯을 추가하도록 직접 구현한다 -
            // 이후 슬롯에 에셋을 드래그하거나 오브젝트 필드로 지정하면 된다(같은 모션 중복 등록 허용).
            list.onAddCallback = l =>
            {
                int index = motionsProp.arraySize;
                motionsProp.arraySize++;
                motionsProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
                l.index = index;
            };

            list.onRemoveCallback = l =>
            {
                ReorderableList.defaultBehaviours.DoRemoveButton(l);
                SetSelectedMotion(null);
            };

            return list;
        }

        private void DrawMiddlePanel()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(280)))
            {
                if (activePool == null)
                {
                    EditorGUILayout.LabelField("Motions", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("좌측에서 Tier Pool을 선택하세요.", MessageType.Info);
                    return;
                }

                poolSerializedObject.Update();

                midScroll = EditorGUILayout.BeginScrollView(midScroll);
                motionList.DoLayoutList();
                EditorGUILayout.EndScrollView();

                if (poolSerializedObject.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(activePool);
                }
            }
        }

        // ==================== 우측: 선택된 모션 편집 + 미리보기 ====================

        private void SetSelectedMotion(AttackMotionDefinition motion)
        {
            if (selectedMotion == motion) return;

            selectedMotion = motion;
            motionSerializedObject = motion != null ? new SerializedObject(motion) : null;
            frameList = motionSerializedObject != null ? BuildFrameList(motionSerializedObject) : null;

            previewPlaying = false;
            previewFrameIndex = 0;

            // 모션을 새로 선택하면 기본 줌을 "전체 프레임이 안 잘리는" Fit Motion 값으로 맞춰준다.
            // 이후에는 사용자가 슬라이더/Fit 버튼으로 자유롭게 바꿀 수 있다.
            previewZoom = motionSerializedObject != null
                ? ComputeFitMotionZoom(motionSerializedObject.FindProperty("frames"))
                : PreviewDefaultZoom;
        }

        private ReorderableList BuildFrameList(SerializedObject serializedMotion)
        {
            SerializedProperty framesProp = serializedMotion.FindProperty("frames");

            var list = new ReorderableList(serializedMotion, framesProp, true, true, true, true);
            list.elementHeight = 48;

            list.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Frames");

            list.drawElementCallback = (rect, index, active, focused) =>
            {
                SerializedProperty element = framesProp.GetArrayElementAtIndex(index);
                var sprite = element.objectReferenceValue as Sprite;

                SerializedProperty hitFrameProp = serializedMotion.FindProperty("hitFrameIndex");
                bool isHitFrame = hitFrameProp != null && hitFrameProp.intValue == index;

                if (isHitFrame)
                {
                    EditorGUI.DrawRect(rect, new Color(1f, 0.3f, 0.3f, 0.15f));
                }

                var thumbRect = new Rect(rect.x + 2, rect.y + 4, 40, 40);
                var fieldRect = new Rect(rect.x + 48, rect.y + 14, rect.width - 48 - 56, EditorGUIUtility.singleLineHeight);
                var tagRect = new Rect(rect.xMax - 50, rect.y + 14, 50, EditorGUIUtility.singleLineHeight);

                DrawSpriteThumbnail(thumbRect, sprite);

                EditorGUI.ObjectField(fieldRect, element, typeof(Sprite), GUIContent.none);
                EditorGUI.LabelField(tagRect, isHitFrame ? "HIT" : "#" + index, isHitFrame ? HitTagStyle : EditorStyles.miniLabel);
            };

            return list;
        }

        private static void DrawSpriteThumbnail(Rect rect, Sprite sprite)
        {
            if (sprite == null)
            {
                EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.15f));
                return;
            }

            Texture2D preview = AssetPreview.GetAssetPreview(sprite);
            if (preview == null) preview = AssetPreview.GetMiniThumbnail(sprite);
            if (preview != null)
            {
                GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
            }
        }

        private void DrawRightPanel()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (selectedMotion == null)
                {
                    EditorGUILayout.LabelField("Motion", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox("중앙 목록에서 모션을 선택하세요.", MessageType.Info);
                    return;
                }

                motionSerializedObject.Update();

                EditorGUILayout.LabelField(selectedMotion.name, EditorStyles.boldLabel);

                rightScroll = EditorGUILayout.BeginScrollView(rightScroll);

                SerializedProperty framesProp = motionSerializedObject.FindProperty("frames");
                SerializedProperty animationFpsProp = motionSerializedObject.FindProperty("animationFps");
                SerializedProperty hitFrameIndexProp = motionSerializedObject.FindProperty("hitFrameIndex");
                SerializedProperty endFrameDurationProp = motionSerializedObject.FindProperty("endFrameDuration");
                SerializedProperty queueExpireTimeoutProp = motionSerializedObject.FindProperty("queueExpireTimeout");

                EditorGUILayout.PropertyField(animationFpsProp, new GUIContent("Animation FPS"));
                EditorGUILayout.PropertyField(hitFrameIndexProp, new GUIContent("Hit Frame Index"));
                EditorGUILayout.PropertyField(endFrameDurationProp, new GUIContent("End Frame Duration"));
                EditorGUILayout.PropertyField(queueExpireTimeoutProp, new GUIContent("Queue Expire Timeout"));

                if (framesProp.arraySize > 0 && (hitFrameIndexProp.intValue < 0 || hitFrameIndexProp.intValue > framesProp.arraySize - 1))
                {
                    // 런타임 쪽 Mathf.Clamp가 재생 시 최종 보정해주지만, 제작 단계에서 바로 알아채도록 안내만 띄운다.
                    EditorGUILayout.HelpBox($"Hit Frame Index가 프레임 범위(0~{framesProp.arraySize - 1})를 벗어났습니다 - 재생 시 자동으로 보정됩니다.", MessageType.Warning);
                }

                EditorGUILayout.Space();
                frameList.DoLayoutList();
                DrawFrameDropZone(framesProp);

                EditorGUILayout.Space();
                DrawPreview(framesProp, animationFpsProp.floatValue, hitFrameIndexProp.intValue);

                EditorGUILayout.EndScrollView();

                if (motionSerializedObject.ApplyModifiedProperties())
                {
                    EditorUtility.SetDirty(selectedMotion);
                }
            }
        }

        /// <summary>Project 창에서 Sprite(들)를 드래그해서 놓으면 frames 배열 끝에 이어서 추가한다.
        /// 텍스처가 Single Sprite 모드라면 경로만으로도 대응되는 Sprite를 바로 찾을 수 있다.</summary>
        private void DrawFrameDropZone(SerializedProperty framesProp)
        {
            Rect dropRect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "여기로 Sprite를 드래그하면 프레임으로 추가됩니다", EditorStyles.helpBox);

            Event evt = Event.current;
            if (!dropRect.Contains(evt.mousePosition)) return;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();

                foreach (string path in DragAndDrop.paths)
                {
                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite == null) continue;

                    int index = framesProp.arraySize;
                    framesProp.arraySize++;
                    framesProp.GetArrayElementAtIndex(index).objectReferenceValue = sprite;
                }
            }

            evt.Use();
        }

        private void DrawPreview(SerializedProperty framesProp, float fps, int hitFrameIndex)
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            int frameCount = framesProp.arraySize;
            if (frameCount == 0)
            {
                EditorGUILayout.HelpBox("프레임이 없습니다.", MessageType.Info);
                return;
            }

            previewFrameIndex = Mathf.Clamp(previewFrameIndex, 0, frameCount - 1);
            int clampedHitFrame = Mathf.Clamp(hitFrameIndex, 0, frameCount - 1);

            var currentSprite = framesProp.GetArrayElementAtIndex(previewFrameIndex).objectReferenceValue as Sprite;

            DrawPreviewStage(currentSprite, previewFrameIndex == clampedHitFrame, framesProp);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("|<", GUILayout.Width(28)))
                {
                    previewFrameIndex = 0;
                    previewPlaying = false;
                }
                if (GUILayout.Button("<", GUILayout.Width(28)))
                {
                    // 첫 프레임에서 누르면 마지막 프레임으로 순환한다.
                    previewFrameIndex = (previewFrameIndex - 1 + frameCount) % frameCount;
                    previewPlaying = false;
                }

                if (GUILayout.Button(previewPlaying ? "Pause" : "Play", GUILayout.Width(56)))
                {
                    previewPlaying = !previewPlaying;
                    previewLastStepTime = EditorApplication.timeSinceStartup;
                }

                if (GUILayout.Button("Stop", GUILayout.Width(48)))
                {
                    previewPlaying = false;
                    previewFrameIndex = 0;
                }

                if (GUILayout.Button(">", GUILayout.Width(28)))
                {
                    // 마지막 프레임에서 누르면 첫 프레임으로 순환한다.
                    previewFrameIndex = (previewFrameIndex + 1) % frameCount;
                    previewPlaying = false;
                }
                if (GUILayout.Button(">|", GUILayout.Width(28)))
                {
                    previewFrameIndex = frameCount - 1;
                    previewPlaying = false;
                }

                previewLoop = GUILayout.Toggle(previewLoop, "Loop", GUILayout.Width(50));
            }

            EditorGUILayout.LabelField($"Frame: {previewFrameIndex + 1} / {frameCount}    FPS: {fps:0.##}    Hit Frame: {clampedHitFrame + 1}");
        }

        /// <summary>
        /// 고정 크기 Preview Stage를 그리고, 그 안에서 바닥 기준선/Anchor Point/현재 프레임을 그린다.
        /// 스프라이트는 Stage 크기에 맞춰 자동으로 늘어나지 않는다 - 모든 프레임에 동일한 previewZoom을
        /// 적용하고, Sprite.pivot을 기준으로 같은 Anchor Point(기본 바닥 중앙)에 정렬한다. 이래야
        /// 프레임마다 발 위치가 튀는지, 상하 이동이 자연스러운지를 프리뷰만 보고 판단할 수 있다.
        /// GUI.BeginGroup으로 Stage 영역 밖으로 나가는 그림은 자동으로 잘라낸다.
        /// </summary>
        private void DrawPreviewStage(Sprite sprite, bool isHitFrame, SerializedProperty framesProp)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Zoom", GUILayout.Width(40));
                previewZoom = EditorGUILayout.Slider(previewZoom, PreviewZoomMin, PreviewZoomMax);

                if (GUILayout.Button("Fit Frame", GUILayout.Width(72)))
                {
                    if (sprite != null)
                    {
                        previewZoom = ClampZoom(ComputeMaxZoomForSprite(sprite, GetStageAnchor(), StageFitMargin));
                    }
                }

                if (GUILayout.Button("Fit Motion", GUILayout.Width(72)))
                {
                    previewZoom = ComputeFitMotionZoom(framesProp);
                }
            }

            Rect stageRect = GUILayoutUtility.GetRect(PreviewStageWidth, PreviewStageHeight, GUILayout.ExpandWidth(false));
            EditorGUI.DrawRect(stageRect, new Color(0.15f, 0.15f, 0.15f));

            GUI.BeginGroup(stageRect); // 그룹 안에서는 (0,0)이 Stage 좌상단이고, Stage 밖 그림은 잘린다.

            var localStage = new Rect(0, 0, stageRect.width, stageRect.height);
            DrawStageBorder(localStage);

            float groundY = localStage.height * PreviewGroundRatio;
            var anchor = new Vector2(localStage.width * 0.5f, groundY);

            // 바닥 기준선(가로) + 중앙 기준선(세로) - Anchor Point의 위치를 눈으로 바로 확인하기 위한 가이드.
            EditorGUI.DrawRect(new Rect(0, groundY, localStage.width, 1f), new Color(0.2f, 1f, 0.2f, 0.6f));
            EditorGUI.DrawRect(new Rect(anchor.x, 0, 1f, localStage.height), new Color(0.2f, 1f, 0.2f, 0.25f));

            if (sprite != null)
            {
                DrawSpriteAtAnchor(sprite, previewZoom, anchor);
            }

            DrawAnchorMarker(anchor);

            if (isHitFrame)
            {
                EditorGUI.LabelField(new Rect(0, 2, localStage.width, 16), "HIT!", HitLabelStyle);
            }

            GUI.EndGroup();
        }

        private static void DrawStageBorder(Rect rect)
        {
            var c = new Color(1f, 1f, 1f, 0.3f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), c);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), c);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), c);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), c);
        }

        private static void DrawAnchorMarker(Vector2 anchor)
        {
            const float size = 5f;
            var markerColor = new Color(1f, 0.9f, 0.1f, 0.9f);
            EditorGUI.DrawRect(new Rect(anchor.x - size, anchor.y - 1f, size * 2f, 2f), markerColor);
            EditorGUI.DrawRect(new Rect(anchor.x - 1f, anchor.y - size, 2f, size * 2f), markerColor);
        }

        /// <summary>
        /// sprite.pivot(스프라이트 rect 좌하단 기준, 픽셀 단위)을 정규화한 뒤, 그 지점이 정확히
        /// anchor 위치에 오도록 그린다. zoom은 텍스처 픽셀당 화면 픽셀 배율로, 모든 프레임에 동일하게
        /// 적용되어 개별 이미지 크기에 따라 자동으로 늘어나지 않는다. 픽셀 아트가 흐려지지 않도록
        /// 텍스처의 filterMode를 그리는 동안만 Point로 바꿨다가 즉시 원래 값으로 되돌린다 - 이 변경은
        /// 저장되지 않으며 텍스처 Import 설정 자체는 건드리지 않는다.
        /// </summary>
        private static void DrawSpriteAtAnchor(Sprite sprite, float zoom, Vector2 anchor)
        {
            Texture2D texture = sprite.texture;
            if (texture == null) return;

            FilterMode originalFilterMode = texture.filterMode;
            texture.filterMode = FilterMode.Point;

            Rect pixelRect = sprite.rect;
            Vector2 pivotPixels = sprite.pivot;

            float displayWidth = pixelRect.width * zoom;
            float displayHeight = pixelRect.height * zoom;

            float pivotFractionX = pixelRect.width > 0f ? pivotPixels.x / pixelRect.width : 0.5f;
            float pivotFractionY = pixelRect.height > 0f ? pivotPixels.y / pixelRect.height : 0f;

            float x = anchor.x - pivotFractionX * displayWidth;
            float y = anchor.y - displayHeight * (1f - pivotFractionY);
            var spriteRect = new Rect(x, y, displayWidth, displayHeight);

            Rect texCoords = pixelRect;
            texCoords.x /= texture.width;
            texCoords.width /= texture.width;
            texCoords.y /= texture.height;
            texCoords.height /= texture.height;

            GUI.DrawTextureWithTexCoords(spriteRect, texture, texCoords);

            texture.filterMode = originalFilterMode;
        }

        private static Vector2 GetStageAnchor()
        {
            return new Vector2(PreviewStageWidth * 0.5f, PreviewStageHeight * PreviewGroundRatio);
        }

        private static float ClampZoom(float zoom)
        {
            return Mathf.Clamp(zoom, PreviewZoomMin, PreviewZoomMax);
        }

        /// <summary>
        /// sprite 한 장 전체가 Stage 안에 잘리지 않고 들어가는 최대 zoom을 구한다. 단순히
        /// 스프라이트의 가로/세로 크기만 보는 게 아니라, Anchor Point(Pivot이 정렬되는 지점)를
        /// 기준으로 좌/우/상/하 각 방향에 남는 여유 공간과, Pivot 기준 스프라이트가 그 방향으로
        /// 뻗어나가는 거리를 각각 비교해서 4방향 중 가장 빡빡한 방향이 허용하는 zoom을 쓴다 -
        /// 예를 들어 Pivot이 발밑(bottom-center에 가까움)이면 위쪽 여유가 좁은 게 보통 병목이 된다.
        /// </summary>
        private static float ComputeMaxZoomForSprite(Sprite sprite, Vector2 anchor, float margin)
        {
            Rect pixelRect = sprite.rect;
            if (pixelRect.width <= 0f || pixelRect.height <= 0f) return PreviewDefaultZoom;

            Vector2 pivotPixels = sprite.pivot;
            float pivotFractionX = pivotPixels.x / pixelRect.width;
            float pivotFractionY = pivotPixels.y / pixelRect.height;

            float availLeft = Mathf.Max(1f, anchor.x - margin);
            float availRight = Mathf.Max(1f, (PreviewStageWidth - anchor.x) - margin);
            float availUp = Mathf.Max(1f, anchor.y - margin);
            float availDown = Mathf.Max(1f, (PreviewStageHeight - anchor.y) - margin);

            float maxZoom = float.MaxValue;

            float extentLeft = pivotFractionX * pixelRect.width;
            if (extentLeft > 0f) maxZoom = Mathf.Min(maxZoom, availLeft / extentLeft);

            float extentRight = (1f - pivotFractionX) * pixelRect.width;
            if (extentRight > 0f) maxZoom = Mathf.Min(maxZoom, availRight / extentRight);

            float extentUp = (1f - pivotFractionY) * pixelRect.height;
            if (extentUp > 0f) maxZoom = Mathf.Min(maxZoom, availUp / extentUp);

            float extentDown = pivotFractionY * pixelRect.height;
            if (extentDown > 0f) maxZoom = Mathf.Min(maxZoom, availDown / extentDown);

            return float.IsInfinity(maxZoom) || maxZoom <= 0f ? PreviewDefaultZoom : maxZoom;
        }

        /// <summary>
        /// 모션에 포함된 모든 프레임을 대상으로 ComputeMaxZoomForSprite를 구해서 그중 가장 작은
        /// 값(가장 빡빡하게 잘리는 프레임 기준)을 쓴다 - 이래야 재생 도중 어느 프레임에서도 잘리지
        /// 않는다(예: 검을 크게 휘두르는 프레임 하나 때문에 전체 줌이 그 프레임에 맞춰 줄어든다).
        /// </summary>
        private static float ComputeFitMotionZoom(SerializedProperty framesProp)
        {
            if (framesProp == null) return PreviewDefaultZoom;

            Vector2 anchor = GetStageAnchor();
            float minZoom = float.MaxValue;
            bool anySprite = false;

            for (int i = 0; i < framesProp.arraySize; i++)
            {
                var sprite = framesProp.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
                if (sprite == null) continue;

                minZoom = Mathf.Min(minZoom, ComputeMaxZoomForSprite(sprite, anchor, StageFitMargin));
                anySprite = true;
            }

            return ClampZoom(anySprite ? minZoom : PreviewDefaultZoom);
        }

        /// <summary>에디터 전용 재생 틱. EditorApplication.update에 매달아 두고 Play 상태일 때만
        /// FPS 간격으로 프레임을 진행시킨다 - 씬이나 런타임 컴포넌트에는 전혀 손대지 않는다.</summary>
        private void OnEditorUpdate()
        {
            if (!previewPlaying || motionSerializedObject == null) return;

            SerializedProperty framesProp = motionSerializedObject.FindProperty("frames");
            int frameCount = framesProp != null ? framesProp.arraySize : 0;
            if (frameCount == 0)
            {
                previewPlaying = false;
                return;
            }

            SerializedProperty fpsProp = motionSerializedObject.FindProperty("animationFps");
            float fps = fpsProp != null ? fpsProp.floatValue : 0f;
            if (fps <= 0f)
            {
                previewPlaying = false;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double frameDuration = 1.0 / fps;
            if (now - previewLastStepTime < frameDuration) return;

            previewLastStepTime = now;
            previewFrameIndex++;

            if (previewFrameIndex >= frameCount)
            {
                if (previewLoop)
                {
                    previewFrameIndex = 0;
                }
                else
                {
                    previewFrameIndex = frameCount - 1;
                    previewPlaying = false;
                }
            }

            Repaint();
        }

        private static void SaveAllDirtyAssets()
        {
            AssetDatabase.SaveAssets();
        }

        private GUIStyle HitLabelStyle
        {
            get
            {
                if (hitLabelStyle == null)
                {
                    hitLabelStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
                    hitLabelStyle.normal.textColor = Color.red;
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
                    hitTagStyle = new GUIStyle(EditorStyles.miniBoldLabel);
                    hitTagStyle.normal.textColor = Color.red;
                }
                return hitTagStyle;
            }
        }
    }
}
