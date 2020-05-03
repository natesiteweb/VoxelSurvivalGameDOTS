using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class trigtest : MonoBehaviour
{
    public Transform pivot;
    public Transform point;
    public Vector3 pivotPoint;
    public Vector3 rot;
    public float xAngle;

    public bool showDebug = true;
    public bool rotateX = false;
    public bool rotateY = false;
    public bool rotateZ = false;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame

    float currentAngle = 0f;

    void Update()
    {
        currentAngle += Time.deltaTime / 4f;

        Vector3 newPos = new Vector3();
        newPos.x = Mathf.Sin(currentAngle) * 10f;
        newPos.z = Mathf.Cos(currentAngle) * 10f;

        point.position = newPos;

        if (showDebug)
        {
            Debug.DrawRay(point.position, point.rotation * Vector3.up, Color.black);
            Debug.DrawRay(point.position, point.rotation * Vector3.right, Color.black);
            Debug.DrawRay(point.position, point.rotation * Vector3.forward, Color.black);

            Debug.DrawRay(point.position + (point.rotation * pivotPoint), point.rotation * Vector3.up, Color.green);
            Debug.DrawRay(point.position + (point.rotation * pivotPoint), point.rotation * Vector3.right, Color.red);
            Debug.DrawRay(point.position + (point.rotation * pivotPoint), point.rotation * Vector3.forward, Color.blue);
        }
    }
}
