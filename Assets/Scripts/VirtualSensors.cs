using UnityEngine;
using System.Collections.Generic;

public class VirtualSensors : MonoBehaviour
{
    private float debugTimer;

    private Collider[] robotColliders;


    [Header("Sensor Points")]
    public Transform centerPoint;
    public Transform leftIRPoint;
    public Transform rightIRPoint;
    public Transform gripperIRPoint;


    [Header("Ultrasonic")]
    public float ultrasonicRange = 2.0f;
    public int ultrasonicRays = 7;
    public float ultrasonicAngle = 30f;


    [Header("IR Sensors")]
    public float irRange = 0.5f;


    [Header("Gripper IR")]
    public float gripperRange = 0.3f;
    public LayerMask ballLayer;


    [Header("Debug")]
    public bool drawDebug = true;



    [HideInInspector]
    public float ultrasonic;

    [HideInInspector]
    public float leftIR;

    [HideInInspector]
    public float rightIR;

    [HideInInspector]
    public float gripperIR;



    void Start()
    {
        robotColliders =
            GetComponentsInChildren<Collider>();
    }



    void Update()
    {
        ultrasonic = ReadUltrasonic();

        leftIR = ReadIR(leftIRPoint);
        rightIR = ReadIR(rightIRPoint);

        gripperIR = ReadGripperIR();



        debugTimer += Time.deltaTime;


        
    }




    bool IsOwnCollider(Collider col)
    {
        foreach (Collider own in robotColliders)
        {
            if (col == own)
                return true;
        }

        return false;
    }





    // ==========================
    // ULTRASONIC
    // ==========================

    float ReadUltrasonic()
    {
        float minDistance = ultrasonicRange;


        for (int i = 0; i < ultrasonicRays; i++)
        {

            float angle = Mathf.Lerp(
                -ultrasonicAngle / 2,
                ultrasonicAngle / 2,
                i / (float)(ultrasonicRays - 1)
            );


            Quaternion rotation =
                Quaternion.AngleAxis(
                    angle,
                    centerPoint.up
                );


            Vector3 direction =
                rotation * centerPoint.forward;



            RaycastHit[] hits =
                Physics.RaycastAll(
                    centerPoint.position,
                    direction,
                    ultrasonicRange
                );


            foreach (RaycastHit hit in hits)
            {

                if (IsOwnCollider(hit.collider))
                    continue;


                if (hit.collider.CompareTag("TargetBall"))
                    continue;


                minDistance =
                    Mathf.Min(
                        minDistance,
                        hit.distance
                    );

                break;
            }



            if (drawDebug)
            {
                Debug.DrawRay(
                    centerPoint.position,
                    direction * ultrasonicRange,
                    Color.blue
                );
            }
        }


        return minDistance;
    }







    // ==========================
    // SIDE IR
    // ==========================

    float ReadIR(Transform point)
    {
        if (point == null)
            return irRange;



        RaycastHit[] hits =
            Physics.RaycastAll(
                point.position,
                point.forward,
                irRange
            );



        float distance = irRange;



        foreach (RaycastHit hit in hits)
        {

            if (IsOwnCollider(hit.collider))
                continue;


            distance =
                Mathf.Min(
                    distance,
                    hit.distance
                );


            if (drawDebug)
            {
                Debug.DrawRay(
                    point.position,
                    point.forward * hit.distance,
                    Color.red
                );
            }


            return distance;
        }



        if (drawDebug)
        {
            Debug.DrawRay(
                point.position,
                point.forward * irRange,
                Color.green
            );
        }


        return distance;
    }








    // ==========================
    // GRIPPER IR
    // ==========================

    float ReadGripperIR()
    {
        if (gripperIRPoint == null)
            return gripperRange;



        Collider[] hits =
            Physics.OverlapSphere(
                gripperIRPoint.position,
                gripperRange,
                ballLayer
            );


        float minDistance = gripperRange;



        foreach (Collider col in hits)
        {

            if (col.CompareTag("TargetBall"))
            {

                float d =
                    Vector3.Distance(
                        gripperIRPoint.position,
                        col.transform.position
                    );


                minDistance =
                    Mathf.Min(
                        minDistance,
                        d
                    );
            }
        }


        return minDistance;
    }







    void OnDrawGizmos()
    {

        if (gripperIRPoint)
        {
            Gizmos.color = Color.yellow;

            Gizmos.DrawWireSphere(
                gripperIRPoint.position,
                gripperRange
            );
        }



        if (centerPoint)
        {
            Gizmos.color = Color.blue;

            Gizmos.DrawRay(
                centerPoint.position,
                centerPoint.forward * ultrasonicRange
            );
        }
    }
}