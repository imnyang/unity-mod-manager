using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace UnityModManagerNet
{
    internal static class UGUITheme
    {
        internal const int ControlHeight = 30;
        internal static readonly Color Window = new Color(.10f, .105f, .115f);
        internal static readonly Color Panel = new Color(.13f, .138f, .15f);
        internal static readonly Color Control = new Color(.18f, .19f, .207f);
        internal static readonly Color Hover = new Color(.22f, .235f, .25f);
        internal static readonly Color Pressed = new Color(.14f, .15f, .165f);
        internal static readonly Color Disabled = new Color(.12f, .127f, .137f);
        internal static readonly Color Input = new Color(.075f, .08f, .09f);
        internal static readonly Color Track = new Color(.20f, .21f, .23f);
        internal static readonly Color Accent = new Color(.59f, .66f, .73f);
        internal static readonly Color Text = new Color(.86f, .88f, .90f);
        internal static readonly Color Muted = new Color(.56f, .59f, .63f);
        internal static readonly Color Border = new Color(.25f, .26f, .29f, .65f);
        internal static readonly Color Success = new Color(.52f, .72f, .52f);
        internal static readonly Color Warning = new Color(.82f, .68f, .41f);
        internal static readonly Color Error = new Color(.85f, .48f, .48f);
        private static readonly Dictionary<int, Sprite> roundedSprites = new Dictionary<int, Sprite>();
        private static readonly PropertyInfo selectedColorProperty = typeof(ColorBlock).GetProperty("selectedColor");
        
        internal static void Shape(Image image, float radius = 2, bool border = false)
        {
            if (radius <= 0)
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
            }
            else
            {
                var r = Math.Max(1, (int)Math.Ceiling(radius));
                Sprite sprite;
                if (!roundedSprites.TryGetValue(r, out sprite) || !sprite)
                {
                    var size = r * 2 + 4;
                    var pixels = new Color[size * size];
                    for (var y = 0; y < size; y++)
                        for (var x = 0; x < size; x++)
                        {
                            var dx = Math.Max(0, r - Math.Min(x + .5f, size - x - .5f));
                            var dy = Math.Max(0, r - Math.Min(y + .5f, size - y - .5f));
                            var alpha = Mathf.Clamp01(r + .5f - (float)Math.Sqrt(dx * dx + dy * dy));
                            pixels[y * size + x] = new Color(1, 1, 1, alpha);
                        }
                    var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    texture.name = "UMM rounded " + r;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.filterMode = FilterMode.Bilinear;
                    texture.SetPixels(pixels);
                    texture.Apply(false, true);
                    sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f),
                        100, 0, SpriteMeshType.FullRect, new Vector4(r + 1, r + 1, r + 1, r + 1));
                    roundedSprites[r] = sprite;
                }
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            var outline = image.GetComponent<Outline>();
            if (border && !outline)
                outline = image.gameObject.AddComponent<Outline>();
            if (outline)
            {
                outline.enabled = border;
                outline.effectColor = Border;
                outline.effectDistance = new Vector2(1, -1);
                outline.useGraphicAlpha = true;
            }
        }

        internal static void Dispose()
        {
            foreach (var sprite in roundedSprites.Values)
                if (sprite)
                {
                    UnityEngine.Object.Destroy(sprite.texture);
                    UnityEngine.Object.Destroy(sprite);
                }
            roundedSprites.Clear();
        }

        internal static Color ControlColor(bool hover, bool active, bool on, bool enabled)
        {
            return !enabled ? Disabled : active || on ? Pressed : hover ? Hover : Control;
        }

        internal static void Apply(Selectable selectable, bool input = false, bool primary = false, bool quiet = false)
        {
            selectable.colors = Colors(selectable.colors, input, primary, quiet);
        }

        internal static ColorBlock Colors(ColorBlock colors, bool input = false, bool primary = false, bool quiet = false)
        {
            colors.normalColor = primary ? new Color(.23f, .25f, .28f) : quiet ? Color.clear : input ? Input : Control;
            colors.highlightedColor = primary ? new Color(.28f, .30f, .33f) : Hover;
            colors.pressedColor = Pressed;
            colors.disabledColor = Disabled;
            colors.colorMultiplier = 1;
            colors.fadeDuration = .12f;
            return WithSelectedColor(colors, input ? Input : Pressed);
        }

        internal static ColorBlock WithSelectedColor(ColorBlock colors, Color selected)
        {
            if (selectedColorProperty != null)
            {
                object boxed = colors;
                selectedColorProperty.SetValue(boxed, selected, null);
                colors = (ColorBlock)boxed;
            }
            return colors;
        }

        internal static Rect CheckboxRect(Rect bounds, float scale)
        {
            var size = Math.Max(0, Math.Min(20 * scale, Math.Min(bounds.width, bounds.height)));
            var inset = Math.Min(3 * scale, Math.Max(0, bounds.width - size));
            return new Rect(bounds.x + inset, bounds.y + (bounds.height - size) / 2, size, size);
        }
    }
}
