using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace GameNet.Gameplay
{
    // 只绘制家具朝向镜头的边缘，让坐下/躺下的人物进入家具前后两层之间。
    [DefaultExecutionOrder(200)]
    public sealed class DukeFurnitureForeground : MaskableGraphic
    {
        Image source;
        Transform character;
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

        public void Configure(Image image, Transform duke, bool sitting, bool sleeping)
        {
            source = image;
            character = duke;
            Vector2[] next = sitting ? SofaArm : sleeping ? BedFoot : null;
            if (outline != next) { outline = next; SetVerticesDirty(); }
            enabled = next != null;
            raycastTarget = false;
        }

        void LateUpdate()
        {
            if (character == null) return;
            Canvas characterCanvas = character.GetComponent<Canvas>();
            if (ownCanvas == null) ownCanvas = gameObject.AddComponent<Canvas>();
            ownCanvas.overrideSorting = true;
            ownCanvas.sortingOrder = characterCanvas != null ? characterCanvas.sortingOrder + 1 : -99;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (outline == null || source == null || source.sprite == null) return;
            Rect rect = rectTransform.rect;
            float aspect = source.sprite.rect.width / source.sprite.rect.height;
            float width = rect.width, height = rect.height;
            if (source.preserveAspect)
            {
                width = Mathf.Min(width, height * aspect);
                height = Mathf.Min(height, width / aspect);
            }
            float left = rect.xMin + (rect.width - width) * rectTransform.pivot.x;
            float top = rect.yMax - (rect.height - height) * (1f - rectTransform.pivot.y);
            Vector4 uv = DataUtility.GetOuterUV(source.sprite);
            for (int i = 0; i < outline.Length; i++)
            {
                Vector2 p = outline[i];
                vh.AddVert(new Vector3(left + p.x * width, top - p.y * height), color,
                    new Vector2(Mathf.Lerp(uv.x, uv.z, p.x), Mathf.Lerp(uv.y, uv.w, 1f - p.y)));
            }
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }
    }
}
