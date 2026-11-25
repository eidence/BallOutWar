using System;
using UnityEngine;
using UnityEngine.UI;

namespace BOW.Game
{
    /// <summary>Shared menu colours.</summary>
    public static class MenuTheme
    {
        public static readonly Color Cyan = new Color(0.25f, 0.95f, 1f);
        public static readonly Color Panel = new Color(0.03f, 0.05f, 0.09f, 0.88f);
        public static readonly Color ButtonBlue = new Color(0.08f, 0.45f, 0.65f);
        public static readonly Color ButtonGrey = new Color(0.18f, 0.2f, 0.26f);
        public static readonly Color ButtonGreen = new Color(0.1f, 0.65f, 0.35f);
        public static readonly Color ButtonRed = new Color(0.7f, 0.2f, 0.2f);
    }

    /// <summary>"Label   &lt;  value  &gt;" selector row. With no step action it shows the value read-only.</summary>
    public sealed class OptionRow
    {
        private readonly RectTransform _root;
        private readonly Text _label;
        private readonly Text _value;
        private readonly Func<string> _get;
        private readonly Button _left, _right;

        public event Action Changed;

        public OptionRow(Transform parent, string label, float x, float y, Func<string> get, Action<int> step)
        {
            _get = get;
            _root = UiFactory.Rect(label, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(720f, 66f));
            _label = UiFactory.Text(UiFactory.Rect("Label", _root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(330f, 60f)),
                label, 32, Color.white, TextAnchor.MiddleLeft);
            _left = UiFactory.Button(_root, "<", new Vector2(0f, 0.5f), new Vector2(370f, 0f), new Vector2(64f, 60f), MenuTheme.ButtonGrey, () => { step?.Invoke(-1); Changed?.Invoke(); });
            _value = UiFactory.Text(UiFactory.Rect("Value", _root, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(530f, 0f), new Vector2(240f, 60f)),
                "", 32, MenuTheme.Cyan);
            _right = UiFactory.Button(_root, ">", new Vector2(0f, 0.5f), new Vector2(690f, 0f), new Vector2(64f, 60f), MenuTheme.ButtonGrey, () => { step?.Invoke(1); Changed?.Invoke(); });
            SetEditable(step != null);
        }

        public void Refresh() => _value.text = _get();
        public void SetLabel(string text) => _label.text = text;
        public void SetVisible(bool visible) => _root.gameObject.SetActive(visible);

        /// <summary>Hides the arrows, for players who can see but not change an option (guests in a lobby).</summary>
        public void SetEditable(bool editable)
        {
            _left.gameObject.SetActive(editable);
            _right.gameObject.SetActive(editable);
        }
    }
}
