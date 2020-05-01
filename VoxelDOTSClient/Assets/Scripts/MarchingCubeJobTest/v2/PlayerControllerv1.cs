using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

public class PlayerControllerv1 : MonoBehaviour
{
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
        if (Vector3.Distance(transform.position, Vector3.zero) > originThreshold)
        {
            lastPlayerPos = transform.position;

            Transform[] allObjects = FindObjectsOfType<Transform>();            

            //world.lastPos -= transform.position;
            //oldPos -= transform.position;

            foreach (Transform obj in allObjects)
            {
                Transform t = (Transform)obj;

                if (t.parent == null)
                {
                    t.position -= lastPlayerPos;
                }
            }

            //universe.position = universe.position-transform.position;
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
        rb.AddForce(((planet.transform.position + new Vector3(planet.LODActualSize / 2f, planet.LODActualSize / 2f, planet.LODActualSize / 2f) - transform.position).normalized * gravity));

        Quaternion newRot = Quaternion.FromToRotation(Vector3.up, -((planet.transform.position + new Vector3(planet.LODActualSize / 2f, planet.LODActualSize / 2f, planet.LODActualSize / 2f)) - transform.position).normalized);

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
