using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BOW.Game
{
    /// <summary>Small helpers for building uGUI screens in code (prototype; screens move to prefabs later).</summary>
    public static class UiFactory
    {
        private static Font _font;

        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            EnsureEventSystem();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(string name, Transform parent)
        {
            var rt = Rect(name, parent, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Image(RectTransform rt, Color color, Sprite sprite = null)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite != null ? sprite : ProceduralTextures.White();
            image.color = color;
            return image;
        }

        public static Text Text(RectTransform rt, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, bool outline = true)
        {
            var label = rt.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = FontStyle.Bold;
            label.color = color;
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            if (outline)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0f, 0f, 0f, 0.85f);
                o.effectDistance = new Vector2(2f, -2f);
            }
            return label;
        }

        public static Button Button(Transform parent, string label, Vector2 anchor, Vector2 position, Vector2 size, Color color, Action onClick)
        {
            var rt = Rect(label, parent, anchor, new Vector2(0.5f, 0.5f), position, size);
            var image = Image(rt, color);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => { GameAudio.Play(Sfx.Click); onClick(); });
            Text(Stretch("Label", rt), label, Mathf.RoundToInt(size.y * 0.42f), Color.white);
            return button;
        }
    }

    public static class UiInput
    {
        /// <summary>A single-line text box (opens the on-screen keyboard on phones).</summary>
        public static InputField Create(Transform parent, Vector2 anchor, Vector2 position, Vector2 size, string placeholder, string value,
            InputField.ContentType type = InputField.ContentType.Standard, int maxLength = 32)
        {
            var rt = UiFactory.Rect("Input", parent, anchor, new Vector2(0.5f, 0.5f), position, size);
            var background = UiFactory.Image(rt, new Color(0.1f, 0.12f, 0.17f, 0.95f));
            var input = rt.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;

            int fontSize = Mathf.RoundToInt(size.y * 0.45f);
            var textRect = UiFactory.Stretch("Text", rt);
            textRect.offsetMin = new Vector2(18f, 0f);
            textRect.offsetMax = new Vector2(-18f, 0f);
            var text = UiFactory.Text(textRect, "", fontSize, Color.white, TextAnchor.MiddleLeft, outline: false);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;

            var placeholderRect = UiFactory.Stretch("Placeholder", rt);
            placeholderRect.offsetMin = textRect.offsetMin;
            placeholderRect.offsetMax = textRect.offsetMax;
            var hint = UiFactory.Text(placeholderRect, placeholder, fontSize, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft, outline: false);
            hint.fontStyle = FontStyle.Italic;

            input.textComponent = text;
            input.placeholder = hint;
            input.contentType = type;
            input.characterLimit = maxLength;
            input.text = value ?? "";
            return input;
        }
    }

    /// <summary>Fires on touch-down rather than release, so a blast is never late (US-2.3).</summary>
    public sealed class PressButton : MonoBehaviour, IPointerDownHandler
    {
        public event Action Pressed;

        public void OnPointerDown(PointerEventData eventData) => Pressed?.Invoke();
    }
}
