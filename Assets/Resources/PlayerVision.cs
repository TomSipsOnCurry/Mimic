using UnityEngine;
using System.Collections.Generic;

public class PlayerVision : MonoBehaviour
{
    public float viewRadius = 1f;
    public int rayCount = 360;
    public LayerMask obstacleMask;

    private Mesh mesh;

    void Start()
    {
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;

        // Assign your wall layer here
        obstacleMask = LayerMask.GetMask("Grid/Walls");
    }

    void LateUpdate()
    {
        GenerateVisionMesh();
        Debug.Log("Vertices: " + mesh.vertexCount);
        Debug.DrawLine(transform.position, transform.position + Vector3.right * 3, Color.red);


    }

    void GenerateVisionMesh()
    {
        List<Vector3> points = new List<Vector3>();
        float angleStep = 360f / rayCount;

        for (int i = 0; i < rayCount; i++)
        {
            float angle = angleStep * i;
            Vector3 dir = DirFromAngle(angle);

            RaycastHit2D hit = Physics2D.Raycast(transform.position, dir, viewRadius, obstacleMask);

            if (hit.collider != null)
                points.Add(hit.point - (Vector2)transform.position);
            else
                points.Add(dir * viewRadius);
        }

        BuildMesh(points);
    }

    void BuildMesh(List<Vector3> points)
    {
        int count = points.Count;

        Vector3[] vertices = new Vector3[count + 1];
        int[] triangles = new int[count * 3];

        vertices[0] = Vector3.zero;

        for (int i = 0; i < count; i++)
        {
            vertices[i + 1] = points[i];

            if (i < count - 1)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }
        }

        triangles[(count - 1) * 3] = 0;
        triangles[(count - 1) * 3 + 1] = count;
        triangles[(count - 1) * 3 + 2] = 1;

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
    }

    Vector3 DirFromAngle(float angle)
    {
        float rad = angle * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(rad), Mathf.Sin(rad));
    }
}
