using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class PlayerControllerv1 : MonoBehaviour
{
    public Transform universe;
    public float originThreshold;
    public float gravity;
    public float mouseSpeed;
    public float walkSpeed;
    public float jumpSpeed;
    public Planet planet;
    public Transform body;
    Rigidbody rb;

    Vector3 lastPlayerPos;

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    void Update()
    {
        //Debug.Log(Vector3.Angle(transform.up, Vector3.up));

        if (Vector3.Distance(transform.position, Vector3.zero) > originThreshold)
        {
            lastPlayerPos = transform.position;
            planet.lastPlayerPos -= lastPlayerPos;

            Vector3 lastPlayerRot = transform.eulerAngles;

            Transform[] allObjects = FindObjectsOfType<Transform>();

            

            //world.lastPos -= transform.position;
            //oldPos -= transform.position;

            foreach (Transform obj in allObjects)
            {
                Transform t = (Transform)obj;

                if (((t.parent == null && t != universe) || t.parent == universe))
                {
                    t.position -= lastPlayerPos;

                    /*float newAngleDegreesZ = 90f - (90f - ((180f - transform.eulerAngles.z) / 2f));
                    float newSIDEZ = (2f * math.pow(Vector3.Distance(obj.transform.position, transform.position), 2f)) - (2f * math.pow(Vector3.Distance(obj.transform.position, transform.position), 2f) * Mathf.Cos(transform.eulerAngles.z * Mathf.Deg2Rad));
                    float3 newPosZ = new float3(-Mathf.Sin(newAngleDegreesZ * Mathf.Deg2Rad) * newSIDEZ, -Mathf.Cos(newAngleDegreesZ * Mathf.Deg2Rad) * newSIDEZ, 0f);

                    float newAngleDegreesX = 90f - (90f - ((180f - transform.eulerAngles.x) / 2f));
                    float newSIDEX = (2f * math.pow(Vector3.Distance(obj.transform.position, transform.position), 2f)) - (2f * math.pow(Vector3.Distance(obj.transform.position, transform.position), 2f) * Mathf.Cos(transform.eulerAngles.x * Mathf.Deg2Rad));
                    float3 newPosX = new float3(0f, Mathf.Cos(newAngleDegreesX * Mathf.Deg2Rad) * newSIDEX, Mathf.Sin(newAngleDegreesX * Mathf.Deg2Rad) * newSIDEX);

                    float newAngleDegreesY = 90f - (90f - ((180f - transform.eulerAngles.y) / 2f));
                    float newSIDEY = (2f * math.pow(Vector3.Distance(obj.transform.position, transform.position), 2f)) - (2f * math.pow(Vector3.Distance(obj.transform.position, transform.position), 2f) * Mathf.Cos(transform.eulerAngles.y * Mathf.Deg2Rad));
                    float3 newPosY = new float3(0f, Mathf.Cos(newAngleDegreesX * Mathf.Deg2Rad) * newSIDEX, Mathf.Sin(newAngleDegreesX * Mathf.Deg2Rad) * newSIDEX);

                    float3 test = new float3();
                    test.x;
                    test.y;
                    test.z;*/

                    //t.position += Vector3.Distance(obj.position, lastPlayerPos) * Vector3.right * math.cos(math.radians(90f - Vector3.Angle(transform.up, Vector3.up)));
                }
            }

            //universe.position = universe.position-transform.position;
            universe.eulerAngles -= lastPlayerRot;
            transform.rotation = Quaternion.identity;

            //universe.position = Vector3.zero;
        }


        if (Input.GetMouseButton(0))
        {
            body.Find("Near").localEulerAngles += new Vector3(-Input.GetAxis("Mouse Y") * mouseSpeed * Time.deltaTime, 0f, 0f);

            //body.localEulerAngles = Vector3.Lerp(body.localEulerAngles, body.localEulerAngles + new Vector3(0f, Input.GetAxis("Mouse X") * mouseSpeed, 0f), .002f);
            body.localEulerAngles += new Vector3(0f, Input.GetAxis("Mouse X") * mouseSpeed * Time.deltaTime, 0f);
        }
    }

    void FixedUpdate()
    {
        rb.AddForce(((planet.transform.position - transform.position).normalized * gravity));

        Quaternion newRot = Quaternion.FromToRotation(Vector3.up, -(planet.transform.position - transform.position).normalized);

        transform.rotation = newRot;

        if (Input.GetKey(KeyCode.W))
        {
            rb.AddForce(body.forward * walkSpeed);
        }

        if (Input.GetKey(KeyCode.S))
        {
            rb.AddForce(-body.forward * walkSpeed);
        }

        if (Input.GetKey(KeyCode.A))
        {
            rb.AddForce(-body.right * walkSpeed);
        }

        if (Input.GetKey(KeyCode.D))
        {
            rb.AddForce(body.right * walkSpeed);
        }

        if (Input.GetKey(KeyCode.Space))
        {
            rb.AddForce(body.up * (gravity + jumpSpeed));
        }
    }
}
