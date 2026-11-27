using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    // 只绘制家具朝向镜头的边缘，让坐下/躺下的人物进入家具前后两层之间。
    [RequireComponent(typeof(CanvasRenderer))]
    [DefaultExecutionOrder(200)]
    public sealed class DukeFurnitureForeground : MaskableGraphic
    {
        Image source;
        Canvas ownCanvas;
        Vector2[] outline;
        static readonly Vector2[] SofaArm = {
            new Vector2(.503f, .740f), new Vector2(.533f, .740f),
            new Vector2(.534f, .779f), new Vector2(.501f, .779f) };
        static readonly Vector2[] BedFoot = {
            new Vector2(.654f, .508f), new Vector2(.728f, .508f),
            new Vector2(.730f, .525f), new Vector2(.652f, .525f) };

        public override Texture mainTexture => source != null && source.sprite != null
            ? source.sprite.texture : base.mainTexture;

        public void Configure(Image image, bool sitting, bool sleeping, Vector2[] customOutline = null)
        {
            source = image;
            Vector2[] next = customOutline != null && customOutline.Length >= 3 ? customOutline
                : sitting ? SofaArm : sleeping ? BedFoot : null;
            if (outline != next) { outline = next; SetVerticesDirty(); }
            enabled = next != null;
            raycastTarget = false;
        }

        void LateUpdate()
        {
            if (source == null) return;
            if (ownCanvas == null) ownCanvas = gameObject.AddComponent<Canvas>();
            ownCanvas.overrideSorting = true;
            // 家具前缘覆盖人物，仍在现有 HUD（0）下面。
            ownCanvas.sortingOrder = -1;
        }

        internal static Rect GetArtworkRect(Image image)
        {
            Rect rect = image.GetPixelAdjustedRect();
            if (!image.preserveAspect || image.sprite == null) return rect;
            float aspect = image.sprite.rect.width / image.sprite.rect.height;
            float width = rect.width, height = rect.height;
            width = Mathf.Min(width, height * aspect);
            height = Mathf.Min(height, width / aspect);
            Vector2 pivot = image.rectTransform.pivot;
            return new Rect(rect.xMin + (rect.width - width) * pivot.x,
                rect.yMin + (rect.height - height) * pivot.y, width, height);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (outline == null || source == null || source.sprite == null) return;
            Rect art = GetArtworkRect(source);
            Vector4 uv = DataUtility.GetOuterUV(source.sprite);
            Vector2 center = Vector2.zero;
            for (int i = 0; i < outline.Length; i++)
                center += outline[i];
            AddVertex(center / outline.Length);
            for (int i = 0; i < outline.Length; i++)
                AddVertex(outline[i]);
            for (int i = 0; i < outline.Length; i++)
                vh.AddTriangle(0, i + 1, (i + 1) % outline.Length + 1);

            void AddVertex(Vector2 point)
            {
                Vector2 local = new Vector2(art.xMin + point.x * art.width, art.yMax - point.y * art.height);
                Vector3 position = rectTransform.InverseTransformPoint(source.rectTransform.TransformPoint(local));
                vh.AddVert(position, color, new Vector2(
                    Mathf.Lerp(uv.x, uv.z, point.x), Mathf.Lerp(uv.y, uv.w, 1f - point.y)));
            }
        }
    }
}
