using System;
using UnityEngine;
using UnityEngine.UI;

namespace BOW.Game
{
    /// <summary>"Label   &lt;  value  &gt;" selector row. With no step action it shows the value read-only.</summary>
    public sealed class OptionRow
    {
        private readonly RectTransform _root;
        private readonly Text _label;
        private readonly Text _value;
        private readonly Func<string> _get;
        private readonly MenuButton _left, _right;

        public event Action Changed;

        /// <summary>
        /// Label on the left; on the right a recessed pill holding the value, with chevron buttons at each end.
        /// </summary>
        public OptionRow(Transform parent, string label, float x, float y, Func<string> get, Action<int> step)
        {
            _get = get;
            _root = UiFactory.Rect(label, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(720f, 66f));
            _label = MenuStyle.FieldLabel(UiFactory.Rect("Label", _root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(330f, 60f)),
                label, 32);

            var pill = UiFactory.Rect("Pill", _root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(340f, 0f), new Vector2(380f, 66f));
            MenuStyle.Recess(pill, new Color(0.02f, 0.04f, 0.08f, 0.85f)).raycastTarget = false;
            MenuStyle.Outline(pill, new Color(0.35f, 0.5f, 0.68f, 0.45f));

            var mid = new Vector2(0f, 0.5f);
            _left = MenuStyle.Make(_root, "", MenuIcon.Back, MenuStyle.Kind.Quiet, mid, new Vector2(376f, 0f), new Vector2(64f, 56f), () => { step?.Invoke(-1); Changed?.Invoke(); });
            _value = UiFactory.Text(UiFactory.Rect("Value", _root, mid, new Vector2(0.5f, 0.5f), new Vector2(530f, 0f), new Vector2(230f, 60f)),
                "", 32, MenuStyle.Cyan, TextAnchor.MiddleCenter, outline: false);
            UiFactory.UseDisplayFont(_value);
            _right = MenuStyle.Make(_root, "", MenuIcon.Next, MenuStyle.Kind.Quiet, mid, new Vector2(684f, 0f), new Vector2(64f, 56f), () => { step?.Invoke(1); Changed?.Invoke(); });
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
