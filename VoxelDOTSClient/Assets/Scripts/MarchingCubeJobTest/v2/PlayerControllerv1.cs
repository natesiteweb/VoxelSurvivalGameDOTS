using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.UI;

public class PlayerControllerv1 : MonoBehaviour
{
    public Transform universe;
    public float originThreshold;
    public float gravity;
    public float mouseSpeed;
    public float walkSpeed;
    public float jumpSpeed;
    public Planet planet;
    public Planet planet2;

    public Text horizonDistanceText;
    public Text curMinimumLOD;

    public Planet dominantPlanet;

    public float curSpeed;

    public Transform body;
    Transform cameraBody;
    Rigidbody rb;

    Vector3 lastPlayerPos;

    int addOrRemove = 1;

    // Start is called before the first frame update
    void Start()
    {
        dominantPlanet = planet;
        rb = GetComponent<Rigidbody>();
        cameraBody = body.Find("Near");
    }

    float distanceToHorizon;

    // Update is called once per frame
    void Update()
    {
        distanceToHorizon = math.sqrt(math.abs((Vector3.Distance(transform.position, dominantPlanet.transform.position) * Vector3.Distance(transform.position, dominantPlanet.transform.position)) - (dominantPlanet.radius * dominantPlanet.radius))) + 1000f;
        horizonDistanceText.text = "LOD: " + (Mathf.CeilToInt(Mathf.Log(distanceToHorizon, 2f) - Mathf.Log(dominantPlanet.baseChunkSize, 2f))) + " Horizon: " + distanceToHorizon.ToString();
        curMinimumLOD.text = "Min LOD Loaded: " + dominantPlanet.curLowerLODLoaded;

        //Debug.Log(Vector3.Angle(transform.up, Vector3.up));

        addOrRemove = Input.GetKey(KeyCode.E) ? 1 : -1;

        if(Input.GetMouseButton(1))
        {
            RaycastHit hit;
            Vector3 fwd = cameraBody.TransformDirection(Vector3.forward);

            List<Chunk> chunks = new List<Chunk>();
            HashSet<Chunk> chunkSet = new HashSet<Chunk>();

            if (Physics.Raycast(cameraBody.position, fwd, out hit))
            {
                if(hit.transform.GetComponent<Chunk>())
                {
                    int range = 5;

                    for(int x = -range; x <= range; x++)
                    {
                        for (int y = -range; y <= range; y++)
                        {
                            for (int z = -range; z <= range; z++)
                            {
                                Vector3 hitPos = math.round(hit.point) + new int3(x, y, z);

                                float density = dominantPlanet.GetDensity(hitPos);

                                density += ((range / 15f) - math.clamp(Vector3.Distance(hitPos, math.round(hit.point)) / (range * 4f), 0f, (range / 15f))) * addOrRemove;

                                List<Chunk> localChunks = dominantPlanet.SetDensity(hitPos, density);

                                for(int i = 0; i < localChunks.Count; i++)
                                {
                                    if(!chunkSet.Contains(localChunks[i]))
                                    {
                                        chunkSet.Add(localChunks[i]);
                                        chunks.Add(localChunks[i]);
                                    }
                                }

                                //Debug.Log("Density: " + density);
                            }
                        }
                    }

                    /*Vector3 hitPos = math.round(hit.point);

                    float density = dominantPlanet.GetDensity(hitPos);

                    density += ((range / 15f) - math.clamp(Vector3.Distance(hitPos, math.round(hit.point)) / (range * 4f), 0f, (range / 15f))) * addOrRemove;

                    List<Chunk> localChunks = dominantPlanet.SetDensity(hitPos, density);

                    for (int i = 0; i < localChunks.Count; i++)
                    {
                        if (!chunkSet.Contains(localChunks[i]))
                        {
                            chunkSet.Add(localChunks[i]);
                            chunks.Add(localChunks[i]);
                        }
                    }*/

                    for (int i = 0; i < chunks.Count; i++)
                    {
                        dominantPlanet.loadQueue.Add(chunks[i]);
                    }
                }
            }
        }

        if (Input.GetMouseButton(0))
        {
            cameraBody.localEulerAngles += new Vector3(-Input.GetAxis("Mouse Y") * mouseSpeed * Time.deltaTime, 0f, 0f);

            //body.localEulerAngles = Vector3.Lerp(body.localEulerAngles, body.localEulerAngles + new Vector3(0f, Input.GetAxis("Mouse X") * mouseSpeed, 0f), .002f);
            body.localEulerAngles += new Vector3(0f, Input.GetAxis("Mouse X") * mouseSpeed * Time.deltaTime, 0f);
        }


        Quaternion newRot = Quaternion.Lerp(transform.rotation, Quaternion.FromToRotation(Vector3.up, -(dominantPlanet.transform.position - transform.position).normalized), .1f * Time.deltaTime);

        transform.rotation = newRot;

        if (Vector3.Distance(planet.transform.position, transform.position) - planet.radius <= (Vector3.Distance(planet.transform.position, planet2.transform.position) - planet.radius - planet2.radius))
            dominantPlanet = planet;
        else
            dominantPlanet = planet2;

        curSpeed = math.pow(math.abs(Vector3.Distance(dominantPlanet.transform.position, transform.position) - (dominantPlanet.radius + 10f)), 1.5f) * walkSpeed + 1f;
        curSpeed = math.clamp(curSpeed / 100f, 100f, 70000f);

        if (Input.GetKey(KeyCode.W))
        {
            transform.position += cameraBody.forward * curSpeed * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.A))
        {
            transform.position -= cameraBody.right * curSpeed * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.S))
        {
            transform.position -= cameraBody.forward * curSpeed * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.D))
        {
            transform.position += cameraBody.right * curSpeed * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.LeftShift))
        {
            transform.position += cameraBody.up * curSpeed * Time.deltaTime;
        }

        if (Input.GetKey(KeyCode.LeftControl))
        {
            transform.position -= cameraBody.up * curSpeed * Time.deltaTime;
        }


        if (Vector3.Distance(transform.position, Vector3.zero) > (originThreshold))
        {
            lastPlayerPos = transform.position;
            planet.lastPlayerPos -= lastPlayerPos;
            planet2.lastPlayerPos -= lastPlayerPos;

            transform.position = float3.zero;
            Quaternion lastPlayerRot = transform.rotation;

            Transform[] allObjects = FindObjectsOfType<Transform>();

            

            foreach (Transform obj in allObjects)
            {
                Transform t = (Transform)obj;

                if (t.parent == null && t != transform)
                {
                    t.position -= lastPlayerPos;
                }
            }
        }
    }

    void FixedUpdate()
    {
        //rb.AddForce(((planet2.transform.position - transform.position).normalized * gravity));

        /*if (Input.GetKey(KeyCode.W))
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
        }*/
    }
}
