using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Common;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StageLayoutEditor
{
    /// <summary>
    /// StageVisualRoot의 "런타임에 실제로 어떻게 보이는가"를 에디터에서 그대로 확인하고 적용하는 도구.
    ///
    /// <b>왜 필요한가</b>: <see cref="StageVisualRootController"/>는 런타임 전용이다([ExecuteAlways]가
    /// 없다). 그래서 씬을 편집할 때 StageVisualRoot는 스케일 1에 씬에 놔둔 위치 그대로 있고, 플레이를
    /// 누르는 순간에야 ApplyPlacement가 모니터 Work Area 기준으로 크기와 위치를 다시 잡는다 - 에디터에서
    /// 보고 맞춘 화면이 실제 화면과 다르다. 게다가 Work Area는 Win32에서만 들어오므로 에디터에는 값
    /// 자체가 없다.
    ///
    /// <b>어떻게 푸는가</b>: [ExecuteAlways]로 매 프레임 덮어쓰면 수동 편집과 계속 충돌하고 씬이
    /// 끊임없이 더티가 된다. 그래서 Motion Editor의 "Apply Preview Layout to Open Stage"와 같은 방식을
    /// 쓴다 - <b>누를 때만</b> 런타임과 같은 계산을 돌려 Transform에 적용하고 Undo에 등록한다.
    ///
    /// <b>같이 보여주는 것</b>: 이 도구의 절반은 계산기다. 스테이지의 화면상 크기는 카메라
    /// orthographicSize 하나로 정해지지 않고 아래 네 값이 함께 정한다.
    ///
    ///   아트 1픽셀 -> 화면 픽셀 = Bounds.Height * baseVisualScale * userScale / (2 * orthoSize * PPU)
    ///
    /// workAreaHeight가 약분되어 <b>이 배율은 해상도와 무관하게 일정하다</b>(해상도는 위치에만 영향을
    /// 준다). 픽셀 아트는 이 값이 정수가 아니면 도트가 뭉개지므로, 지금 값이 몇 배인지와 정수로 맞추려면
    /// baseVisualScale이 얼마여야 하는지를 표로 보여준다.
    /// </summary>
    public class StageLayoutWindow : EditorWindow
    {
        private const float DefaultTargetRatio = 2f;
        private static readonly float[] UserScalePresets = { 0.5f, 1f, 1.5f };

        /// <summary>도트가 뭉개지지 않는 배율 후보 - 정수 확대와 정수 분의 1 축소만 담는다.</summary>
        private static readonly float[] CleanRatioPresets = { 2f, 1f, 1f / 2f, 1f / 3f, 1f / 4f };

        /// <summary>화면 크기 토글 단계 후보. 지금 쓰는 50/100/150을 포함해 흔히 쓸 만한 값들을 훑는다.</summary>
        private static readonly float[] ZoomStepCandidates = { 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 2.5f, 3f, 4f };

        [MenuItem("Tools/KeyBuddy/Stage Layout")]
        private static void Open()
        {
            var window = GetWindow<StageLayoutWindow>("Stage Layout");
            window.minSize = new Vector2(460f, 560f);
        }

        private StageVisualRootController controller;
        private int referenceWidth = 1920;
        private int referenceHeight = 1080;
        private float targetRatio = DefaultTargetRatio;
        // 0 이하면 "아직 사용자가 정하지 않음" - 스프라이트가 가장 많은 PPU로 한 번만 채운다.
        // OnGUI에서 매번 다시 채우면 사용자가 입력한 값이 다음 프레임에 지워진다.
        private float referencePpu;
        private Vector2 scroll;

        // 화면 px 기준 크기 맞추기용. 측정 결과는 어느 스프라이트를 잰 것인지와 함께 들고 있어야
        // 기준을 바꾼 뒤에도 옛 수치를 그대로 보여주는 사고가 없다.
        private Sprite referenceSprite;
        private Sprite measuredSprite;
        private int measuredContentWidth;
        private int measuredContentHeight;
        private int measuredCanvasWidth;
        private int measuredCanvasHeight;
        // 1이면 확대 흔적 없음. N이면 NxN 블록 격자가 발견됐다는 뜻이라 1/N로 무손실 축소가 가능하다.
        private int measuredPixelDensity;
        private float targetScreenHeight = 130f;

        /// <summary>스테이지 아래에서 발견한 묶음 - 캐릭터(PPU 200)와 마을 프롭(PPU 32)처럼 PPU가 섞여
        /// 있으면 같은 설정에서도 아트 픽셀 배율이 달라지므로 따로 보여준다. <b>오브젝트의 Transform
        /// 스케일도 배율에 그대로 곱해지므로</b> PPU만이 아니라 (PPU, 스테이지 기준 상대 스케일) 짝으로
        /// 묶는다 - 같은 PPU라도 스케일이 다르면 화면에서 다른 굵기로 보인다.</summary>
        private readonly List<SpriteGroup> sceneGroups = new List<SpriteGroup>();

        /// <summary>프로젝트 전체 Sprite 에셋의 PPU 분포(버튼을 눌렀을 때만 채운다). 씬 스캔은 "지금
        /// 씬에 놓여 있는 것"만 보는데, 이 프로젝트는 캐릭터/몬스터 스프라이트를 런타임에 프로필에서
        /// 갈아끼우므로 씬에는 액터 몇 개만 존재한다 - 실제로 어떤 PPU들이 쓰이고 있는지는 에셋을
        /// 훑어야 알 수 있다.</summary>
        private readonly List<(float ppu, int count)> projectPpuGroups = new List<(float, int)>();
        private bool projectScanned;

        private struct SpriteGroup
        {
            public float Ppu;
            public float RelativeScale;
            public int Count;
            public string Sample;

            /// <summary>이 묶음에 실제로 적용되는 배율 - PPU가 작을수록, 오브젝트 스케일이 클수록 커진다.</summary>
            public float EffectivePpu => Ppu / Mathf.Max(0.0001f, RelativeScale);
        }

        private void OnEnable()
        {
            RefreshTarget();
            TryPullGameViewSize();
        }

        private void OnFocus()
        {
            RefreshTarget();
        }

        private void OnHierarchyChange()
        {
            RefreshTarget();
            Repaint();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawTargetSection();

            if (controller == null)
            {
                EditorGUILayout.HelpBox(
                    "열려 있는 씬에서 StageVisualRootController를 찾지 못했습니다.\n" +
                    "작업할 씬(예: desktopScene_ReSize)을 열고 [대상 다시 찾기]를 누르세요.",
                    MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            SerializedObject serialized = new SerializedObject(controller);
            serialized.Update();

            Camera camera = ResolveCamera(serialized);
            StagePlacementBounds bounds = ResolveBounds(serialized);

            if (camera == null || bounds == null || !camera.orthographic)
            {
                EditorGUILayout.HelpBox(
                    "계산에 필요한 연결이 없습니다 - Stage Camera(직교 투영)와 StagePlacementBounds가 모두 필요합니다.\n" +
                    $"Camera: {(camera == null ? "없음" : camera.orthographic ? "정상" : "직교 투영이 아님")} / " +
                    $"Placement Bounds: {(bounds == null ? "없음" : "정상")}",
                    MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space(8f);
            DrawResolutionSection();

            EditorGUILayout.Space(8f);
            DrawCurrentSettingsSection(serialized, camera, bounds);

            EditorGUILayout.Space(8f);
            DrawPixelRatioSection(serialized, camera, bounds);

            EditorGUILayout.Space(8f);
            DrawScreenSizeSection(serialized, camera, bounds);

            EditorGUILayout.Space(8f);
            DrawIntegerHelperSection(serialized, camera, bounds);

            EditorGUILayout.Space(8f);
            DrawApplySection(serialized, camera, bounds);

            EditorGUILayout.EndScrollView();
        }

        // ---- 대상 ----

        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField("대상", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(controller, typeof(StageVisualRootController), true);
                }
                if (GUILayout.Button("대상 다시 찾기", GUILayout.Width(110f))) RefreshTarget();
            }

            if (controller != null)
            {
                EditorGUILayout.LabelField(" ", $"씬: {controller.gameObject.scene.name}", EditorStyles.miniLabel);
            }
        }

        private void RefreshTarget()
        {
            // 비활성 포함 - 편집 중에 꺼둔 상태일 수 있다.
            var found = Object.FindObjectsOfType<StageVisualRootController>(true);
            controller = found.Length > 0 ? found[0] : null;

            if (found.Length > 1)
            {
                Debug.LogWarning($"[Stage Layout] 열려 있는 씬에 StageVisualRootController가 {found.Length}개 있습니다 - " +
                                 $"첫 번째('{found[0].name}')를 기준으로 계산합니다.", found[0]);
            }

            RefreshPpuGroups();
        }

        /// <summary>스테이지 아래 SpriteRenderer들을 (PPU, 스테이지 기준 상대 스케일)로 묶는다.
        /// 배율 표를 그릴 때만 쓰므로 창을 열거나 계층이 바뀔 때만 갱신한다(OnGUI마다 돌리지 않는다).
        ///
        /// 상대 스케일을 함께 보는 이유: 화면상 배율에는 StageVisualRoot의 스케일뿐 아니라 <b>그 아래
        /// 각 오브젝트의 스케일도 그대로 곱해진다</b>. 마을 프롭처럼 씬에서 크기를 조정해둔 것이 있으면
        /// PPU만 보고 계산한 값은 실제와 다르다. lossyScale을 스테이지 루트의 lossyScale로 나눠
        /// 루트 자신의 기여분을 뺀 순수 상대 배율만 남긴다.</summary>
        private void RefreshPpuGroups()
        {
            sceneGroups.Clear();
            if (controller == null) return;

            float rootScale = Mathf.Abs(controller.transform.lossyScale.y);
            if (rootScale < 0.0001f) rootScale = 1f;

            var buckets = new Dictionary<(float, float), (int count, string sample)>();
            var renderers = controller.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Sprite sprite = renderers[i].sprite;
                if (sprite == null) continue;

                float relative = Mathf.Abs(renderers[i].transform.lossyScale.y) / rootScale;
                // 소수점 셋째 자리에서 묶는다 - 부동소수 오차로 같은 스케일이 여러 줄로 갈라지지 않게.
                var key = (sprite.pixelsPerUnit, Mathf.Round(relative * 1000f) / 1000f);

                if (buckets.TryGetValue(key, out var entry)) buckets[key] = (entry.count + 1, entry.sample);
                else buckets[key] = (1, renderers[i].name);
            }

            foreach (var pair in buckets)
            {
                sceneGroups.Add(new SpriteGroup
                {
                    Ppu = pair.Key.Item1,
                    RelativeScale = pair.Key.Item2,
                    Count = pair.Value.count,
                    Sample = pair.Value.sample,
                });
            }
            sceneGroups.Sort((a, b) => b.Count.CompareTo(a.Count));
        }

        /// <summary>프로젝트의 모든 Sprite 에셋을 훑어 PPU 분포를 센다. 에셋 수가 많으면 몇 초 걸릴 수
        /// 있어서 버튼을 눌렀을 때만 돌린다.</summary>
        private void ScanProjectPpu()
        {
            projectPpuGroups.Clear();
            projectScanned = true;

            var buckets = new Dictionary<float, int>();
            // "t:Sprite"는 텍스처의 서브에셋으로 들어 있는 Sprite를 안정적으로 잡지 못한다 - 텍스처를
            // 찾은 뒤 그 경로의 서브에셋에서 Sprite만 골라내는 쪽이 확실하다.
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });
            for (int i = 0; i < guids.Length; i++)
            {
                if (i % 16 == 0 && EditorUtility.DisplayCancelableProgressBar("Stage Layout",
                        $"Sprite 에셋 스캔 중... ({i + 1}/{guids.Length})", (float)i / guids.Length))
                {
                    break;
                }

                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is not Sprite sprite) continue;

                    float ppu = sprite.pixelsPerUnit;
                    buckets[ppu] = buckets.TryGetValue(ppu, out int c) ? c + 1 : 1;
                }
            }
            EditorUtility.ClearProgressBar();

            foreach (var pair in buckets) projectPpuGroups.Add((pair.Key, pair.Value));
            projectPpuGroups.Sort((a, b) => b.count.CompareTo(a.count));
        }

        // ---- 기준 해상도 ----

        private void DrawResolutionSection()
        {
            EditorGUILayout.LabelField("기준 해상도", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                referenceWidth = Mathf.Max(1, EditorGUILayout.IntField("Width", referenceWidth));
                referenceHeight = Mathf.Max(1, EditorGUILayout.IntField("Height", referenceHeight));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Game 뷰에서 가져오기")) TryPullGameViewSize();
                if (GUILayout.Button("1920x1080")) SetReference(1920, 1080);
                if (GUILayout.Button("2560x1440")) SetReference(2560, 1440);
                if (GUILayout.Button("3840x2160")) SetReference(3840, 2160);
            }

            EditorGUILayout.HelpBox(
                "실제 빌드는 모니터 Work Area(작업 표시줄을 뺀 영역)를 씁니다 - 1080p 모니터라면 보통 1920x1040 근처입니다.\n" +
                "다만 아트 픽셀 배율은 해상도와 무관하게 일정하므로, 이 값이 정확하지 않아도 크기 작업에는 오차가 없습니다. " +
                "영향을 받는 것은 위치(여백)뿐입니다.",
                MessageType.None);
        }

        private void SetReference(int width, int height)
        {
            referenceWidth = width;
            referenceHeight = height;
        }

        /// <summary>Game 뷰 해상도를 읽어온다. 공개 API가 없어 내부 메서드를 리플렉션으로 부르고,
        /// 실패하면 조용히 현재 값을 유지한다(수동 입력과 프리셋이 있으므로 기능이 막히지 않는다).</summary>
        private void TryPullGameViewSize()
        {
            try
            {
                MethodInfo method = typeof(Handles).GetMethod("GetMainGameViewSize",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (method == null) return;

                if (method.Invoke(null, null) is Vector2 size && size.x >= 1f && size.y >= 1f)
                {
                    SetReference(Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y));
                }
            }
            catch
            {
                // 에디터 내부 API라 버전에 따라 없을 수 있다 - 수동 입력으로 대체 가능하므로 조용히 넘어간다.
            }
        }

        // ---- 현재 설정 ----

        private void DrawCurrentSettingsSection(SerializedObject serialized, Camera camera, StagePlacementBounds bounds)
        {
            EditorGUILayout.LabelField("현재 설정", EditorStyles.boldLabel);

            float orthoSize = camera.orthographicSize;
            float baseVisualScale = serialized.FindProperty("baseVisualScale").floatValue;

            EditorGUILayout.LabelField("Camera Orthographic Size", orthoSize.ToString("0.###"));
            EditorGUILayout.LabelField("Base Visual Scale", baseVisualScale.ToString("0.###"));
            EditorGUILayout.LabelField("Placement Bounds", $"{bounds.Width:0.#} x {bounds.Height:0.#} px (safety {bounds.SafetyMarginPixels:0.#})");

            float stageScale = ComputeStageScale(bounds, baseVisualScale, 1f);
            EditorGUILayout.LabelField("적용될 StageVisualRoot Scale", stageScale.ToString("0.####"));

            Rect box = ComputeStageScreenRect(serialized, bounds, 1f);
            EditorGUILayout.LabelField("스테이지 박스(화면 px)", $"x {box.x:0} / y {box.y:0} / {box.width:0} x {box.height:0}");

            EditorGUILayout.HelpBox(
                "Placement Bounds는 드래그 한계와 클릭 영역(footprint)이면서, 동시에 위 Scale 계산의 분자이기도 합니다 - " +
                "footprint를 키우면 캐릭터도 같이 커지므로 Base Visual Scale로 되맞춰야 합니다.",
                MessageType.None);
        }

        // ---- 아트 픽셀 배율 ----

        private void DrawPixelRatioSection(SerializedObject serialized, Camera camera, StagePlacementBounds bounds)
        {
            EditorGUILayout.LabelField("아트 1픽셀 → 화면 픽셀", EditorStyles.boldLabel);

            EditorGUILayout.LabelField(" ",
                "지금 열려 있는 씬에 실제로 놓인 SpriteRenderer만 셉니다 - 캐릭터/몬스터 스프라이트는 " +
                "런타임에 프로필에서 갈아끼우므로 여기에는 액터 몇 개만 잡힙니다.", EditorStyles.wordWrappedMiniLabel);

            if (sceneGroups.Count == 0)
            {
                EditorGUILayout.HelpBox("스테이지 아래에서 Sprite가 지정된 SpriteRenderer를 찾지 못했습니다.", MessageType.Info);
            }
            else
            {
                float orthoSize = camera.orthographicSize;
                float baseVisualScale = serialized.FindProperty("baseVisualScale").floatValue;

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("PPU / 스케일 (개수)", GUILayout.Width(190f));
                    for (int i = 0; i < UserScalePresets.Length; i++)
                    {
                        EditorGUILayout.LabelField($"{UserScalePresets[i] * 100f:0}%", EditorStyles.miniBoldLabel, GUILayout.Width(78f));
                    }
                }

                for (int g = 0; g < sceneGroups.Count; g++)
                {
                    SpriteGroup group = sceneGroups[g];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string scaleLabel = Mathf.Approximately(group.RelativeScale, 1f) ? "" : $" x{group.RelativeScale:0.###}";
                        EditorGUILayout.LabelField(
                            new GUIContent($"{group.Ppu:0.#}{scaleLabel}  ({group.Count}개)", $"예: {group.Sample}"),
                            GUILayout.Width(190f));

                        for (int i = 0; i < UserScalePresets.Length; i++)
                        {
                            // 오브젝트 스케일이 곱해진 "실효 PPU"로 계산해야 화면에서 보이는 값과 맞는다.
                            float ratio = ComputePixelRatio(bounds, baseVisualScale, UserScalePresets[i], orthoSize, group.EffectivePpu);
                            bool clean = IsCleanRatio(ratio);
                            var style = new GUIStyle(EditorStyles.label)
                            {
                                normal = { textColor = clean ? new Color(0.35f, 0.75f, 0.35f) : new Color(0.85f, 0.55f, 0.2f) }
                            };
                            EditorGUILayout.LabelField(DescribeRatio(ratio), style, GUILayout.Width(78f));
                        }
                    }
                }
            }

            EditorGUILayout.HelpBox(
                "정수(✓)가 아니면 픽셀 아트가 비정수 배율로 그려져 도트가 뭉개집니다. 1보다 작으면 축소라 " +
                "원본 디테일이 아예 사라집니다.\n" +
                "PPU가 서로 다른 아트는 배율이 PPU에 반비례하므로, 값 하나로 양쪽을 동시에 정수로 맞출 수 " +
                "없는 경우가 많습니다 - 어느 쪽을 기준으로 삼을지 정해야 합니다.\n" +
                "이 값은 해상도와 무관하게 일정합니다(workAreaHeight가 약분됨).",
                MessageType.None);

            EditorGUILayout.Space(4f);
            DrawProjectScanSection();
        }

        /// <summary>씬 스캔만으로는 "이 프로젝트가 어떤 PPU들을 쓰는가"를 알 수 없어서(캐릭터는 런타임에
        /// 교체됨), 에셋 전체를 훑는 경로를 따로 둔다.</summary>
        private void DrawProjectScanSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("프로젝트 전체 Sprite PPU 분포", EditorStyles.miniBoldLabel);
                if (GUILayout.Button(projectScanned ? "다시 스캔" : "스캔", GUILayout.Width(80f))) ScanProjectPpu();
            }

            if (!projectScanned)
            {
                EditorGUILayout.LabelField(" ", "씬에 없는 캐릭터/몬스터까지 포함해 확인하려면 스캔하세요.", EditorStyles.miniLabel);
                return;
            }

            if (projectPpuGroups.Count == 0)
            {
                EditorGUILayout.LabelField(" ", "Sprite 에셋을 찾지 못했습니다.", EditorStyles.miniLabel);
                return;
            }

            for (int i = 0; i < projectPpuGroups.Count; i++)
            {
                (float ppu, int count) = projectPpuGroups[i];
                EditorGUILayout.LabelField($"    PPU {ppu:0.#}", $"{count}개");
            }
        }

        // ---- 화면 픽셀 기준 크기 맞추기 ----

        /// <summary>"몇 배로 그릴까"가 아니라 <b>"화면에서 몇 픽셀로 보일까"</b>로 크기를 정하는 구역.
        /// 배율은 결과일 뿐이고 실제로 판단하는 기준은 화면 크기이므로, 이쪽이 훨씬 직관적이다.
        ///
        /// 스프라이트 캔버스에는 보통 투명 여백이 크게 붙어 있어서(이 프로젝트 캐릭터는 512x512 중 실제
        /// 내용이 195x258뿐이다) 캔버스 크기로 계산하면 실제 보이는 크기와 두 배 넘게 어긋난다. 그래서
        /// 알파 바운딩 박스를 실측하는 버튼을 따로 둔다.</summary>
        private void DrawScreenSizeSection(SerializedObject serialized, Camera camera, StagePlacementBounds bounds)
        {
            EditorGUILayout.LabelField("크기 맞추기 (화면 px 기준)", EditorStyles.boldLabel);

            if (referenceSprite == null) referenceSprite = FindReferenceSprite();
            referenceSprite = (Sprite)EditorGUILayout.ObjectField(
                new GUIContent("기준 스프라이트", "이 스프라이트가 화면에서 몇 px로 보일지를 기준으로 계산합니다."),
                referenceSprite, typeof(Sprite), false);

            if (referenceSprite == null)
            {
                EditorGUILayout.HelpBox("기준으로 삼을 스프라이트를 지정하세요.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                string measured = measuredSprite == referenceSprite && measuredContentHeight > 0
                    ? $"{measuredContentWidth} x {measuredContentHeight} px  (캔버스 {measuredCanvasWidth} x {measuredCanvasHeight})"
                    : "아직 측정하지 않음 - 캔버스 크기로 계산 중";
                EditorGUILayout.LabelField("실제 내용 크기", measured);
                if (GUILayout.Button("측정", GUILayout.Width(60f))) MeasureReferenceContent();
            }

            if (measuredSprite == referenceSprite && measuredPixelDensity > 0)
            {
                string density = measuredPixelDensity == 1
                    ? "1x1 (확대 흔적 없음 - 이 해상도로 직접 그린 아트)"
                    : $"{measuredPixelDensity}x{measuredPixelDensity}  →  1/{measuredPixelDensity}로 줄여도 무손실 " +
                      $"(실질 해상도 {measuredContentWidth / measuredPixelDensity} x {measuredContentHeight / measuredPixelDensity})";
                EditorGUILayout.LabelField("픽셀 밀도", density);
            }

            EditorGUILayout.LabelField(" ",
                "내용 크기는 캐릭터마다, 애니메이션 프레임마다 다릅니다(캔버스는 팔다리가 뻗는 최대 범위에 맞춰 잡음) - " +
                "맞춰야 할 기준이 아니라 \"이 스프라이트가 지금 몇 px로 보이나\"를 가늠하는 참고값입니다. " +
                "실제로 고정해야 하는 값은 아래 배율입니다.",
                EditorStyles.wordWrappedMiniLabel);

            float contentHeight = ResolveReferenceHeight();
            float ppu = Mathf.Max(0.01f, referenceSprite.pixelsPerUnit);
            float orthoSize = camera.orthographicSize;
            float baseVisualScale = serialized.FindProperty("baseVisualScale").floatValue;

            float currentRatio = ComputePixelRatio(bounds, baseVisualScale, 1f, orthoSize, ppu);
            EditorGUILayout.LabelField("현재 배율 (100%)", $"{DescribeRatio(currentRatio)}   → 이 스프라이트는 약 {contentHeight * currentRatio:0} px");

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("깨끗한 배율로 맞추기", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(" ", "도트가 뭉개지지 않는 배율만 추립니다. px는 기준 스프라이트 기준 참고값입니다.",
                EditorStyles.wordWrappedMiniLabel);

            foreach (float ratio in CleanRatioPresets)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(DescribeRatio(ratio), GUILayout.Width(120f));
                    EditorGUILayout.LabelField($"{contentHeight * ratio:0} px", GUILayout.Width(80f));

                    float required = ratio * 2f * orthoSize * ppu / Mathf.Max(0.01f, bounds.Height);
                    if (GUILayout.Button($"적용 (Base {required:0.####})"))
                    {
                        ApplyBaseVisualScale(serialized, required);
                    }
                }
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("(참고) 화면 높이로 역산", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(" ",
                "기준 스프라이트 하나에만 해당하는 계산입니다 - 다른 캐릭터는 내용 크기가 달라 결과가 달라집니다.",
                EditorStyles.wordWrappedMiniLabel);
            targetScreenHeight = Mathf.Max(1f, EditorGUILayout.FloatField(
                new GUIContent("목표 화면 높이(px)", "이 높이에 맞추려면 배율이 얼마여야 하는지 역산합니다."), targetScreenHeight));

            float neededRatio = targetScreenHeight / Mathf.Max(1f, contentHeight);
            float neededBase = neededRatio * 2f * orthoSize * ppu / Mathf.Max(0.01f, bounds.Height);
            EditorGUILayout.LabelField("필요한 배율 / Base Visual Scale", $"{DescribeRatio(neededRatio)}  /  {neededBase:0.####}");

            if (!IsCleanRatio(neededRatio))
            {
                EditorGUILayout.HelpBox(
                    "이 높이는 깨끗한 배율이 아닙니다 - 도트가 뭉개집니다. 위 목록에서 가까운 값을 고르는 쪽을 권합니다.",
                    MessageType.Warning);
            }

            if (GUILayout.Button($"이 높이로 적용 (Base {neededBase:0.####})"))
            {
                ApplyBaseVisualScale(serialized, neededBase);
            }

            if (currentRatio < 0.999f)
            {
                EditorGUILayout.HelpBox(
                    "1배 미만(축소)으로 그리고 있습니다. 축소가 깨끗하려면 해당 텍스처의 밉맵이 켜져 있어야 합니다.\n" +
                    "Project 창에서 그 png를 선택 → Inspector의 Advanced → Generate Mip Maps 체크 → Apply.\n" +
                    "반대로 1배 이상으로만 쓸 거라면 밉맵은 꺼두는 것이 맞습니다.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(2f);
            if (GUILayout.Button("기준 스프라이트를 Project 창에서 선택"))
            {
                Selection.activeObject = referenceSprite;
                EditorGUIUtility.PingObject(referenceSprite);
            }

            EditorGUILayout.Space(8f);
            DrawZoomStepSection(currentRatio);
        }

        /// <summary>지금 100% 배율을 기준으로, 화면 크기 토글의 각 단계가 깨끗하게 떨어지는지 보여준다.
        /// SizeToggleButton의 50/100/150은 임의로 정한 값이라 바꿀 수 있는데, 단계마다 배율이 곱해지므로
        /// 어떤 단계를 고르느냐에 따라 도트가 깨지기도 하고 안 깨지기도 한다.</summary>
        private void DrawZoomStepSection(float baseRatio)
        {
            EditorGUILayout.LabelField("화면 크기 토글 단계 추천", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(" ",
                "SizeToggleButton(tgl_size)의 단계 후보입니다. ✓인 단계만 골라 쓰면 어느 배율에서도 도트가 뭉개지지 않습니다.",
                EditorStyles.wordWrappedMiniLabel);

            const int columns = 3;
            for (int i = 0; i < ZoomStepCandidates.Length; i += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int c = 0; c < columns && i + c < ZoomStepCandidates.Length; c++)
                    {
                        float step = ZoomStepCandidates[i + c];
                        float ratio = baseRatio * step;
                        bool clean = IsCleanRatio(ratio);
                        var style = new GUIStyle(EditorStyles.label)
                        {
                            normal = { textColor = clean ? new Color(0.35f, 0.75f, 0.35f) : new Color(0.6f, 0.6f, 0.6f) }
                        };
                        EditorGUILayout.LabelField($"{step * 100f:0}% → {DescribeRatio(ratio)}", style, GUILayout.Width(180f));
                    }
                }
            }

            EditorGUILayout.HelpBox(
                "아트를 1:1(배율 1)로 그리도록 다시 만들면 100/200/300처럼 정수 배만 쓰는 것이 가장 깔끔합니다 - " +
                "밉맵도 필요 없고 모든 단계가 완벽합니다. 1배 미만 단계(축소)를 넣으려면 그 텍스처의 밉맵을 켜야 합니다.",
                MessageType.None);
        }

        private void ApplyBaseVisualScale(SerializedObject serialized, float value)
        {
            SerializedProperty baseProp = serialized.FindProperty("baseVisualScale");
            baseProp.floatValue = value;
            serialized.ApplyModifiedProperties();
            MarkTargetSceneDirty();
        }

        /// <summary>측정했으면 알파 바운딩 박스 높이, 아니면 스프라이트 rect 높이(투명 여백 포함).</summary>
        private float ResolveReferenceHeight()
        {
            if (measuredSprite == referenceSprite && measuredContentHeight > 0) return measuredContentHeight;
            return referenceSprite != null ? referenceSprite.rect.height : 1f;
        }

        private Sprite FindReferenceSprite()
        {
            if (controller == null) return null;

            // 플레이어가 기준으로 가장 자연스럽다 - 화면에 항상 있고 크기 판단의 기준이 된다.
            var renderers = controller.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].sprite != null && renderers[i].name.IndexOf("player", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return renderers[i].sprite;
                }
            }
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].sprite != null) return renderers[i].sprite;
            }
            return null;
        }

        /// <summary>스프라이트 원본 png를 직접 디코드해서 알파가 있는 영역의 크기를 잰다.
        ///
        /// Texture2D.GetPixels는 Read/Write가 꺼진 텍스처에서 실패하고, 임포트 설정을 건드리면 에셋이
        /// 바뀌어버린다. 그래서 파일 바이트를 읽어 임시 Texture2D로 디코드한 뒤 바로 버린다 - 에셋에는
        /// 아무 영향이 없고, 버튼을 눌렀을 때만 도는 경로라 비용도 문제되지 않는다.</summary>
        private void MeasureReferenceContent()
        {
            measuredSprite = null;
            measuredContentWidth = measuredContentHeight = 0;
            measuredPixelDensity = 0;

            if (referenceSprite == null) return;

            string path = AssetDatabase.GetAssetPath(referenceSprite);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning($"[Stage Layout] 스프라이트 원본 파일을 찾지 못해 측정할 수 없습니다: {path}");
                return;
            }

            var temp = new Texture2D(2, 2);
            try
            {
                if (!temp.LoadImage(File.ReadAllBytes(path)))
                {
                    Debug.LogWarning($"[Stage Layout] 이미지를 디코드하지 못했습니다: {path}");
                    return;
                }

                measuredCanvasWidth = temp.width;
                measuredCanvasHeight = temp.height;

                // 아틀라스가 아니면 rect가 곧 전체 텍스처다. 아틀라스여도 해당 rect 안만 훑는다.
                Rect rect = referenceSprite.rect;
                int x0 = Mathf.Clamp(Mathf.FloorToInt(rect.x), 0, temp.width - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(rect.y), 0, temp.height - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), x0 + 1, temp.width);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), y0 + 1, temp.height);

                Color32[] pixels = temp.GetPixels32();
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                for (int y = y0; y < y1; y++)
                {
                    int row = y * temp.width;
                    for (int x = x0; x < x1; x++)
                    {
                        if (pixels[row + x].a <= 8) continue;   // 거의 투명한 픽셀은 내용으로 치지 않는다.
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }

                if (minX > maxX)
                {
                    Debug.LogWarning($"[Stage Layout] '{referenceSprite.name}'에 불투명 픽셀이 없습니다.");
                    return;
                }

                measuredContentWidth = maxX - minX + 1;
                measuredContentHeight = maxY - minY + 1;
                measuredPixelDensity = DetectPixelDensity(pixels, temp.width, minX, minY, maxX, maxY);
                measuredSprite = referenceSprite;
            }
            finally
            {
                DestroyImmediate(temp);
            }
        }

        /// <summary>
        /// 원본 아트의 "논리 픽셀" 크기를 찾는다 - 저해상도로 그린 뒤 N배로 확대해서 저장한 아트는
        /// N x N 픽셀이 전부 같은 색인 격자 구조를 갖는다. 그 N을 찾아내면 <b>1/N로 줄여도 정보가 하나도
        /// 손실되지 않는다</b>는 뜻이 되므로, 아트 해상도를 다시 설계할 때 "어디까지 줄여도 되는가"의
        /// 상한을 알 수 있다.
        ///
        /// 격자가 캔버스 원점에 딱 맞아떨어지지 않을 수 있어서(예: 170px을 3배 확대하면 510px이고
        /// 512 캔버스에 1px 밀려 들어간다) 오프셋도 함께 훑는다. 큰 N부터 확인해 가장 큰 값을 답으로
        /// 삼고, 하나도 맞지 않으면 1(확대 흔적 없음)이다.
        /// </summary>
        private static int DetectPixelDensity(Color32[] pixels, int textureWidth, int minX, int minY, int maxX, int maxY)
        {
            const float requiredMatch = 0.999f;

            for (int block = 8; block >= 2; block--)
            {
                for (int offsetY = 0; offsetY < block; offsetY++)
                {
                    for (int offsetX = 0; offsetX < block; offsetX++)
                    {
                        if (BlockMatches(pixels, textureWidth, minX, minY, maxX, maxY, block, offsetX, offsetY, requiredMatch))
                        {
                            return block;
                        }
                    }
                }
            }
            return 1;
        }

        /// <summary>주어진 블록 크기/오프셋으로 나눴을 때 각 블록 안의 픽셀이 전부 같은 색인지 본다.
        /// 어긋나는 픽셀 비율이 허용치를 넘으면 곧바로 실패로 끊는다(전수 검사를 끝까지 돌지 않는다).</summary>
        private static bool BlockMatches(Color32[] pixels, int textureWidth, int minX, int minY, int maxX, int maxY,
            int block, int offsetX, int offsetY, float requiredMatch)
        {
            long total = (long)(maxX - minX + 1) * (maxY - minY + 1);
            long allowedMismatch = (long)(total * (1f - requiredMatch));
            long mismatch = 0;

            for (int y = minY; y <= maxY; y++)
            {
                int blockY = BlockOrigin(y, minY, offsetY, block);
                int rowBase = y * textureWidth;
                int blockRowBase = blockY * textureWidth;

                for (int x = minX; x <= maxX; x++)
                {
                    int blockX = BlockOrigin(x, minX, offsetX, block);

                    Color32 a = pixels[rowBase + x];
                    Color32 b = pixels[blockRowBase + blockX];
                    if (a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a) continue;

                    if (++mismatch > allowedMismatch) return false;
                }
            }
            return true;
        }

        /// <summary>coord가 속한 블록의 기준 좌표. 격자는 (min + offset)에서 시작하고, 그보다 앞쪽에
        /// 남는 부분 블록은 min을 기준으로 삼는다.</summary>
        private static int BlockOrigin(int coord, int min, int offset, int block)
        {
            int shifted = coord - (min + offset);
            int origin = shifted >= 0
                ? min + offset + shifted / block * block
                : min + offset - block;

            return origin < min ? min : origin;
        }

        // ---- 정수 배율 도우미 ----

        private void DrawIntegerHelperSection(SerializedObject serialized, Camera camera, StagePlacementBounds bounds)
        {
            EditorGUILayout.LabelField("정수 배율 맞추기", EditorStyles.boldLabel);

            targetRatio = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                new GUIContent("목표 배율(100% 기준)", "아트 1픽셀이 화면에서 차지할 픽셀 수. 2를 권장합니다."), targetRatio));

            // 기본값은 씬에서 가장 많이 쓰인 묶음의 "실효 PPU"(오브젝트 스케일까지 반영된 값)다.
            if (referencePpu <= 0f) referencePpu = sceneGroups.Count > 0 ? sceneGroups[0].EffectivePpu : 100f;
            referencePpu = Mathf.Max(0.01f, EditorGUILayout.FloatField(
                new GUIContent("기준 PPU", "이 PPU를 가진 아트를 기준으로 계산합니다. 기본값은 스프라이트가 가장 많은 PPU입니다."), referencePpu));

            float orthoSize = camera.orthographicSize;
            // ratio = H * b / (2 * ortho * ppu)  ->  b = ratio * 2 * ortho * ppu / H
            float requiredBase = targetRatio * 2f * orthoSize * referencePpu / Mathf.Max(0.01f, bounds.Height);

            EditorGUILayout.LabelField("필요한 Base Visual Scale", requiredBase.ToString("0.####"));

            SerializedProperty baseProp = serialized.FindProperty("baseVisualScale");
            using (new EditorGUI.DisabledScope(Mathf.Approximately(baseProp.floatValue, requiredBase)))
            {
                if (GUILayout.Button($"Base Visual Scale을 {requiredBase:0.####}로 적용"))
                {
                    baseProp.floatValue = requiredBase;
                    serialized.ApplyModifiedProperties();   // Undo는 SerializedObject가 자동 등록한다.
                    MarkTargetSceneDirty();
                }
            }

            EditorGUILayout.HelpBox(
                "Camera Orthographic Size는 이 계산의 분모일 뿐이라 단독으로는 의미가 없습니다 - " +
                "씬 뷰에서 보기 편한 값으로 두고, 최종 크기는 Base Visual Scale로 맞추세요.",
                MessageType.None);
        }

        // ---- 적용 ----

        private void DrawApplySection(SerializedObject serialized, Camera camera, StagePlacementBounds bounds)
        {
            EditorGUILayout.LabelField("런타임 배치 적용", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "StageVisualRoot의 position/localScale을 런타임과 같은 공식으로 지금 씬에 적용합니다. " +
                "Undo로 되돌릴 수 있습니다.\n\n" +
                "여백은 Inspector의 Default Right/Bottom Margin Fraction을 씁니다 - 실제 실행에서는 " +
                "저장된 배치(windowplacement.json)가 있으면 그쪽이 우선하므로, 배치를 새로 잡는 중이라면 " +
                "그 파일을 지우고 확인하세요.",
                MessageType.None);

            // Play 모드에서는 씬을 더티로 표시할 수 없고(예외), 애초에 런타임 컨트롤러가 스스로
            // 배치하므로 이 버튼이 할 일도 없다.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUILayout.HelpBox(
                    "Play 모드에서는 적용할 수 없습니다 - 재생 중에는 StageVisualRootController가 직접 배치합니다. " +
                    "위의 계산 결과는 그대로 참고하실 수 있습니다.",
                    MessageType.Info);
                return;
            }

            if (GUILayout.Button("런타임 배치를 씬에 적용", GUILayout.Height(28f)))
            {
                ApplyRuntimePlacement(serialized, camera, bounds);
            }

            if (GUILayout.Button("초기화 (Scale 1 / 위치 0)"))
            {
                Transform t = controller.transform;
                Undo.RecordObject(t, "Reset Stage Layout");
                t.localScale = new Vector3(1f, 1f, t.localScale.z);
                t.position = new Vector3(0f, 0f, t.position.z);
                MarkTargetSceneDirty();
            }
        }

        /// <summary>씬 더티 표시는 편집 모드에서만 허용된다 - Play 중에 부르면 예외가 나고, 그 예외가
        /// OnGUI 한가운데서 터지면 GUILayout Begin/End 짝이 깨져 "Invalid GUILayout state"까지 함께 뜬다.</summary>
        private void MarkTargetSceneDirty()
        {
            if (controller == null || EditorApplication.isPlayingOrWillChangePlaymode) return;

            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        }

        /// <summary>StageVisualRootController.ApplyPlacement와 <b>같은 식</b>을 그대로 옮긴 것이다 -
        /// 한쪽만 바뀌면 에디터와 런타임이 어긋나므로, 그쪽 계산이 바뀌면 여기도 함께 고쳐야 한다.</summary>
        private void ApplyRuntimePlacement(SerializedObject serialized, Camera camera, StagePlacementBounds bounds)
        {
            Transform t = controller.transform;
            Undo.RecordObject(t, "Apply Runtime Stage Placement");

            float baseVisualScale = serialized.FindProperty("baseVisualScale").floatValue;
            float scaleFactor = ComputeStageScale(bounds, baseVisualScale, 1f);
            t.localScale = new Vector3(scaleFactor, scaleFactor, t.localScale.z);

            Rect box = ComputeStageScreenRect(serialized, bounds, 1f);
            float targetScreenX = box.x + box.width / 2f;
            float targetScreenY = box.y + box.height / 2f;

            float worldUnitsPerPixel = 2f * camera.orthographicSize / referenceHeight;
            Vector3 cameraPosition = camera.transform.position;
            float worldX = cameraPosition.x + (targetScreenX - referenceWidth / 2f) * worldUnitsPerPixel;
            float worldY = cameraPosition.y + (targetScreenY - referenceHeight / 2f) * worldUnitsPerPixel;

            t.position = new Vector3(worldX, worldY, t.position.z);

            MarkTargetSceneDirty();
            Debug.Log($"[Stage Layout] 적용 완료 - scale {scaleFactor:0.####}, position ({worldX:0.###}, {worldY:0.###}) " +
                      $"@ {referenceWidth}x{referenceHeight}", controller);
        }

        // ---- 계산 (런타임 ApplyPlacement와 동일한 식) ----

        private float ComputeStageScale(StagePlacementBounds bounds, float baseVisualScale, float userScale)
        {
            return Mathf.Max(0.01f, bounds.Height * baseVisualScale * userScale / referenceHeight);
        }

        private Rect ComputeStageScreenRect(SerializedObject serialized, StagePlacementBounds bounds, float userScale)
        {
            float stageWidthPixels = bounds.Width * userScale;
            float stageHeightPixels = bounds.Height * userScale;
            float safetyMargin = bounds.SafetyMarginPixels;

            float rightFraction = serialized.FindProperty("defaultRightMarginFraction").floatValue;
            float bottomFraction = serialized.FindProperty("defaultBottomMarginFraction").floatValue;

            float maxRightMarginPixels = Mathf.Max(safetyMargin, referenceWidth - stageWidthPixels - safetyMargin);
            float maxBottomMarginPixels = Mathf.Max(safetyMargin, referenceHeight - stageHeightPixels - safetyMargin);
            float rightMarginPixels = Mathf.Clamp(rightFraction * referenceWidth, safetyMargin, maxRightMarginPixels);
            float bottomMarginPixels = Mathf.Clamp(bottomFraction * referenceHeight, safetyMargin, maxBottomMarginPixels);

            float targetScreenX = referenceWidth - rightMarginPixels - stageWidthPixels / 2f;
            float targetScreenY = bottomMarginPixels + stageHeightPixels / 2f;

            return new Rect(
                targetScreenX - stageWidthPixels / 2f,
                targetScreenY - stageHeightPixels / 2f,
                stageWidthPixels,
                stageHeightPixels);
        }

        /// <summary>아트 1픽셀이 화면에서 차지하는 픽셀 수. workAreaHeight가 약분되므로 해상도가
        /// 들어가지 않는다 - 이 값이 해상도와 무관하게 일정하다는 것이 식에서 그대로 드러난다.</summary>
        private static float ComputePixelRatio(StagePlacementBounds bounds, float baseVisualScale, float userScale,
            float orthoSize, float ppu)
        {
            return bounds.Height * baseVisualScale * userScale / (2f * Mathf.Max(0.0001f, orthoSize) * Mathf.Max(0.0001f, ppu));
        }

        /// <summary>도트가 뭉개지지 않는 배율인지. 정수 확대(1,2,3...)뿐 아니라 <b>정수 분의 1 축소</b>
        /// (1/2, 1/3, 1/4)도 깨끗하다 - 출력 픽셀 하나가 입력 2x2/3x3의 평균이 되기 때문이다(단, 축소는
        /// 밉맵이 켜져 있어야 실제로 그 평균이 쓰인다). 그 사이의 애매한 값(0.64, 1.6 등)만 문제다.</summary>
        private static bool IsCleanRatio(float value)
        {
            if (value <= 0f) return false;
            if (value >= 0.999f) return Mathf.Abs(value - Mathf.Round(value)) < 0.001f;

            float inverse = 1f / value;
            return Mathf.Abs(inverse - Mathf.Round(inverse)) < 0.002f;
        }

        private static string DescribeRatio(float value)
        {
            if (!IsCleanRatio(value)) return $"{value:0.###}";
            if (value >= 0.999f) return $"{value:0.###} ✓";

            return $"{value:0.###} ✓ (1/{Mathf.RoundToInt(1f / value)})";
        }

        private static Camera ResolveCamera(SerializedObject serialized)
        {
            var assigned = serialized.FindProperty("stageCamera").objectReferenceValue as Camera;
            return assigned != null ? assigned : Camera.main;
        }

        private static StagePlacementBounds ResolveBounds(SerializedObject serialized)
        {
            var assigned = serialized.FindProperty("placementBounds").objectReferenceValue as StagePlacementBounds;
            if (assigned != null) return assigned;

            var target = serialized.targetObject as StageVisualRootController;
            return target != null ? target.GetComponentInChildren<StagePlacementBounds>(true) : null;
        }
    }
}
