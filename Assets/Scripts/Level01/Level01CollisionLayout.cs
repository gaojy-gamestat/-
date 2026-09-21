using System;
using UnityEngine;

/// <summary>
/// First-level collision authoring for the existing Screen Space Overlay house image.
///
/// The background is a UI Image, while the player is a 3D CharacterController.  The
/// collision objects therefore stay in world space and are baked from the image's
/// source-pixel rectangles through the selected level camera.  This keeps the mapping
/// explicit instead of treating UI/local pixels as world coordinates.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-1000)]
public sealed class Level01CollisionLayout : MonoBehaviour
{
    [Serializable]
    private struct PixelBox
    {
        public string name;
        public string group;
        public Vector2 topLeft;
        public Vector2 bottomRight;
        public float zCenter;
        public float zSize;

        public PixelBox(string name, string group, Vector2 topLeft, Vector2 bottomRight, float zCenter, float zSize)
        {
            this.name = name;
            this.group = group;
            this.topLeft = topLeft;
            this.bottomRight = bottomRight;
            this.zCenter = zCenter;
            this.zSize = zSize;
        }
    }

    [Header("Background-to-world calibration")]
    [SerializeField] private RectTransform backgroundImage;
    [SerializeField] private Camera projectionCamera;
    [SerializeField] private Vector2 sourceTextureSize = new Vector2(1677f, 938f);
    [SerializeField] private Vector2 referenceResolution = new Vector2(1440f, 810f);
    [SerializeField] private float collisionDepthFromCamera = 10f;
    [SerializeField] private bool applyOnPlay = true;

    [Header("Debug")]
    [SerializeField] private bool drawMappingGizmos = true;

    private const string GroundGroupName = "Ground";
    private const string WallsGroupName = "Walls";
    private const string DoorsGroupName = "Doors";
    private const string PlatformsGroupName = "Platforms";

    // Source-image pixels, origin at the top-left.  These are intentionally kept in
    // image space so a reviewer can compare them with the reference artwork directly.
    private static readonly PixelBox[] Layout =
    {
        // Standable surfaces: each room remains an independent slab.
        new PixelBox("Ground_LivingRoom", GroundGroupName, new Vector2(445f, 835f), new Vector2(902f, 866f), 0f, 7f),
        new PixelBox("Ground_Hallway", GroundGroupName, new Vector2(902f, 835f), new Vector2(1058f, 866f), 0f, 7f),
        new PixelBox("Ground_Kitchen", GroundGroupName, new Vector2(1058f, 835f), new Vector2(1592f, 866f), 0f, 7f),
        new PixelBox("Platform_UpperBedroom", PlatformsGroupName, new Vector2(956f, 520f), new Vector2(1394f, 551f), 0f, 7f),

        // Exterior walls and the two depth limits.  The depth limits are at the front
        // and back edges; they do not fill the rooms' walkable volume.
        new PixelBox("OuterWall_Left", WallsGroupName, new Vector2(414f, 106f), new Vector2(445f, 866f), 0f, 7f),
        new PixelBox("OuterWall_Right", WallsGroupName, new Vector2(1592f, 106f), new Vector2(1623f, 866f), 0f, 7f),
        new PixelBox("DepthBoundary_Front", WallsGroupName, new Vector2(414f, 106f), new Vector2(1623f, 866f), -3.5f, 0.25f),
        new PixelBox("DepthBoundary_Back", WallsGroupName, new Vector2(414f, 106f), new Vector2(1623f, 866f), 3.5f, 0.25f),

        // Bottom-floor dividers are headers above the real doorway height.  The lower
        // gaps remain open so a CharacterController can pass through the intended doors.
        new PixelBox("Wall_LivingHall_AboveDoor", WallsGroupName, new Vector2(888f, 538f), new Vector2(918f, 635f), 0f, 7f),
        new PixelBox("Wall_HallKitchen_AboveDoor", WallsGroupName, new Vector2(1042f, 538f), new Vector2(1072f, 635f), 0f, 7f),

        // The upper bedroom is an independent level.  Its side walls do not create a
        // route through the floor; a future stair/ladder can be added explicitly.
        new PixelBox("UpperBedroomWall_Left", WallsGroupName, new Vector2(939f, 313f), new Vector2(970f, 520f), 0f, 7f),
        new PixelBox("UpperBedroomWall_Right", WallsGroupName, new Vector2(1380f, 313f), new Vector2(1411f, 520f), 0f, 7f),
    };

    private static readonly (string name, Vector2 sourcePixel)[] DoorMarkers =
    {
        ("Doorway_LivingToHall", new Vector2(920f, 810f)),
        ("Doorway_HallToKitchen", new Vector2(1030f, 810f)),
        ("Doorway_HallToUpperBedroom", new Vector2(1180f, 535f)),
    };

    private void Awake()
    {
        if (Application.isPlaying && applyOnPlay)
        {
            RebuildLayout();
        }
    }

    /// <summary>Rebuilds the serialized BoxCollider layout from the calibration data.</summary>
    [ContextMenu("Rebuild Level 01 Collision Layout")]
    public void RebuildLayout()
    {
        if (!ValidateReferences())
        {
            return;
        }

        Transform ground = GetOrCreateGroup(GroundGroupName);
        Transform walls = GetOrCreateGroup(WallsGroupName);
        Transform doors = GetOrCreateGroup(DoorsGroupName);
        Transform platforms = GetOrCreateGroup(PlatformsGroupName);

        foreach (PixelBox layout in Layout)
        {
            Transform parent = layout.group switch
            {
                GroundGroupName => ground,
                WallsGroupName => walls,
                PlatformsGroupName => platforms,
                _ => walls,
            };

            ApplyBox(parent, layout);
        }

        foreach ((string name, Vector2 sourcePixel) in DoorMarkers)
        {
            Transform marker = GetOrCreateChild(doors, name);
            Vector3 markerPosition = SourcePixelToWorld(sourcePixel, collisionDepthFromCamera);
            marker.localPosition = new Vector3(markerPosition.x, markerPosition.y, 0f);
            marker.localRotation = Quaternion.identity;
            marker.localScale = Vector3.one;
        }

        if (Application.isPlaying)
        {
            Physics.SyncTransforms();
        }
    }

    private void ApplyBox(Transform parent, PixelBox layout)
    {
        Transform child = GetOrCreateChild(parent, layout.name);
        Vector3 topLeft = SourcePixelToWorld(layout.topLeft, collisionDepthFromCamera);
        Vector3 bottomRight = SourcePixelToWorld(layout.bottomRight, collisionDepthFromCamera);
        Vector3 center = (topLeft + bottomRight) * 0.5f;
        Vector3 size = new Vector3(
            Mathf.Abs(bottomRight.x - topLeft.x),
            Mathf.Abs(bottomRight.y - topLeft.y),
            layout.zSize);

        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        child.localPosition = new Vector3(center.x, center.y, layout.zCenter);

        BoxCollider collider = child.GetComponent<BoxCollider>();
        if (collider == null)
        {
            collider = child.gameObject.AddComponent<BoxCollider>();
        }

        collider.center = Vector3.zero;
        collider.size = size;
        collider.isTrigger = false;
        collider.enabled = true;
    }

    private bool ValidateReferences()
    {
        if (backgroundImage == null)
        {
            Debug.LogError("[Level01CollisionLayout] Background Image is not assigned.", this);
            return false;
        }

        if (projectionCamera == null)
        {
            Debug.LogError("[Level01CollisionLayout] Projection Camera is not assigned.", this);
            return false;
        }

        if (sourceTextureSize.x <= 0f || sourceTextureSize.y <= 0f || referenceResolution.x <= 0f || referenceResolution.y <= 0f)
        {
            Debug.LogError("[Level01CollisionLayout] Calibration dimensions must be positive.", this);
            return false;
        }

        return true;
    }

    private Transform GetOrCreateGroup(string groupName)
    {
        return GetOrCreateChild(transform, groupName);
    }

    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new GameObject(childName);
        child = childObject.transform;
        child.SetParent(parent, false);
        return child;
    }

    private Vector3 SourcePixelToWorld(Vector2 sourcePixel, float depth)
    {
        Vector2 imageLocal = new Vector2(
            (sourcePixel.x / sourceTextureSize.x - 0.5f) * backgroundImage.rect.width,
            (0.5f - sourcePixel.y / sourceTextureSize.y) * backgroundImage.rect.height);

        // The scene uses a centered, constant-pixel Screen Space Overlay image.  At
        // runtime the image remains centered while the camera projection uses the real
        // viewport.  In batchmode/editor baking we use the declared reference viewport.
        float screenWidth = referenceResolution.x;
        float screenHeight = referenceResolution.y;
        if (Application.isPlaying && Screen.width > 0 && Screen.height > 0)
        {
            screenWidth = Screen.width;
            screenHeight = Screen.height;
        }

        Canvas canvas = backgroundImage.GetComponentInParent<Canvas>();
        float canvasScale = canvas != null ? canvas.scaleFactor : 1f;
        Vector2 screenPoint = new Vector2(
            screenWidth * 0.5f + imageLocal.x * canvasScale,
            screenHeight * 0.5f + imageLocal.y * canvasScale);

        float aspect = screenWidth / screenHeight;
        Vector3 cameraLocalPoint;
        if (projectionCamera.orthographic)
        {
            float halfHeight = projectionCamera.orthographicSize;
            cameraLocalPoint = new Vector3(
                (screenPoint.x / screenWidth - 0.5f) * halfHeight * 2f * aspect,
                (screenPoint.y / screenHeight - 0.5f) * halfHeight * 2f,
                depth);
        }
        else
        {
            float halfHeight = depth * Mathf.Tan(projectionCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            cameraLocalPoint = new Vector3(
                (screenPoint.x / screenWidth - 0.5f) * halfHeight * 2f * aspect,
                (screenPoint.y / screenHeight - 0.5f) * halfHeight * 2f,
                depth);
        }

        return projectionCamera.transform.TransformPoint(cameraLocalPoint);
    }

    private void OnDrawGizmos()
    {
        if (!drawMappingGizmos || !enabled)
        {
            return;
        }

        Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.9f);
        foreach (Transform child in transform)
        {
            if (child.name == GroundGroupName || child.name == PlatformsGroupName)
            {
                DrawGroupGizmos(child);
            }
        }

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.85f);
        Transform wallGroup = transform.Find(WallsGroupName);
        if (wallGroup != null)
        {
            DrawGroupGizmos(wallGroup);
        }
    }

    private static void DrawGroupGizmos(Transform group)
    {
        foreach (BoxCollider collider in group.GetComponentsInChildren<BoxCollider>())
        {
            Gizmos.matrix = collider.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(collider.center, collider.size);
        }

        Gizmos.matrix = Matrix4x4.identity;
    }
}
