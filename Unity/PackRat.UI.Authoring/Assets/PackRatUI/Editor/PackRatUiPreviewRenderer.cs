using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PackRat.UI.Authoring.Editor
{
    /// <summary>
    /// Renders review PNGs from the serialized prefab assets. These previews are deliberately
    /// generated outside the AssetBundle so review tooling never becomes a runtime dependency.
    /// </summary>
    public static class PackRatUiPreviewRenderer
    {
        private const string PillSpritePath = "Assets/PackRatUI/Generated/Pill.png";
        private static readonly Color32 Backdrop = new Color32(66, 86, 103, 255);
        private static readonly Color32 SafeGuide = new Color32(84, 180, 225, 90);

        [MenuItem("PackRat UI/Render Review Previews")]
        public static void RenderReviewPreviews()
        {
            PackRatUiBundleBuilder.CreateOrRefreshPrefabs();
            PackRatUiBundleBuilder.ValidatePrefabs();

            var outputDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "PreviewArtifacts"));
            Directory.CreateDirectory(outputDirectory);

            var previews = new List<PreviewCase>
            {
                new PreviewCase("standalone-1920x1080", PackRatUiBundleBuilder.StandalonePrefabPath,
                    1920, 1080, 1f),
                new PreviewCase("standalone-metrics-expanded-1920x1080",
                    PackRatUiBundleBuilder.StandalonePrefabPath, 1920, 1080, 1.5f,
                    metricsExpanded: true),
                new PreviewCase("embedded-1920x1080", PackRatUiBundleBuilder.EmbeddedPrefabPath,
                    1920, 1080, 1f),
                new PreviewCase("handover-1920x1080", PackRatUiBundleBuilder.HandoverPrefabPath,
                    1920, 1080, 1f),
                new PreviewCase("settings-1920x1080", PackRatUiBundleBuilder.SettingsPrefabPath,
                    1920, 1080, 1f),
                new PreviewCase("standalone-1280x720-zoom150", PackRatUiBundleBuilder.StandalonePrefabPath,
                    1280, 720, 1.5f),
                new PreviewCase("standalone-1280x720", PackRatUiBundleBuilder.StandalonePrefabPath,
                    1280, 720, 1f),
                new PreviewCase("embedded-1280x720-zoom150", PackRatUiBundleBuilder.EmbeddedPrefabPath,
                    1280, 720, 1.5f),
                new PreviewCase("embedded-1280x720", PackRatUiBundleBuilder.EmbeddedPrefabPath,
                    1280, 720, 1f),
                new PreviewCase("handover-1280x720-zoom150", PackRatUiBundleBuilder.HandoverPrefabPath,
                    1280, 720, 1.5f),
                new PreviewCase("handover-1280x720", PackRatUiBundleBuilder.HandoverPrefabPath,
                    1280, 720, 1f),
                new PreviewCase("settings-1280x720", PackRatUiBundleBuilder.SettingsPrefabPath,
                    1280, 720, 1f),
                new PreviewCase("standalone-1280x960", PackRatUiBundleBuilder.StandalonePrefabPath,
                    1280, 960, 1f),
                new PreviewCase("standalone-3440x1440", PackRatUiBundleBuilder.StandalonePrefabPath,
                    3440, 1440, 1f),
                new PreviewCase("standalone-5120x1440", PackRatUiBundleBuilder.StandalonePrefabPath,
                    5120, 1440, 1f)
            };

            foreach (var preview in previews)
                Render(preview, outputDirectory);

            Debug.Log("PackRat UI: rendered " + previews.Count + " review previews to " + outputDirectory);
        }

        [MenuItem("PackRat UI/Render Button Shape Comparison")]
        public static void RenderButtonShapeComparison()
        {
            PackRatUiBundleBuilder.CreateOrRefreshPrefabs();
            PackRatUiBundleBuilder.ValidatePrefabs();

            var outputDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "PreviewArtifacts", "ButtonShapeComparison"));
            Directory.CreateDirectory(outputDirectory);

            var before = new PreviewCase("01-before-oval-buttons-closeup",
                PackRatUiBundleBuilder.StandalonePrefabPath, 960, 720, 1.5f, true);
            var after = new PreviewCase("02-after-rounded-corner-buttons-closeup",
                PackRatUiBundleBuilder.StandalonePrefabPath, 960, 720, 1.5f);

            var beforePath = Render(before, outputDirectory);
            var afterPath = Render(after, outputDirectory);
            CreateSideBySide(beforePath, afterPath,
                Path.Combine(outputDirectory, "00-side-by-side-before-left-after-right.png"));

            var revisedSurfaces = new List<PreviewCase>
            {
                new PreviewCase("03-rounded-corners-embedded-closeup",
                    PackRatUiBundleBuilder.EmbeddedPrefabPath, 960, 720, 1.5f),
                new PreviewCase("04-rounded-corners-handover-closeup",
                    PackRatUiBundleBuilder.HandoverPrefabPath, 960, 720, 1.5f),
                new PreviewCase("05-rounded-corners-settings-closeup",
                    PackRatUiBundleBuilder.SettingsPrefabPath, 960, 720, 1.5f)
            };

            foreach (var preview in revisedSurfaces)
                Render(preview, outputDirectory);

            Debug.Log("PackRat UI: rendered uncached button-shape comparison to " + outputDirectory);
        }

        [MenuItem("PackRat UI/Render Framework Revision Previews")]
        public static void RenderFrameworkRevisionPreviews()
        {
            PackRatUiBundleBuilder.CreateOrRefreshPrefabs();
            PackRatUiBundleBuilder.ValidatePrefabs();

            var outputDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "PreviewArtifacts", "SideRailRevisionR7"));
            Directory.CreateDirectory(outputDirectory);

            var previews = new List<PreviewCase>
            {
                new PreviewCase("01-embedded-side-rail-expanded",
                    PackRatUiBundleBuilder.EmbeddedPrefabPath, 960, 720, 1.5f),
                new PreviewCase("02-embedded-hide-backpack-tooltip",
                    PackRatUiBundleBuilder.EmbeddedPrefabPath, 960, 720, 1.5f,
                    collapseState: CollapsePreviewState.HideTooltip),
                new PreviewCase("03-handover-side-rail-expanded",
                    PackRatUiBundleBuilder.HandoverPrefabPath, 960, 720, 1.5f),
                new PreviewCase("04-handover-hide-backpack-tooltip",
                    PackRatUiBundleBuilder.HandoverPrefabPath, 960, 720, 1.5f,
                    collapseState: CollapsePreviewState.HideTooltip),
                new PreviewCase("05-collapsed-restore-rail",
                    PackRatUiBundleBuilder.EmbeddedPrefabPath, 960, 720, 1.5f,
                    collapseState: CollapsePreviewState.Collapsed)
            };

            var rendered = new Dictionary<string, string>();
            foreach (var preview in previews)
                rendered[preview.Name] = Render(preview, outputDirectory);

            var selectedReference = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PreviewArtifacts",
                "DesignReferences", "selected-side-mounted-collapse-rail.png"));
            if (!File.Exists(selectedReference))
                throw new FileNotFoundException("Selected side-rail design reference is missing.", selectedReference);
            CreateNormalizedSideBySide(selectedReference, rendered[previews[0].Name],
                Path.Combine(outputDirectory, "00-selected-concept-left-unity-r6-right.png"));
            CreateFocusedRailComparison(selectedReference, rendered[previews[0].Name],
                Path.Combine(outputDirectory, "00-focused-side-rail-comparison.png"));

            Debug.Log("PackRat UI: rendered framework revision previews to " + outputDirectory);
        }

        private static string Render(PreviewCase preview, string outputDirectory)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(preview.PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Preview prefab was not found: " + preview.PrefabPath);

            var stage = new GameObject("PackRatUiPreviewStage", typeof(RectTransform), typeof(Canvas));
            var cameraObject = new GameObject("PackRatUiPreviewCamera", typeof(Camera));
            RenderTexture target = null;
            Texture2D image = null;

            try
            {
                var stageRect = stage.GetComponent<RectTransform>();
                stageRect.sizeDelta = new Vector2(preview.Width, preview.Height);
                stageRect.position = Vector3.zero;
                stageRect.localScale = Vector3.one;

                var camera = cameraObject.GetComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = preview.Height * 0.5f;
                camera.aspect = preview.Width / (float)preview.Height;
                camera.transform.position = new Vector3(0f, 0f, -100f);
                camera.transform.rotation = Quaternion.identity;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Backdrop;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 200f;

                var canvas = stage.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                canvas.sortingOrder = 1;

                CreateBackdrop(stage.transform);
                CreateSafeAreaGuide(stage.transform);

                var instance = PrefabUtility.InstantiatePrefab(prefab, stage.transform) as GameObject;
                if (instance == null)
                    throw new InvalidOperationException("Unity could not instantiate preview prefab: " +
                        preview.PrefabPath);

                ConfigureInstance(instance, preview);
                ConfigureCollapsePreview(instance, preview.CollapseState);
                ConfigureMetricsPreview(instance, preview.MetricsExpanded);
                if (preview.UseLegacyPillButtons)
                    ApplyLegacyPillButtons(instance);

                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(stageRect);
                ConfigureScrollPositions(instance);
                Canvas.ForceUpdateCanvases();

                target = new RenderTexture(preview.Width, preview.Height, 24, RenderTextureFormat.ARGB32)
                {
                    name = preview.Name + "-render-target",
                    antiAliasing = 1,
                    filterMode = FilterMode.Bilinear
                };
                target.Create();
                camera.targetTexture = target;
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = target;
                image = new Texture2D(preview.Width, preview.Height, TextureFormat.RGBA32, false, false);
                image.ReadPixels(new Rect(0, 0, preview.Width, preview.Height), 0, 0);
                image.Apply(false, false);
                RenderTexture.active = previous;

                var path = Path.Combine(outputDirectory, preview.Name + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log("PackRat UI preview: " + path);
                return path;
            }
            finally
            {
                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }

                if (image != null)
                    Object.DestroyImmediate(image);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(stage);
            }
        }

        private static void ApplyLegacyPillButtons(GameObject instance)
        {
            var pillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PillSpritePath);
            if (pillSprite == null)
                throw new InvalidOperationException("Legacy comparison sprite was not found: " + PillSpritePath);

            foreach (var button in instance.GetComponentsInChildren<Button>(true))
            {
                if (!(button.targetGraphic is Image image))
                    continue;

                image.sprite = pillSprite;
                image.type = Image.Type.Sliced;
            }
        }

        private static void CreateSideBySide(string leftPath, string rightPath, string outputPath)
        {
            const int dividerWidth = 12;
            Texture2D left = null;
            Texture2D right = null;
            Texture2D comparison = null;

            try
            {
                left = LoadPng(leftPath);
                right = LoadPng(rightPath);
                if (left.width != right.width || left.height != right.height)
                    throw new InvalidOperationException("Button comparison renders must have matching dimensions.");

                comparison = new Texture2D(left.width + dividerWidth + right.width, left.height,
                    TextureFormat.RGBA32, false, false);
                var divider = new Color32[dividerWidth * left.height];
                for (var index = 0; index < divider.Length; index++)
                    divider[index] = new Color32(18, 25, 31, 255);

                comparison.SetPixels32(0, 0, left.width, left.height, left.GetPixels32());
                comparison.SetPixels32(left.width, 0, dividerWidth, left.height, divider);
                comparison.SetPixels32(left.width + dividerWidth, 0, right.width, right.height,
                    right.GetPixels32());
                comparison.Apply(false, false);
                File.WriteAllBytes(outputPath, comparison.EncodeToPNG());
                Debug.Log("PackRat UI button comparison: " + outputPath);
            }
            finally
            {
                if (left != null)
                    Object.DestroyImmediate(left);
                if (right != null)
                    Object.DestroyImmediate(right);
                if (comparison != null)
                    Object.DestroyImmediate(comparison);
            }
        }

        private static void CreateNormalizedSideBySide(string leftPath, string rightPath, string outputPath)
        {
            Texture2D source = null;
            Texture2D implementation = null;
            Texture2D normalized = null;
            try
            {
                source = LoadPng(leftPath);
                implementation = LoadPng(rightPath);
                normalized = Resize(source, implementation.width, implementation.height);
                var temporaryPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? string.Empty,
                    ".normalized-selected-concept.png");
                File.WriteAllBytes(temporaryPath, normalized.EncodeToPNG());
                try
                {
                    CreateSideBySide(temporaryPath, rightPath, outputPath);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }
            finally
            {
                if (source != null)
                    Object.DestroyImmediate(source);
                if (implementation != null)
                    Object.DestroyImmediate(implementation);
                if (normalized != null)
                    Object.DestroyImmediate(normalized);
            }
        }

        private static Texture2D Resize(Texture2D source, int width, int height)
        {
            var resized = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                resized.SetPixel(x, y, source.GetPixelBilinear(
                    (x + 0.5f) / width, (y + 0.5f) / height));
            }
            resized.Apply(false, false);
            return resized;
        }

        private static void CreateFocusedRailComparison(string sourcePath, string implementationPath,
            string outputPath)
        {
            Texture2D source = null;
            Texture2D implementation = null;
            Texture2D normalized = null;
            Texture2D sourceCrop = null;
            Texture2D implementationCrop = null;
            Texture2D comparison = null;
            try
            {
                source = LoadPng(sourcePath);
                implementation = LoadPng(implementationPath);
                if (implementation.width != 960 || implementation.height != 720)
                    throw new InvalidOperationException("Focused side-rail QA expects a 960x720 implementation.");
                normalized = Resize(source, implementation.width, implementation.height);
                sourceCrop = CropFromTop(normalized, 210, 250, 180, 160);
                implementationCrop = CropFromTop(implementation, 230, 280, 180, 160);

                const int dividerWidth = 8;
                comparison = new Texture2D(sourceCrop.width + dividerWidth + implementationCrop.width,
                    sourceCrop.height, TextureFormat.RGBA32, false, false);
                comparison.SetPixels32(0, 0, sourceCrop.width, sourceCrop.height, sourceCrop.GetPixels32());
                var divider = Enumerable.Repeat(new Color32(18, 25, 31, 255),
                    dividerWidth * sourceCrop.height).ToArray();
                comparison.SetPixels32(sourceCrop.width, 0, dividerWidth, sourceCrop.height, divider);
                comparison.SetPixels32(sourceCrop.width + dividerWidth, 0, implementationCrop.width,
                    implementationCrop.height, implementationCrop.GetPixels32());
                comparison.Apply(false, false);
                File.WriteAllBytes(outputPath, comparison.EncodeToPNG());
                Debug.Log("PackRat UI focused rail comparison: " + outputPath);
            }
            finally
            {
                if (source != null)
                    Object.DestroyImmediate(source);
                if (implementation != null)
                    Object.DestroyImmediate(implementation);
                if (normalized != null)
                    Object.DestroyImmediate(normalized);
                if (sourceCrop != null)
                    Object.DestroyImmediate(sourceCrop);
                if (implementationCrop != null)
                    Object.DestroyImmediate(implementationCrop);
                if (comparison != null)
                    Object.DestroyImmediate(comparison);
            }
        }

        private static Texture2D CropFromTop(Texture2D source, int left, int top, int width, int height)
        {
            var bottom = source.height - top - height;
            if (left < 0 || bottom < 0 || left + width > source.width || bottom + height > source.height)
                throw new ArgumentOutOfRangeException(nameof(left), "Focused comparison crop exceeds its source.");
            var crop = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            crop.SetPixels(source.GetPixels(left, bottom, width, height));
            crop.Apply(false, false);
            return crop;
        }

        private static Texture2D LoadPng(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (!texture.LoadImage(File.ReadAllBytes(path), false))
            {
                Object.DestroyImmediate(texture);
                throw new InvalidOperationException("Unity could not decode comparison PNG: " + path);
            }

            return texture;
        }

        private static void ConfigureInstance(GameObject instance, PreviewCase preview)
        {
            var rect = instance.GetComponent<RectTransform>();
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.identity;

            // Dedicated PackRat canvases match the reference height. Reproducing the resulting
            // physical scale here lets PNG review use the same policy without depending on the
            // batch editor's own window size.
            var canvasScale = preview.Height / 1080f;
            if (preview.PrefabPath == PackRatUiBundleBuilder.SettingsPrefabPath)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;

                var settingsCard = instance.transform.Find("Card") as RectTransform;
                if (settingsCard != null)
                    settingsCard.localScale = Vector3.one * canvasScale * preview.Zoom;
                return;
            }

            rect.localScale = Vector3.one * canvasScale * preview.Zoom;
        }

        private static void ConfigureCollapsePreview(GameObject instance, CollapsePreviewState state)
        {
            if (state == CollapsePreviewState.Default)
                return;

            if (state == CollapsePreviewState.HideTooltip)
            {
                var trigger = instance.transform.Find("CollapseRail/HideButton")?.GetComponent<EventTrigger>();
                var tooltip = instance.transform.Find("CollapseRail/Tooltip");
                if (trigger == null || tooltip == null)
                    throw new InvalidOperationException("Hide backpack tooltip interaction contract is missing.");
                InvokeTooltipEvent(trigger, tooltip.gameObject, EventTriggerType.PointerEnter, true);
                InvokeTooltipEvent(trigger, tooltip.gameObject, EventTriggerType.PointerExit, false);
                InvokeTooltipEvent(trigger, tooltip.gameObject, EventTriggerType.Select, true);
                InvokeTooltipEvent(trigger, tooltip.gameObject, EventTriggerType.Deselect, false);
                InvokeTooltipEvent(trigger, tooltip.gameObject, EventTriggerType.PointerEnter, true);
                return;
            }

            var collapsedHandle = instance.transform.Find("CollapsedHandle");
            if (collapsedHandle == null)
                throw new InvalidOperationException("Collapsed restore rail is missing from preview prefab.");
            foreach (Transform child in instance.transform)
            {
                if (child != collapsedHandle)
                    child.gameObject.SetActive(false);
            }
            var rootImage = instance.GetComponent<Image>();
            if (rootImage != null)
                rootImage.enabled = false;
            collapsedHandle.gameObject.SetActive(true);
        }

        private static void ConfigureMetricsPreview(GameObject instance, bool expanded)
        {
            if (!expanded)
                return;

            var tray = instance.transform.Find("OverlayHost/MetricsTray") as RectTransform;
            var toggle = instance.transform.Find("OverlayHost/MetricsToggle") as RectTransform;
            var content = instance.transform.Find(
                "OverlayHost/MetricsTray/Panel/Scroll/Viewport/Content") as RectTransform;
            var template = content?.Find("RowTemplate")?.gameObject;
            var empty = instance.transform.Find(
                "OverlayHost/MetricsTray/Panel/Scroll/Viewport/EmptyLabel");
            var summary = instance.transform.Find("OverlayHost/MetricsTray/Panel/Summary")?.GetComponent<Text>();
            if (tray == null || toggle == null || content == null || template == null || summary == null)
                throw new InvalidOperationException("Expanded metrics preview contract is incomplete.");

            tray.gameObject.SetActive(true);
            var canvasGroup = tray.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
                canvasGroup.alpha = 1f;
            toggle.anchoredPosition = new Vector2(tray.anchoredPosition.x - tray.sizeDelta.x,
                toggle.anchoredPosition.y);
            var openIcon = toggle.Find("OpenIcon");
            var closeIcon = toggle.Find("CloseIcon");
            if (openIcon != null)
                openIcon.gameObject.SetActive(false);
            if (closeIcon != null)
                closeIcon.gameObject.SetActive(true);
            if (empty != null)
                empty.gameObject.SetActive(false);

            var products = new[]
            {
                ("ALASKAN SNORLAX", "QTY 17  •  2 BAGS  •  3 JARS\nEA $157  •  TOTAL $2,669",
                    new Color32(92, 190, 104, 255)),
                ("COCAINE", "QTY 5  •  5 BAGS\nEA $150  •  TOTAL $750",
                    new Color32(244, 247, 250, 255)),
                ("COLUMBIAN BAM BAM", "QTY 1  •  1 UNPACKAGED\nEA $617  •  TOTAL $617",
                    new Color32(244, 247, 250, 255)),
                ("GRANDDADDY PURPLE", "QTY 7  •  2 BAGS  •  1 JAR\nEA $44  •  TOTAL $308",
                    new Color32(92, 190, 104, 255)),
                ("HEISENBERG'S SPECIAL", "QTY 20  •  1 BRICK\nEA $288  •  TOTAL $5,760",
                    new Color32(238, 151, 61, 255)),
                ("ICE CREAM CRYSTAL", "QTY 4  •  4 UNPACKAGED\nEA $144  •  TOTAL $576",
                    new Color32(238, 151, 61, 255)),
                ("OG KUSH", "QTY 2  •  2 BAGS\nEA $53  •  TOTAL $106",
                    new Color32(92, 190, 104, 255)),
                ("SOUR DIESEL", "QTY 2  •  2 BAGS\nEA $56  •  TOTAL $112",
                    new Color32(92, 190, 104, 255)),
                ("GOLDEN CAPS", "QTY 10  •  2 JARS\nEA $80  •  TOTAL $800",
                    new Color32(76, 173, 229, 255))
            };
            for (var index = 0; index < products.Length; index++)
            {
                var row = Object.Instantiate(template, content, false);
                row.name = "PreviewMetricRow" + index;
                row.SetActive(true);
                var rect = row.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                var top = 2f + index * 72f;
                rect.offsetMin = new Vector2(1f, -top - 68f);
                rect.offsetMax = new Vector2(-1f, -top);
                var name = row.transform.Find("Name").GetComponent<Text>();
                name.text = EllipsizePreviewText(name, products[index].Item1);
                row.transform.Find("Details").GetComponent<Text>().text = products[index].Item2;
                row.transform.Find("Accent").GetComponent<Image>().color = products[index].Item3;
            }

            content.sizeDelta = new Vector2(0f, 2f + products.Length * 72f);
            summary.text = "9 TYPES  •  QTY 68  •  VALUE $11,698";
        }

        private static string EllipsizePreviewText(Text label, string value)
        {
            if (label == null || string.IsNullOrEmpty(value))
                return value ?? string.Empty;

            var maxWidth = Mathf.Max(1f, label.rectTransform.rect.width);
            label.text = value;
            if (label.preferredWidth <= maxWidth + 0.5f)
                return value;

            const string ellipsis = "…";
            var low = 0;
            var high = value.Length;
            var best = ellipsis;
            while (low <= high)
            {
                var length = low + ((high - low) / 2);
                var candidate = value.Substring(0, length).TrimEnd() + ellipsis;
                label.text = candidate;
                if (label.preferredWidth <= maxWidth + 0.5f)
                {
                    best = candidate;
                    low = length + 1;
                }
                else
                {
                    high = length - 1;
                }
            }

            return best;
        }

        private static void ConfigureScrollPositions(GameObject instance)
        {
            if (instance == null)
                return;

            foreach (var scroll in instance.GetComponentsInChildren<ScrollRect>(includeInactive: true))
            {
                if (scroll?.content == null)
                    continue;

                LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
                scroll.verticalNormalizedPosition = 1f;
                scroll.StopMovement();
            }
        }

        private static void InvokeTooltipEvent(EventTrigger trigger, GameObject tooltip,
            EventTriggerType eventType, bool expectedVisible)
        {
            var entry = trigger.triggers.Find(candidate => candidate.eventID == eventType);
            if (entry == null)
                throw new InvalidOperationException("Tooltip event is missing: " + eventType);
            entry.callback.Invoke(null);
            if (tooltip.activeSelf != expectedVisible)
                throw new InvalidOperationException("Tooltip event " + eventType + " did not set visibility to " +
                    expectedVisible + ".");
        }

        private static void CreateBackdrop(Transform parent)
        {
            var backdrop = new GameObject("PreviewBackdrop", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            backdrop.transform.SetParent(parent, false);
            backdrop.transform.SetAsFirstSibling();
            var rect = backdrop.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            backdrop.GetComponent<Image>().color = Backdrop;
        }

        private static void CreateSafeAreaGuide(Transform parent)
        {
            var guide = new GameObject("SafeArea24pxGuide", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Outline));
            guide.transform.SetParent(parent, false);
            var rect = guide.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(24f, 24f);
            rect.offsetMax = new Vector2(-24f, -24f);
            var image = guide.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = false;
            var outline = guide.GetComponent<Outline>();
            outline.effectColor = SafeGuide;
            outline.effectDistance = new Vector2(2f, -2f);
        }

        private readonly struct PreviewCase
        {
            public PreviewCase(string name, string prefabPath, int width, int height, float zoom,
                bool useLegacyPillButtons = false, bool metricsExpanded = false,
                CollapsePreviewState collapseState = CollapsePreviewState.Default)
            {
                Name = name;
                PrefabPath = prefabPath;
                Width = width;
                Height = height;
                Zoom = zoom;
                UseLegacyPillButtons = useLegacyPillButtons;
                MetricsExpanded = metricsExpanded;
                CollapseState = collapseState;
            }

            public string Name { get; }
            public string PrefabPath { get; }
            public int Width { get; }
            public int Height { get; }
            public float Zoom { get; }
            public bool UseLegacyPillButtons { get; }
            public bool MetricsExpanded { get; }
            public CollapsePreviewState CollapseState { get; }
        }

        private enum CollapsePreviewState
        {
            Default,
            HideTooltip,
            Collapsed
        }
    }
}
