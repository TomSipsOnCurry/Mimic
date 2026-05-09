using UnityEngine;
using System.Collections.Generic;

public class PlayerVision : MonoBehaviour
{
    [Header("Vision Settings")]
    public float viewRadius = 5f;
    [Range(1f, 360f)]
    public float viewAngle = 90f;   // Cone width in degrees
    public int rayCount = 120;      // More = smoother edges

    [Header("Direction")]
    public bool faceMouseCursor = true;  // Set false to use movement direction instead

    [Header("Layer")]
    // Set this in the Inspector to your wall layer — do NOT use "Grid/Walls" (slashes are invalid)
    public LayerMask obstacleMask;

    private Mesh mesh;
    private MeshFilter meshFilter;

    void Start()
    {
        meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            Debug.LogError("PlayerVision: No MeshFilter found on this GameObject!");
            return;
        }

        mesh = new Mesh();
        mesh.name = "VisionMesh";
        meshFilter.mesh = mesh;
        MeshRenderer mr = GetComponent<MeshRenderer>();
        mr.sortingLayerName = "Default"; // match your tilemap's sorting layer
        mr.sortingOrder = 9;
        viewRadius = 5f / transform.lossyScale.x;
    }

    void LateUpdate()
    {
        Vector3 facingDir = GetFacingDirection();
        GenerateVisionMesh(facingDir);
    }

    Vector3 GetFacingDirection()
    {
        if (faceMouseCursor)
        {
            // Face toward the mouse cursor
            Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mouseWorld.z = 0f;
            Vector3 dir = (mouseWorld - transform.position).normalized;
            return dir == Vector3.zero ? Vector3.right : dir;
        }
        else
        {
            // Face based on WASD/joystick input
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 dir = new Vector3(h, v, 0f).normalized;
            return dir == Vector3.zero ? Vector3.right : dir;
        }
    }

    void GenerateVisionMesh(Vector3 facing)
    {
        List<Vector3> points = new List<Vector3>();

        // Starting angle: centre the cone on the facing direction
        float baseAngle = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg;
        float halfAngle = viewAngle * 0.5f;
        float angleStep = viewAngle / rayCount;

        for (int i = 0; i <= rayCount; i++)
        {
            float angle = baseAngle - halfAngle + angleStep * i;
            Vector3 dir = DirFromAngle(angle);

            RaycastHit2D hit = Physics2D.Raycast(transform.position, dir, viewRadius, obstacleMask);

            if (hit.collider != null)
                // Hit a wall — stop the ray there
                points.Add(hit.point - (Vector2)transform.position);
            else
                // No wall — ray reaches full distance
                points.Add(dir * viewRadius);
        }

        BuildMesh(points);
    }

    void BuildMesh(List<Vector3> points)
    {
        int count = points.Count;

        Vector3[] vertices = new Vector3[count + 1];
        int[] triangles = new int[(count - 1) * 3];

        vertices[0] = Vector3.zero; // Origin (player position in local space)

        for (int i = 0; i < count; i++)
            vertices[i + 1] = points[i];

        for (int i = 0; i < count - 1; i++)
        {
            triangles[i * 3]     = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals(); // Needed for lit materials
    }

    Vector3 DirFromAngle(float angleDegrees)
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
    }
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, viewRadius);
    }
}