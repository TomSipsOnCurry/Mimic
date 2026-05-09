using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

public class PlayerVision : MonoBehaviour
{
    [Header("Vision Settings")]
    [Min(0.1f)]
    public float viewRadius = 8.5f;
    [Range(32, 1440)]
    public int rayCount = 360;
    [Range(0f, 1f)]
    public float shadowOpacity = 1f;
    [Min(0f)]
    public float edgeFadeDistance = 2.25f;

    [Header("Coverage")]
    [SerializeField] private float cameraCoveragePadding = 3f;

    [Header("Rendering")]
    [SerializeField] private Material shadowMaterial;
    [SerializeField] private int sortingOrder = 500;

    [Header("Layer")]
    public LayerMask obstacleMask;

    private const string ShadowShaderName = "Mimic/VisionShadow";
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<int> triangles = new List<int>();
    private readonly List<Color32> colors = new List<Color32>();

    private Mesh mesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Material runtimeShadowMaterial;
    private PhotonView ownerPhotonView;

    private void Awake()
    {
        ownerPhotonView = GetComponentInParent<PhotonView>();
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();

        if (meshFilter == null)
        {
            Debug.LogError("PlayerVision: No MeshFilter found on this GameObject.");
            enabled = false;
            return;
        }

        if (meshRenderer == null)
        {
            Debug.LogError("PlayerVision: No MeshRenderer found on this GameObject.");
            enabled = false;
            return;
        }

        mesh = new Mesh
        {
            name = "PlayerVisionShadowMesh"
        };
        mesh.MarkDynamic();
        meshFilter.sharedMesh = mesh;

        meshRenderer.sharedMaterial = ResolveShadowMaterial();
        meshRenderer.sortingLayerName = "Default";
        meshRenderer.sortingOrder = sortingOrder;
    }

    private void LateUpdate()
    {
        bool shouldRender = ShouldRenderForThisPlayer();
        if (meshRenderer.enabled != shouldRender)
        {
            meshRenderer.enabled = shouldRender;
        }

        if (!shouldRender)
        {
            return;
        }

        UpdateMaterialColor();
        GenerateShadowMesh();
    }

    private bool ShouldRenderForThisPlayer()
    {
        if (ownerPhotonView == null)
        {
            ownerPhotonView = GetComponentInParent<PhotonView>();
        }

        return ownerPhotonView == null || ownerPhotonView.IsMine;
    }

    private Material ResolveShadowMaterial()
    {
        if (shadowMaterial != null)
        {
            runtimeShadowMaterial = new Material(shadowMaterial)
            {
                name = "Runtime Player Vision Shadow",
                hideFlags = HideFlags.HideAndDontSave
            };

            return runtimeShadowMaterial;
        }

        Shader shader = Shader.Find(ShadowShaderName);
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader == null)
        {
            Debug.LogError("PlayerVision: No compatible shadow shader found.");
            return null;
        }

        runtimeShadowMaterial = new Material(shader)
        {
            name = "Runtime Player Vision Shadow",
            hideFlags = HideFlags.HideAndDontSave
        };

        return runtimeShadowMaterial;
    }

    private void UpdateMaterialColor()
    {
        Material material = meshRenderer.sharedMaterial;
        if (material == null)
        {
            return;
        }

        Color shadowColor = new Color(0f, 0f, 0f, shadowOpacity);
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", shadowColor);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", shadowColor);
        }
    }

    private void GenerateShadowMesh()
    {
        int segmentCount = Mathf.Max(32, rayCount);
        float outerRadius = GetCoverageRadius();
        float angleStep = 360f / segmentCount;

        vertices.Clear();
        triangles.Clear();
        colors.Clear();

        Vector2 firstFade = Vector2.zero;
        Vector2 firstVisible = Vector2.zero;
        Vector2 firstOuter = Vector2.zero;
        Vector2 previousFade = Vector2.zero;
        Vector2 previousVisible = Vector2.zero;
        Vector2 previousOuter = Vector2.zero;

        for (int i = 0; i <= segmentCount; i++)
        {
            float angle = angleStep * i;
            Vector2 direction = DirectionFromAngle(angle);
            VisionRay ray = CastVisionRay(direction);
            Vector2 fadePoint = direction * ray.fadeStartDistance;
            Vector2 visiblePoint = direction * ray.shadowStartDistance;
            Vector2 outerPoint = direction * outerRadius;

            if (i == 0)
            {
                firstFade = fadePoint;
                firstVisible = visiblePoint;
                firstOuter = outerPoint;
                previousFade = fadePoint;
                previousVisible = visiblePoint;
                previousOuter = outerPoint;
                continue;
            }

            if (i == segmentCount)
            {
                fadePoint = firstFade;
                visiblePoint = firstVisible;
                outerPoint = firstOuter;
            }

            AddShadowQuad(previousFade, previousVisible, fadePoint, visiblePoint, 0, 255);
            AddShadowQuad(previousVisible, previousOuter, visiblePoint, outerPoint, 255, 255);
            previousFade = fadePoint;
            previousVisible = visiblePoint;
            previousOuter = outerPoint;
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    private VisionRay CastVisionRay(Vector2 direction)
    {
        Vector2 origin = transform.position;
        RaycastHit2D hit = Physics2D.Raycast(origin, direction, viewRadius, obstacleMask);
        if (hit.collider != null)
        {
            return new VisionRay(hit.distance, hit.distance);
        }

        float fadeStartDistance = Mathf.Max(0f, viewRadius - edgeFadeDistance);
        return new VisionRay(fadeStartDistance, viewRadius);
    }

    private float GetCoverageRadius()
    {
        float radius = viewRadius + cameraCoveragePadding;
        Camera mainCamera = Camera.main;
        if (mainCamera == null || !mainCamera.orthographic)
        {
            return radius;
        }

        float halfHeight = mainCamera.orthographicSize;
        float halfWidth = halfHeight * mainCamera.aspect;
        float cameraDiagonal = Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight);

        Vector3 cameraOffset = mainCamera.transform.position - transform.position;
        cameraOffset.z = 0f;

        return Mathf.Max(radius, cameraOffset.magnitude + cameraDiagonal + cameraCoveragePadding);
    }

    private void AddShadowQuad(Vector2 innerA, Vector2 outerA, Vector2 innerB, Vector2 outerB, byte innerAlpha, byte outerAlpha)
    {
        int start = vertices.Count;
        vertices.Add(WorldOffsetToLocal(innerA));
        vertices.Add(WorldOffsetToLocal(outerA));
        vertices.Add(WorldOffsetToLocal(outerB));
        vertices.Add(WorldOffsetToLocal(innerB));

        colors.Add(new Color32(255, 255, 255, innerAlpha));
        colors.Add(new Color32(255, 255, 255, outerAlpha));
        colors.Add(new Color32(255, 255, 255, outerAlpha));
        colors.Add(new Color32(255, 255, 255, innerAlpha));

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);

        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private Vector3 WorldOffsetToLocal(Vector2 worldOffset)
    {
        return transform.InverseTransformVector(new Vector3(worldOffset.x, worldOffset.y, 0f));
    }

    private static Vector2 DirectionFromAngle(float angleDegrees)
    {
        float radians = angleDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    private void OnDestroy()
    {
        if (runtimeShadowMaterial != null)
        {
            Destroy(runtimeShadowMaterial);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, viewRadius);
    }

    private readonly struct VisionRay
    {
        public readonly float fadeStartDistance;
        public readonly float shadowStartDistance;

        public VisionRay(float fadeStartDistance, float shadowStartDistance)
        {
            this.fadeStartDistance = fadeStartDistance;
            this.shadowStartDistance = shadowStartDistance;
        }
    }
}
