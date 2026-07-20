using UnityEngine;
using UnityEngine.InputSystem;


public class GripperController : MonoBehaviour
{

    public Transform holdPoint;


    public Transform sensorPoint;


    public float grabDistance = 0.08f;


    public LayerMask ballLayer;



    public bool gripperIR;

    [Header("Задержка захвата (sim2real)")]
    [Tooltip("Сколько секунд реально 'закрывается' гриппер, прежде чем мяч считается схваченным")]
    public float grabDuration = 3f;

    [Tooltip("Если во время закрытия мяч отдалился от сенсора дальше, чем grabDistance * этот множитель - захват срывается")]
    public float grabAbortMultiplier = 1.5f;


    private GameObject heldBall;

    private Rigidbody heldRb;

    private Collider heldCollider;

    // --- состояние процесса закрытия гриппера ---
    private GameObject pendingBall;
    private float grabTimer;
    private bool isGripping;

    /// <summary>Гриппер сейчас в процессе закрытия (мяч ещё не считается пойманным)</summary>
    public bool IsGripping => isGripping;

    /// <summary>Прогресс закрытия от 0 до 1 (для отладки/наблюдений, если понадобится)</summary>
    public float GrabProgress => isGripping ? Mathf.Clamp01(grabTimer / grabDuration) : (heldBall != null ? 1f : 0f);


    void Update()
    {

        UpdateGripperIR();



        if (Keyboard.current != null)
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
                Grab();

            if (Keyboard.current.leftShiftKey.wasPressedThisFrame)
                Release();
        }

    }

    void FixedUpdate()
    {
        // Таймер закрытия гриппера считаем в FixedUpdate (а не Update),
        // чтобы он был детерминирован относительно шагов ML-Agents/Academy
        // и не зависел от плавающего фреймрейта при ускоренном time_scale.
        if (!isGripping)
            return;

        if (pendingBall == null || !StillInGrabRange(pendingBall))
        {
            Debug.Log("GRAB FAILED - мяч ушёл из зоны захвата во время закрытия");
            AbortGrip();
            return;
        }

        grabTimer += Time.fixedDeltaTime;

        if (grabTimer >= grabDuration)
        {
            CompleteGrab(pendingBall);
        }
    }

    private bool StillInGrabRange(GameObject ball)
    {
        float dist = Vector3.Distance(sensorPoint.position, ball.transform.position);
        return dist <= grabDistance * grabAbortMultiplier;
    }

    private void AbortGrip()
    {
        isGripping = false;
        pendingBall = null;
        grabTimer = 0f;
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







    /// <summary>
    /// Запускает процесс закрытия гриппера (не хватает мяч мгновенно!).
    /// Мяч будет реально прикреплён только через grabDuration секунд,
    /// и только если всё это время оставался в зоне захвата.
    /// </summary>
    public void Grab()
    {

        if (heldBall != null)
        {
            // уже что-то держим
            return;
        }

        if (isGripping)
        {
            // уже в процессе закрытия - повторное нажатие ничего не делает
            return;
        }

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
                pendingBall = hit.gameObject;
                grabTimer = 0f;
                isGripping = true;

                Debug.Log("GRIPPER CLOSING...");

                return;

            }

        }

    }

    /// <summary>
    /// Реально прикрепляет мяч к holdPoint. Вызывается автоматически
    /// из FixedUpdate по истечении grabDuration - не вызывать напрямую.
    /// </summary>
    private void CompleteGrab(GameObject ball)
    {
        heldBall = ball;

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

        isGripping = false;
        pendingBall = null;
        grabTimer = 0f;
    }








    public void Release()
    {

        // Если гриппер ещё закрывается - отменяем захват вместо того,
        // чтобы отпускать уже пойманный мяч (которого пока и нет).
        if (isGripping)
        {
            Debug.Log("GRAB CANCELLED");
            AbortGrip();
            return;
        }

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
            Gizmos.color = isGripping ? Color.yellow : Color.red;
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