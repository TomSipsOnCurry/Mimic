using UnityEngine;
using UnityEngine.UI;

public class ParallaxScroll : MonoBehaviour
{
    public float speed = 0.1f;
    private RawImage img;
    private float offset;

    void Awake()
    {
        img = GetComponent<RawImage>();
    }

    void Update()
    {
        offset += speed * Time.deltaTime;
        img.uvRect = new Rect(offset, 0f, 1f, 1f);
    }
}