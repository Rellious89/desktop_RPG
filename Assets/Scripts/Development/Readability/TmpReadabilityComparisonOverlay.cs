#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using DesktopWindow;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DevelopmentTools
{
    /// <summary>
    /// Development-only runtime lab for comparing small Mulmaru TMP samples on the real
    /// transparent desktop window. It never assigns a material back to an existing scene
    /// text object and is intentionally bootstrapped without scene/prefab wiring.
    /// </summary>
    public sealed class TmpReadabilityComparisonOverlay : MonoBehaviour
    {
        private const string RootName = "__TmpReadabilityComparisonOverlay";
        private const int OverlaySortingOrder = 32760;
        private const string PreferredFontName = "Mulmaru SDF";
        private const string PreferredMaterialName = "Mulmaru SDF Outline B";

        [Serializable]
        public sealed class CandidateConfig
        {
            [Min(0f)] public float strongerOutlineWidth = 0.28f;
            [Min(0f)] public float thinOutlineWidth = 0.12f;
            [Min(0f)] public float hardUnderlayDilate = 0f;
            public float hardUnderlayOffsetX = 0.5f;
            public float hardUnderlayOffsetY = -0.5f;
            public Color hardUnderlayColor = new Color(0f, 0f, 0f, 0.9f);
        }

        [Header("Development-only comparison tuning")]
        [SerializeField] private CandidateConfig candidateConfig = new CandidateConfig();

        private readonly List<Material> runtimeMaterials = new List<Material>();
        private Canvas canvas;
        private FontAssetSource source;
        private bool isVisible;

        public static bool IsBuildEnabled => true;
        public CandidateConfig Config => candidateConfig;

        /// <summary>Pure clamp used by the candidate builder and its focused EditMode test.</summary>
        public static float ClampOutlineWidth(float value) => Mathf.Clamp(value, 0f, 1f);

        public static int[] ComparisonFontSizes => new[] { 8, 10, 12 };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<TmpReadabilityComparisonOverlay>() != null) return;

            GameObject root = new GameObject(RootName);
            DontDestroyOnLoad(root);
            root.AddComponent<TmpReadabilityComparisonOverlay>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            GlobalKeyboardHook.RegisterExcludedKey(KeyCode.F8);
            BuildCanvas();
            SetVisible(false);
        }

        private void Update()
        {
            bool hookPressed = GlobalKeyboardHook.WasExcludedKeyDownThisFrame(KeyCode.F8);
#if UNITY_EDITOR
            // The editor path is still routed through GlobalKeyboardHook when the scene contains it;
            // this fallback keeps the lab usable in a minimal test scene with no hook component.
            bool editorPressed = Input.GetKeyDown(KeyCode.F8);
#else
            bool editorPressed = false;
#endif
            // In the normal Editor scene both paths can report the same physical key in one frame.
            // Consume it once so a single F8 never toggles on and immediately back off.
            if (hookPressed || (!hookPressed && editorPressed)) SetVisible(!isVisible);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < runtimeMaterials.Count; i++)
            {
                if (runtimeMaterials[i] == null) continue;
#if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(runtimeMaterials[i]);
                else Destroy(runtimeMaterials[i]);
#else
                Destroy(runtimeMaterials[i]);
#endif
            }
            runtimeMaterials.Clear();
        }

        private void BuildCanvas()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = OverlaySortingOrder;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform root = gameObject.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            BuildView(root);
        }

        private void BuildView(RectTransform root)
        {
            // Keep the main sample area transparent so the real desktop wallpaper remains visible.
            // Dark backplates are added only for labels that need contrast.
            Image panel = CreateImage(root, "ComparisonPanel", new Color(0f, 0f, 0f, 0f));
            RectTransform panelRect = panel.rectTransform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(1160f, 650f);
            panelRect.anchoredPosition = Vector2.zero;

            Image titleBackplate = CreateImage(panelRect, "TitleBackplate", new Color(0f, 0f, 0f, 0.78f));
            RectTransform titleBackplateRect = titleBackplate.rectTransform;
            titleBackplateRect.anchorMin = new Vector2(0.5f, 1f);
            titleBackplateRect.anchorMax = new Vector2(0.5f, 1f);
            titleBackplateRect.sizeDelta = new Vector2(760f, 48f);
            titleBackplateRect.anchoredPosition = new Vector2(-180f, -28f);

            CreateLabel(panelRect, "Title", "TMP READABILITY LAB  |  F8: SHOW / HIDE", 18,
                Color.white, new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(1100f, 32f),
                TextAlignmentOptions.Center, null, null);
            CreateLabel(panelRect, "Subtitle", "Mulmaru SDF  |  actual transparent window comparison  |  no game material changes",
                10, new Color(0.72f, 0.9f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -55f),
                new Vector2(1100f, 24f), TextAlignmentOptions.Center, null, null);

            RectTransform matrix = CreateRect(panelRect, "Matrix", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(830f, 520f), new Vector2(-120f, -10f));
            CreateLabel(matrix, "Header", "CANDIDATE / PARAMETERS                       8 px                 10 px                 12 px",
                10, new Color(0.55f, 0.85f, 1f), new Vector2(0f, 1f), new Vector2(12f, -2f),
                new Vector2(810f, 26f), TextAlignmentOptions.Left, null, null);

            string[] names =
            {
                "1  CURRENT BASELINE",
                "2  STRONGER OUTLINE",
                "3  THIN OUTLINE + HARD UNDERLAY",
                "4  CURRENT OUTLINE + HARD UNDERLAY"
            };
            Material[] materials = BuildMaterials();
            string strongerDetail = "Outline " + Format(candidateConfig.strongerOutlineWidth);
            string underlayDetail = "U " + Format(candidateConfig.hardUnderlayOffsetX) + "," +
                                    Format(candidateConfig.hardUnderlayOffsetY) +
                                    " / D " + Format(candidateConfig.hardUnderlayDilate);
            string thinDetail = "Outline " + Format(candidateConfig.thinOutlineWidth) + " / " + underlayDetail;
            string currentDetail = "Outline " + Format(ReadMaterialFloat(materials[0], "_OutlineWidth", 0.2f)) +
                                   " / " + underlayDetail;
            string[] details = { "source values", strongerDetail, thinDetail, currentDetail };
            int[] sizes = ComparisonFontSizes;
            for (int row = 0; row < names.Length; row++)
            {
                float y = -52f - row * 112f;
                Image rowBackplate = CreateImage(matrix, "CandidateBackplate" + row, new Color(0f, 0f, 0f, 0.7f));
                RectTransform rowBackplateRect = rowBackplate.rectTransform;
                rowBackplateRect.anchorMin = new Vector2(0f, 1f);
                rowBackplateRect.anchorMax = new Vector2(0f, 1f);
                rowBackplateRect.sizeDelta = new Vector2(245f, 88f);
                rowBackplateRect.anchoredPosition = new Vector2(12f, y);
                CreateLabel(matrix, "CandidateLabel" + row, names[row] + "\n" + details[row], 10,
                    new Color(0.85f, 0.85f, 0.85f), new Vector2(0f, 1f), new Vector2(12f, y),
                    new Vector2(245f, 88f), TextAlignmentOptions.Left, null, null);
                for (int column = 0; column < sizes.Length; column++)
                {
                    float x = 270f + column * 178f;
                    CreateLabel(matrix, "Sample" + row + "_" + column, "한글 Aa 0123", sizes[column], Color.white,
                        new Vector2(0f, 1f), new Vector2(x, y + 20f), new Vector2(165f, 52f),
                        TextAlignmentOptions.Center, source.Font, materials[row]);
                }
            }

            Image lightSwatch = CreateImage(panelRect, "LightWallpaperSwatch", new Color(0.92f, 0.92f, 0.92f, 0.95f));
            RectTransform lightRect = lightSwatch.rectTransform;
            lightRect.anchorMin = new Vector2(1f, 0.5f);
            lightRect.anchorMax = new Vector2(1f, 0.5f);
            lightRect.sizeDelta = new Vector2(250f, 210f);
            lightRect.anchoredPosition = new Vector2(-145f, 100f);
            CreateLabel(lightRect, "LightTitle", "LIGHT WALLPAPER", 10, Color.black, new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(230f, 24f), TextAlignmentOptions.Center, source.Font, materials[0]);
            CreateSwatchSamples(lightRect, "Light", source.Font, materials, Color.white);

            Image darkSwatch = CreateImage(panelRect, "DarkWallpaperSwatch", new Color(0.12f, 0.12f, 0.12f, 0.95f));
            RectTransform darkRect = darkSwatch.rectTransform;
            darkRect.anchorMin = new Vector2(1f, 0.5f);
            darkRect.anchorMax = new Vector2(1f, 0.5f);
            darkRect.sizeDelta = new Vector2(250f, 210f);
            darkRect.anchoredPosition = new Vector2(-145f, -140f);
            CreateLabel(darkRect, "DarkTitle", "DARK WALLPAPER", 10, Color.white, new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(230f, 24f), TextAlignmentOptions.Center, source.Font, materials[0]);
            CreateSwatchSamples(darkRect, "Dark", source.Font, materials, Color.white);

            CreateLabel(panelRect, "Diagnostic", source.Diagnostic, 10, source.IsResolved ? new Color(0.5f, 1f, 0.65f) : new Color(1f, 0.45f, 0.45f),
                new Vector2(0f, 0f), new Vector2(20f, 12f), new Vector2(800f, 24f), TextAlignmentOptions.Left, null, null);
            CreateLabel(panelRect, "TransparentHint", "Panel is intentionally compact; surrounding transparent area shows the real desktop wallpaper.",
                9, new Color(0.6f, 0.6f, 0.6f), new Vector2(1f, 0f), new Vector2(-20f, 12f), new Vector2(400f, 24f),
                TextAlignmentOptions.Right, null, null);
        }

        private Material[] BuildMaterials()
        {
            source = ResolveSource();
            Material baseline = CloneMaterial(source.Material, "Runtime Baseline");
            Material stronger = CloneMaterial(source.Material, "Runtime Stronger Outline");
            SetOutline(stronger, ClampOutlineWidth(candidateConfig.strongerOutlineWidth));
            Material thinUnderlay = CloneMaterial(source.Material, "Runtime Thin Outline Hard Underlay");
            SetOutline(thinUnderlay, ClampOutlineWidth(candidateConfig.thinOutlineWidth));
            SetHardUnderlay(thinUnderlay);
            Material currentUnderlay = CloneMaterial(source.Material, "Runtime Current Outline Hard Underlay");
            SetHardUnderlay(currentUnderlay);
            return new[] { baseline, stronger, thinUnderlay, currentUnderlay };
        }

        private Material CloneMaterial(Material original, string label)
        {
            Material clone = original != null ? new Material(original) : CreateFallbackMaterial();
            clone.name = RootName + " - " + label;
            clone.hideFlags = HideFlags.HideAndDontSave;
            runtimeMaterials.Add(clone);
            return clone;
        }

        private Material CreateFallbackMaterial()
        {
            Shader shader = Shader.Find("TextMeshPro/Distance Field") ?? Shader.Find("UI/Default");
            return new Material(shader) { color = Color.white };
        }

        private void SetOutline(Material material, float width)
        {
            if (material == null) return;
            if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", width);
            material.EnableKeyword("OUTLINE_ON");
        }

        private void SetHardUnderlay(Material material)
        {
            if (material == null) return;
            if (material.HasProperty("_UnderlayDilate")) material.SetFloat("_UnderlayDilate", candidateConfig.hardUnderlayDilate);
            if (material.HasProperty("_UnderlayOffsetX")) material.SetFloat("_UnderlayOffsetX", candidateConfig.hardUnderlayOffsetX);
            if (material.HasProperty("_UnderlayOffsetY")) material.SetFloat("_UnderlayOffsetY", candidateConfig.hardUnderlayOffsetY);
            if (material.HasProperty("_UnderlaySoftness")) material.SetFloat("_UnderlaySoftness", 0f);
            if (material.HasProperty("_UnderlayColor")) material.SetColor("_UnderlayColor", candidateConfig.hardUnderlayColor);
            material.EnableKeyword("UNDERLAY_ON");
        }

        private static float ReadMaterialFloat(Material material, string property, float fallback)
        {
            return material != null && material.HasProperty(property) ? material.GetFloat(property) : fallback;
        }

        private static string Format(float value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private FontAssetSource ResolveSource()
        {
            TextMeshProUGUI[] loadedTexts = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
            TextMeshProUGUI best = null;
            for (int i = 0; i < loadedTexts.Length; i++)
            {
                TextMeshProUGUI candidate = loadedTexts[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid() || candidate.font == null) continue;
                if (candidate.font.name == PreferredFontName && candidate.fontSharedMaterial != null &&
                    candidate.fontSharedMaterial.name == PreferredMaterialName)
                {
                    best = candidate;
                    break;
                }
                if (best == null && candidate.font.name == PreferredFontName) best = candidate;
            }

            if (best != null)
            {
                Material material = best.fontSharedMaterial != null ? best.fontSharedMaterial : best.font.material;
                return new FontAssetSource(best.font, material, true,
                    "Resolved loaded TMP source: " + best.font.name + " / " + material?.name);
            }

            TMP_FontAsset fallbackFont = TMP_Settings.defaultFontAsset;
            Material fallbackMaterial = fallbackFont != null ? fallbackFont.material : null;
            return new FontAssetSource(fallbackFont, fallbackMaterial, false,
                "Mulmaru SDF / Outline B was not found in loaded TMP objects; fallback font used.");
        }

        private void SetVisible(bool visible)
        {
            isVisible = visible;
            if (canvas != null) canvas.enabled = visible;
        }

        private static RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 size, Vector2 position)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void CreateSwatchSamples(Transform parent, string prefix, TMP_FontAsset font,
            Material[] materials, Color textColor)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                int column = i % 2;
                int row = i / 2;
                CreateLabel(parent, prefix + "Sample" + i, (i + 1) + "  한글 Aa 0123", 10, textColor,
                    new Vector2(0.5f, 1f), new Vector2(-58f + column * 116f, -62f - row * 62f),
                    new Vector2(112f, 48f), TextAlignmentOptions.Center, font, materials[i]);
            }
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, Color color,
            Vector2 anchor, Vector2 position, Vector2 dimensions, TextAlignmentOptions alignment,
            TMP_FontAsset font, Material material)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.sizeDelta = dimensions;
            rect.anchoredPosition = position;
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            if (font != null) label.font = font;
            if (material != null) label.fontSharedMaterial = material;
            return label;
        }

        private readonly struct FontAssetSource
        {
            public readonly TMP_FontAsset Font;
            public readonly Material Material;
            public readonly bool IsResolved;
            public readonly string Diagnostic;

            public FontAssetSource(TMP_FontAsset font, Material material, bool resolved, string diagnostic)
            {
                Font = font;
                Material = material;
                IsResolved = resolved;
                Diagnostic = diagnostic;
            }
        }
    }
}
#endif
