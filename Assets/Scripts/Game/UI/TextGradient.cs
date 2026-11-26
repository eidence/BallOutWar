using UnityEngine;
using UnityEngine.UI;

namespace BOW.Game
{
    /// <summary>
    /// Shades a Text (or any graphic) from <see cref="Top"/> to <see cref="Bottom"/> down its height, multiplied by the
    /// graphic's own colour, so tinting the text still works. Add it before any Outline or Shadow on the same object,
    /// so those copies keep their own flat colour.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TextGradient : BaseMeshEffect
    {
        public Color Top = Color.white;
        public Color Bottom = Color.white;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var vertex = new UIVertex();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                min = Mathf.Min(min, vertex.position.y);
                max = Mathf.Max(max, vertex.position.y);
            }
            float span = Mathf.Max(0.001f, max - min);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                var shade = Color.Lerp(Bottom, Top, (vertex.position.y - min) / span);
                vertex.color = (Color)vertex.color * shade;
                vh.SetUIVertex(vertex, i);
            }
        }
    }
}
