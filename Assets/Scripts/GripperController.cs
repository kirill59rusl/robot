using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.InputSystem;


public class GripperController : MonoBehaviour
{

    public Transform holdPoint;


    public Transform sensorPoint;


    public float grabDistance = 0.08f;


    public LayerMask ballLayer;



    public bool gripperIR;

    [Header("Use Real Robot")]
    public bool useRealRobot = false;

    private GameObject heldBall;

    private Rigidbody heldRb;

    private Collider heldCollider;



    void Update()
    {
        // Отключаем локальный Update, если работаем через ROS на реальном роботе
        if (useRealRobot)
        {
            UpdateGripperIRReal();
            return;
        }
        UpdateGripperIR();



        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            Grab();
        }



        if (Keyboard.current.leftShiftKey.wasPressedThisFrame)
        {
            Release();
        }

    }



    void UpdateGripperIRReal()
    {
        var sensors = GetComponent<VirtualSensors>();
        if (sensors != null)
        {
            // Если расстояние от ИК-датчика клешни близко к минимуму, считаем, что объект обнаружен
            gripperIR = sensors.gripperIR < (sensors.gripperRange * 0.5f);
        }
    }



    void UpdateGripperIR()
    {

        Collider[] hits =
            Physics.OverlapSphere(
                sensorPoint.position,
                grabDistance,
                ballLayer
            );


        gripperIR = false;


        foreach (Collider hit in hits)
        {

            if (hit.CompareTag("TargetBall"))
            {
                gripperIR = true;
                break;
            }

        }

    }







    public void Grab()
    {

        if (!gripperIR)
        {
            //Debug.Log("No ball detected");
            return;
        }



        Collider[] hits =
            Physics.OverlapSphere(
                sensorPoint.position,
                grabDistance,
                ballLayer
            );



        foreach (Collider hit in hits)
        {

            if (hit.CompareTag("TargetBall"))
            {

                heldBall = hit.gameObject;


                heldRb =
                    heldBall.GetComponent<Rigidbody>();


                heldCollider =
                    heldBall.GetComponent<Collider>();




                heldRb.isKinematic = true;

                heldCollider.enabled = false;




                heldBall.transform.SetParent(
                    holdPoint
                );


                heldBall.transform.localPosition =
                    Vector3.zero;


                heldBall.transform.localRotation =
                    Quaternion.identity;



                Debug.Log("BALL GRABBED");


                return;

            }

        }

    }








    public void Release()
    {

        if (heldBall == null)
            return;



        heldBall.transform.SetParent(null);



        heldCollider.enabled = true;


        heldRb.isKinematic = false;



        heldBall = null;


        heldRb = null;


        heldCollider = null;



        Debug.Log("BALL RELEASED");

    }






    void OnDrawGizmos()
    {
        if (holdPoint)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(
                holdPoint.position,
                0.03f
            );
        }


        if (sensorPoint)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(
                sensorPoint.position,
                0.03f
            );
        }
    }
    public bool HasBall()
    {
        return heldBall != null;
    }

}