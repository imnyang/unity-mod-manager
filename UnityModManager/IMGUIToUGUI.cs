using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace UnityModManagerNet
{
    internal sealed class IMGUIToUGUI : IDisposable
    {
        private const string PatchId = "UnityModManager.UGUICompatibility";
        private static IMGUIToUGUI current;
        private readonly Harmony harmony;
        private readonly RectTransform canvasRoot;
        private readonly List<Paint> paints = new List<Paint>();
        private readonly Dictionary<SpriteKey, Sprite> sprites = new Dictionary<SpriteKey, Sprite>();
        private readonly Dictionary<GUIStyle, GUIStyle> transparentWindows = new Dictionary<GUIStyle, GUIStyle>();
        private readonly MethodInfo unclip;
        private readonly PropertyInfo visibleRect;
        private readonly PropertyInfo clipOffset;
        private readonly Font fallbackFont;
        private int paintCount;
        private Rect? clipBoundary;

        private sealed class Paint
        {
            internal RectTransform Clip;
            internal Image Background;
            internal Image Check;
            internal RawImage Texture;
            internal Text Text;
            internal readonly List<Image> Selection = new List<Image>();
        }

        private struct SpriteKey : IEquatable<SpriteKey>
        {
            internal int Texture, Left, Bottom, Right, Top;
            public bool Equals(SpriteKey other)
            {
                return Texture == other.Texture && Left == other.Left && Bottom == other.Bottom && Right == other.Right && Top == other.Top;
            }
            public override bool Equals(object obj) { return obj is SpriteKey && Equals((SpriteKey)obj); }
            public override int GetHashCode()
            {
                unchecked { return ((((Texture * 397 ^ Left) * 397 ^ Bottom) * 397 ^ Right) * 397 ^ Top); }
            }
        }

        internal IMGUIToUGUI(Transform owner)
        {
            var guiClip = typeof(GUI).Assembly.GetType("UnityEngine.GUIClip");
            if (guiClip == null)
                throw new NotSupportedException("Unity GUIClip was not found.");
            unclip = guiClip.GetMethod("Unclip", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Rect) }, null);
            visibleRect = guiClip.GetProperty("visibleRect", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            clipOffset = typeof(GUIStyle).GetProperty("clipOffset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (unclip == null || visibleRect == null)
                throw new NotSupportedException("Unity GUI clipping APIs were not found.");

            var root = new GameObject("UMM OnGUI uGUI renderer", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(owner, false);
            canvasRoot = root.GetComponent<RectTransform>();
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            canvas.scaleFactor = 1;
            fallbackFont = Font.CreateDynamicFontFromOSFont("Arial", 13);
            harmony = new Harmony(PatchId);
            try
            {
                var stylePrefix = new HarmonyMethod(typeof(IMGUIToUGUI).GetMethod("StylePrefix", BindingFlags.NonPublic | BindingFlags.Static));
                var texturePrefix = new HarmonyMethod(typeof(IMGUIToUGUI).GetMethod("TexturePrefix", BindingFlags.NonPublic | BindingFlags.Static));
                var windowPrefix = new HarmonyMethod(typeof(IMGUIToUGUI).GetMethod("WindowPrefix", BindingFlags.NonPublic | BindingFlags.Static));
                foreach (var method in StyleMethods())
                    harmony.Patch(method, prefix: stylePrefix);
                foreach (var method in TextureMethods())
                    harmony.Patch(method, prefix: texturePrefix);
                foreach (var method in WindowMethods())
                    harmony.Patch(method, prefix: windowPrefix);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal static IEnumerable<MethodInfo> StyleMethods()
        {
            return typeof(GUIStyle).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(method => (method.Name == "Draw" || method.Name == "DrawWithTextSelection" ||
                    method.Name == "DrawCursor" || method.Name == "DrawPrefixLabel") &&
                    method.GetMethodBody() != null && method.GetParameters().Any(p => p.ParameterType == typeof(Rect)));
        }

        internal static IEnumerable<MethodInfo> TextureMethods()
        {
            return typeof(GUI).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => (method.Name == "DrawTexture" || method.Name == "DrawTextureWithTexCoords") && method.GetMethodBody() != null);
        }

        internal void BeginFrame()
        {
            if (current != null)
                throw new InvalidOperationException("Nested UMM GUI capture.");
            current = this;
            clipBoundary = null;
            if (Event.current.type == EventType.Repaint)
                paintCount = 0;
        }

        internal void EndFrame()
        {
            current = null;
            clipBoundary = null;
            if (Event.current.type != EventType.Repaint)
                return;
            for (var i = paintCount; i < paints.Count; i++)
                paints[i].Clip.gameObject.SetActive(false);
        }

        internal void SetClipBoundary(Rect? boundary) { clipBoundary = boundary; }

        internal static IEnumerable<MethodInfo> WindowMethods()
        {
            return typeof(GUI).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(method => (method.Name == "DoWindow" || method.Name == "DoModalWindow") && method.GetMethodBody() != null);
        }

        private static void WindowPrefix(object[] __args)
        {
            if (current == null)
                return;
            var styleIndex = Array.FindIndex(__args, arg => arg is GUIStyle);
            var callbackIndex = Array.FindIndex(__args, arg => arg is GUI.WindowFunction);
            if (styleIndex < 0 || callbackIndex < 0)
                return;
            var bridge = current;
            var style = (GUIStyle)__args[styleIndex];
            var rect = (Rect)__args.First(arg => arg is Rect);
            var callback = (GUI.WindowFunction)__args[callbackIndex];
            var originalContent = __args.OfType<GUIContent>().FirstOrDefault();
            var content = originalContent == null ? GUIContent.none : new GUIContent(originalContent);
            GUIStyle transparent;
            if (!bridge.transparentWindows.TryGetValue(style, out transparent))
            {
                transparent = new GUIStyle(style);
                bridge.transparentWindows.Add(style, transparent);
            }
            // Preserve the native window's layout/input metrics but suppress its native paint.
            transparent.padding = new RectOffset(style.padding.left, style.padding.right, style.padding.top, style.padding.bottom);
            transparent.margin = new RectOffset(style.margin.left, style.margin.right, style.margin.top, style.margin.bottom);
            transparent.fixedWidth = style.fixedWidth;
            transparent.fixedHeight = style.fixedHeight;
            foreach (var state in new[] { transparent.normal, transparent.hover, transparent.active, transparent.focused,
                transparent.onNormal, transparent.onHover, transparent.onActive, transparent.onFocused })
            {
                state.background = null;
                state.textColor = Color.clear;
            }
            __args[styleIndex] = transparent;
            __args[callbackIndex] = new GUI.WindowFunction(id =>
            {
                if (Event.current.type == EventType.Repaint)
                    style.Draw(new Rect(0, 0, rect.width, rect.height), content, id);
                callback(id);
            });
        }

        private static bool StylePrefix(GUIStyle __instance, MethodBase __originalMethod, object[] __args)
        {
            if (current == null || Event.current.type != EventType.Repaint)
                return true;
            var parameters = __originalMethod.GetParameters();
            var position = Rect.zero;
            var content = GUIContent.none;
            var hover = false;
            var active = false;
            var on = false;
            var focused = false;
            var first = -1;
            var last = -1;
            var cursor = -1;
            var control = -1;
            for (var i = 0; i < parameters.Length; i++)
            {
                var name = parameters[i].Name.ToLowerInvariant();
                if (__args[i] is Rect) position = (Rect)__args[i];
                else if (__args[i] is GUIContent) content = (GUIContent)__args[i];
                else if (__args[i] is string) content = new GUIContent((string)__args[i]);
                else if (__args[i] is Texture) content = new GUIContent((Texture)__args[i]);
                else if (__args[i] is bool)
                {
                    if (name.Contains("hover")) hover = (bool)__args[i];
                    else if (name.Contains("active")) active = (bool)__args[i];
                    else if (name.Contains("focus")) focused = (bool)__args[i];
                    else if (name == "on") on = (bool)__args[i];
                }
                else if (__args[i] is int)
                {
                    if (name.Contains("control")) control = (int)__args[i];
                    else if (name.Contains("first")) first = (int)__args[i];
                    else if (name.Contains("last")) last = (int)__args[i];
                    else if (name.Contains("character")) cursor = (int)__args[i];
                }
            }
            if (control >= 0)
            {
                hover = position.Contains(Event.current.mousePosition);
                active = control == GUIUtility.hotControl;
                focused = control == GUIUtility.keyboardControl;
            }
            if (hover && !string.IsNullOrEmpty(content.tooltip))
                GUI.tooltip = content.tooltip;
            if (__originalMethod.Name != "DrawCursor")
                current.DrawStyle(__instance, position, content, hover, active, on, focused, focused ? first : -1, last);
            if (focused && first >= 0 && last >= 0)
                cursor = first;
            if (focused && cursor >= 0)
                current.DrawCaret(__instance, position, content, cursor);
            return false;
        }

        private static bool TexturePrefix(MethodBase __originalMethod, object[] __args)
        {
            if (current == null || Event.current.type != EventType.Repaint)
                return true;
            var rect = (Rect)__args[0];
            var texture = __args[1] as Texture;
            if (!texture)
                return false;
            var uv = new Rect(0, 0, 1, 1);
            var mode = ScaleMode.StretchToFill;
            var aspect = (float)texture.width / texture.height;
            var color = GUI.color;
            var parameters = __originalMethod.GetParameters();
            for (var i = 2; i < __args.Length; i++)
            {
                if (__args[i] is Rect) uv = (Rect)__args[i];
                else if (__args[i] is ScaleMode) mode = (ScaleMode)__args[i];
                else if (__args[i] is Color) color *= (Color)__args[i];
                else if (__args[i] is float && parameters[i].Name == "imageAspect" && (float)__args[i] > 0)
                    aspect = (float)__args[i];
            }
            if (__originalMethod.Name == "DrawTexture")
                ScaleTexture(ref rect, ref uv, mode, aspect);
            current.DrawTexture(rect, texture, uv, color);
            return false;
        }

        internal static void ScaleTexture(ref Rect rect, ref Rect uv, ScaleMode mode, float aspect)
        {
            if (rect.height <= 0 || rect.width <= 0 || aspect <= 0)
                return;
            var targetAspect = rect.width / rect.height;
            if (mode == ScaleMode.ScaleToFit)
            {
                if (targetAspect > aspect)
                {
                    var width = rect.height * aspect;
                    rect.x += (rect.width - width) / 2;
                    rect.width = width;
                }
                else
                {
                    var height = rect.width / aspect;
                    rect.y += (rect.height - height) / 2;
                    rect.height = height;
                }
            }
            else if (mode == ScaleMode.ScaleAndCrop)
            {
                if (targetAspect > aspect)
                {
                    uv.height = aspect / targetAspect;
                    uv.y = (1 - uv.height) / 2;
                }
                else
                {
                    uv.width = targetAspect / aspect;
                    uv.x = (1 - uv.width) / 2;
                }
            }
        }

        private Rect ScreenRect(Rect rect) { return (Rect)unclip.Invoke(null, new object[] { rect }); }

        private Rect CurrentClip()
        {
            var clip = ScreenRect((Rect)visibleRect.GetValue(null, null));
            clip = Intersect(clip, new Rect(0, 0, Screen.width, Screen.height));
            if (clipBoundary.HasValue)
                clip = Intersect(clip, clipBoundary.Value);
            return clip;
        }

        internal static Rect Intersect(Rect first, Rect second)
        {
            var left = Math.Max(first.xMin, second.xMin);
            var top = Math.Max(first.yMin, second.yMin);
            var right = Math.Min(first.xMax, second.xMax);
            var bottom = Math.Min(first.yMax, second.yMax);
            return new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        private static RectTransform RectObject(Transform parent, string name)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            return rect;
        }

        private static void Place(RectTransform target, Rect rect)
        {
            target.anchoredPosition = new Vector2(rect.x, -rect.y);
            target.sizeDelta = new Vector2(rect.width, rect.height);
        }

        private Paint NextPaint(Rect clip)
        {
            Paint paint;
            if (paintCount < paints.Count)
                paint = paints[paintCount];
            else
            {
                paint = new Paint { Clip = RectObject(canvasRoot, "IMGUI paint " + paintCount) };
                var maskImage = paint.Clip.gameObject.AddComponent<Image>();
                maskImage.raycastTarget = false;
                paint.Clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                paint.Background = RectObject(paint.Clip, "Background").gameObject.AddComponent<Image>();
                paint.Check = RectObject(paint.Clip, "Check").gameObject.AddComponent<Image>();
                paint.Texture = RectObject(paint.Clip, "Image").gameObject.AddComponent<RawImage>();
                paint.Text = RectObject(paint.Clip, "Text").gameObject.AddComponent<Text>();
                paint.Background.raycastTarget = paint.Check.raycastTarget = paint.Texture.raycastTarget = paint.Text.raycastTarget = false;
                paints.Add(paint);
            }
            paintCount++;
            Place(paint.Clip, clip);
            paint.Clip.gameObject.SetActive(true);
            paint.Background.gameObject.SetActive(false);
            paint.Check.gameObject.SetActive(false);
            paint.Texture.gameObject.SetActive(false);
            paint.Text.gameObject.SetActive(false);
            foreach (var selection in paint.Selection)
                selection.gameObject.SetActive(false);
            return paint;
        }

        private Sprite BackgroundSprite(Texture2D texture, RectOffset border)
        {
            var key = new SpriteKey
            {
                Texture = texture.GetInstanceID(), Left = border.left, Bottom = border.bottom,
                Right = border.right, Top = border.top
            };
            Sprite sprite;
            if (sprites.TryGetValue(key, out sprite) && sprite)
                return sprite;
            var borders = new Vector4(Math.Min(border.left, texture.width / 2), Math.Min(border.bottom, texture.height / 2),
                Math.Min(border.right, texture.width / 2), Math.Min(border.top, texture.height / 2));
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, borders);
            sprites[key] = sprite;
            return sprite;
        }

        private static GUIStyleState State(GUIStyle style, bool hover, bool active, bool on, bool focused)
        {
            if (!GUI.enabled)
                return on ? style.onNormal : style.normal;
            if (active) return on ? style.onActive : style.active;
            if (hover) return on ? style.onHover : style.hover;
            if (focused) return on ? style.onFocused : style.focused;
            return on ? style.onNormal : style.normal;
        }

        private enum DefaultControl { Custom, Button, Toggle, Input, Panel, HorizontalTrack, VerticalTrack, Thumb }

        private static bool Matches(GUIStyle style, GUIStyle standard)
        {
            // An unchanged copy of a default style can share the theme. A mod with its
            // own background textures keeps them, along with its fonts and text colors.
            return ReferenceEquals(style, standard) || style.name == standard.name &&
                style.normal.background == standard.normal.background && style.active.background == standard.active.background;
        }

        private static DefaultControl Classify(GUIStyle style)
        {
            var skin = GUI.skin;
            if (Matches(style, skin.button) || style.name == "umm button") return DefaultControl.Button;
            if (Matches(style, skin.toggle)) return DefaultControl.Toggle;
            if (Matches(style, skin.textField) || Matches(style, skin.textArea)) return DefaultControl.Input;
            if (Matches(style, skin.box) || Matches(style, skin.window) || style.name == "umm window") return DefaultControl.Panel;
            if (Matches(style, skin.horizontalSlider) || Matches(style, skin.horizontalScrollbar)) return DefaultControl.HorizontalTrack;
            if (Matches(style, skin.verticalSlider) || Matches(style, skin.verticalScrollbar)) return DefaultControl.VerticalTrack;
            if (Matches(style, skin.horizontalSliderThumb) || Matches(style, skin.verticalSliderThumb) ||
                Matches(style, skin.horizontalScrollbarThumb) || Matches(style, skin.verticalScrollbarThumb)) return DefaultControl.Thumb;
            return DefaultControl.Custom;
        }

        private static void Solid(Image image, Rect position, Color color, float radius = 2, bool border = false)
        {
            Place(image.rectTransform, position);
            UGUITheme.Shape(image, radius, border);
            image.color = color * GUI.color * GUI.backgroundColor;
            image.gameObject.SetActive(true);
        }

        private void DrawStyle(GUIStyle style, Rect position, GUIContent content, bool hover, bool active, bool on, bool focused, int first, int last)
        {
            var screen = ScreenRect(position);
            var clip = CurrentClip();
            if (style.clipping == TextClipping.Clip)
                clip = Intersect(clip, screen);
            if (clip.width <= 0 || clip.height <= 0)
                return;
            var paint = NextPaint(clip);
            var outline = paint.Background.GetComponent<Outline>();
            if (outline) outline.enabled = false;
            var state = State(style, hover, active, on, focused);
            var local = new Rect(screen.x - clip.x, screen.y - clip.y, screen.width, screen.height);
            var control = Classify(style);
            var scale = UnityModManager.UI.Scale(1f);
            if (control == DefaultControl.Toggle)
            {
                var box = UGUITheme.CheckboxRect(local, scale);
                Solid(paint.Background, box, UGUITheme.ControlColor(hover, active, false, GUI.enabled), 2 * scale, true);
                if (on)
                {
                    var inset = Math.Min(5 * scale, box.width / 3);
                    Solid(paint.Check, new Rect(box.x + inset, box.y + inset,
                        Math.Max(0, box.width - inset * 2), Math.Max(0, box.height - inset * 2)), UGUITheme.Accent, scale);
                }
            }
            else if (control == DefaultControl.HorizontalTrack || control == DefaultControl.VerticalTrack)
            {
                var horizontal = control == DefaultControl.HorizontalTrack;
                var thickness = Math.Min(4 * scale, horizontal ? local.height : local.width);
                var track = horizontal ? new Rect(local.x, local.y + (local.height - thickness) / 2, local.width, thickness)
                    : new Rect(local.x + (local.width - thickness) / 2, local.y, thickness, local.height);
                Solid(paint.Background, track, UGUITheme.Track, thickness / 2);
            }
            else if (control != DefaultControl.Custom)
            {
                var color = control == DefaultControl.Input ? focused ? UGUITheme.Pressed : UGUITheme.Input :
                    control == DefaultControl.Panel ? UGUITheme.Panel : control == DefaultControl.Thumb ? UGUITheme.Accent :
                    UGUITheme.ControlColor(hover, active, on, GUI.enabled);
                Solid(paint.Background, local, color, 2 * scale,
                    control == DefaultControl.Input);
            }
            else if (state.background)
            {
                var overflow = style.overflow;
                var background = new Rect(local.x - overflow.left, local.y - overflow.top,
                    local.width + overflow.horizontal, local.height + overflow.vertical);
                Place(paint.Background.rectTransform, background);
                paint.Background.sprite = BackgroundSprite(state.background, style.border);
                paint.Background.type = style.border.horizontal + style.border.vertical > 0 ? Image.Type.Sliced : Image.Type.Simple;
                paint.Background.color = GUI.color * GUI.backgroundColor;
                paint.Background.gameObject.SetActive(true);
            }
            var padding = style.padding;
            var offset = style.contentOffset;
            if (clipOffset != null)
                offset -= (Vector2)clipOffset.GetValue(style, null);
            var contentRect = new Rect(local.x + padding.left + offset.x, local.y + padding.top + offset.y,
                Math.Max(0, local.width - padding.horizontal), Math.Max(0, local.height - padding.vertical));
            if (control == DefaultControl.Toggle)
            {
                var box = UGUITheme.CheckboxRect(local, scale);
                var start = Math.Max(contentRect.x, box.xMax + 6 * scale);
                contentRect.width = Math.Max(0, contentRect.xMax - start);
                contentRect.x = start;
            }
            var contentColor = GUI.color * GUI.contentColor;
            if (!GUI.enabled)
                contentColor.a *= .5f;
            if (content.image && style.imagePosition != ImagePosition.TextOnly)
            {
                var imageWidth = Math.Min(content.image.width, contentRect.width);
                var imageHeight = Math.Min(content.image.height, contentRect.height);
                var imageRect = new Rect(contentRect.x, contentRect.y, imageWidth, imageHeight);
                if (style.imagePosition == ImagePosition.ImageOnly)
                    Align(ref imageRect, contentRect, style.alignment);
                else if (style.imagePosition == ImagePosition.ImageAbove)
                {
                    imageRect.x += (contentRect.width - imageWidth) / 2;
                    contentRect.y += imageHeight;
                    contentRect.height = Math.Max(0, contentRect.height - imageHeight);
                }
                else
                {
                    imageRect.y += (contentRect.height - imageHeight) / 2;
                    contentRect.x += imageWidth;
                    contentRect.width = Math.Max(0, contentRect.width - imageWidth);
                }
                Place(paint.Texture.rectTransform, imageRect);
                paint.Texture.texture = content.image;
                paint.Texture.uvRect = new Rect(0, 0, 1, 1);
                paint.Texture.color = contentColor;
                paint.Texture.gameObject.SetActive(true);
            }
            if (!string.IsNullOrEmpty(content.text) && style.imagePosition != ImagePosition.ImageOnly)
            {
                if (first >= 0 && last >= 0)
                    DrawSelection(paint, clip, style, position, content, first, last);
                Place(paint.Text.rectTransform, contentRect);
                paint.Text.font = style.font ? style.font : GUI.skin.font ? GUI.skin.font : fallbackFont;
                paint.Text.fontSize = style.fontSize > 0 ? style.fontSize : Math.Max(1, paint.Text.font.fontSize);
                paint.Text.fontStyle = style.fontStyle;
                paint.Text.alignment = style.alignment;
                paint.Text.supportRichText = style.richText;
                paint.Text.horizontalOverflow = style.wordWrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
                paint.Text.verticalOverflow = VerticalWrapMode.Overflow;
                paint.Text.color = state.textColor * contentColor;
                paint.Text.text = content.text;
                paint.Text.gameObject.SetActive(true);
            }
        }

        private static void Align(ref Rect rect, Rect container, TextAnchor anchor)
        {
            var column = (int)anchor % 3;
            var row = (int)anchor / 3;
            rect.x += (container.width - rect.width) * column / 2;
            rect.y += (container.height - rect.height) * row / 2;
        }

        private void DrawTexture(Rect position, Texture texture, Rect uv, Color color)
        {
            var screen = ScreenRect(position);
            var clip = CurrentClip();
            if (clip.width <= 0 || clip.height <= 0)
                return;
            var paint = NextPaint(clip);
            Place(paint.Texture.rectTransform, new Rect(screen.x - clip.x, screen.y - clip.y, screen.width, screen.height));
            paint.Texture.texture = texture;
            paint.Texture.uvRect = uv;
            paint.Texture.color = color;
            paint.Texture.gameObject.SetActive(true);
        }

        private void DrawSelection(Paint paint, Rect clip, GUIStyle style, Rect position, GUIContent content, int first, int last)
        {
            var start = Math.Max(0, Math.Min(first, last));
            var end = Math.Min(content.text.Length, Math.Max(first, last));
            var lineHeight = style.lineHeight;
            var count = 0;
            for (var i = start; i < end; i++)
            {
                var left = style.GetCursorPixelPosition(position, content, i);
                var right = style.GetCursorPixelPosition(position, content, i + 1);
                var width = right.y > left.y ? position.xMax - style.padding.right - left.x : right.x - left.x;
                if (width > 0)
                {
                    Image selection;
                    if (count < paint.Selection.Count)
                        selection = paint.Selection[count];
                    else
                    {
                        selection = RectObject(paint.Clip, "Selection").gameObject.AddComponent<Image>();
                        selection.raycastTarget = false;
                        paint.Selection.Add(selection);
                    }
                    count++;
                    var screen = ScreenRect(new Rect(left.x, left.y, width, lineHeight));
                    Place(selection.rectTransform, new Rect(screen.x - clip.x, screen.y - clip.y, screen.width, screen.height));
                    selection.color = GUI.skin.settings.selectionColor * GUI.color;
                    selection.gameObject.SetActive(true);
                }
            }
            paint.Text.transform.SetAsLastSibling();
        }

        private void DrawCaret(GUIStyle style, Rect position, GUIContent content, int index)
        {
            var flash = GUI.skin.settings.cursorFlashSpeed;
            if (flash > 0 && Time.realtimeSinceStartup % flash > flash / 2)
                return;
            var point = style.GetCursorPixelPosition(position, content, Math.Min(index, content.text.Length));
            DrawTexture(new Rect(point.x, point.y, 1, style.lineHeight), Texture2D.whiteTexture,
                new Rect(0, 0, 1, 1), GUI.skin.settings.cursorColor * GUI.color);
        }

        public void Dispose()
        {
            if (current == this)
                current = null;
            if (harmony != null)
                harmony.UnpatchAll(PatchId);
            foreach (var sprite in sprites.Values)
                if (sprite) UnityEngine.Object.Destroy(sprite);
            sprites.Clear();
            if (canvasRoot) UnityEngine.Object.Destroy(canvasRoot.gameObject);
            if (fallbackFont) UnityEngine.Object.Destroy(fallbackFont);
        }
    }
}
