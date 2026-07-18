using UnityEngine;
using UnityEngine.InputSystem;
 
/// <summary>
/// Управление гусеницами GFS-X:
/// дифференциал → PWM → MovePosition/MoveRotation.
/// WASD управление:
/// W/S - газ
/// A/D - поворот
/// </summary>
 
[RequireComponent(typeof(Rigidbody))]
public class TrackController : MonoBehaviour
{
    [Header("Калибровка")]
    public float moveSpeed = 0.57f;
    public float turnSpeed = 120f;
    [Range(0f, 1f)]
    public float turnK = 0.30f;
 
    public float maxLinearCmd = 0.25f;
 
    [Header("PWM")]
    public float speedToPwm = 200f;
    public float motorDeadzone = 30f;
    public float minMotorPwm = 50f;
    public float maxPwmStep = 15f;
 
    [Header("Команды (-1...1)")]
    public float gas;
    public float steer;
    [Header("ROS")]
    public bool useRealRobot = false;
    
    public RosBridge rosBridge;
 
    private Rigidbody rb;
 
    private float prevLeftPwm;
    private float prevRightPwm;


 
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
 
        rb.interpolation =
            RigidbodyInterpolation.Interpolate;
 
        rb.collisionDetectionMode =
            CollisionDetectionMode.Continuous;
 
        rb.linearDamping = 9f;
        rb.angularDamping = 9f;
 
        rb.constraints |=
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
    }



 
    private void Update()
    {
        ReadKeyboard();
    }




 
    private void ReadKeyboard()
    {
        gas = 0f;
        steer = 0f;
 
        if (Keyboard.current == null)
            return;


 
        // газ
 
        if (Keyboard.current.wKey.isPressed)
            gas += 1f;
 
        if (Keyboard.current.sKey.isPressed)
            gas -= 1f;


 
        // поворот
 
        if (Keyboard.current.aKey.isPressed)
            steer -= 1f;
 
        if (Keyboard.current.dKey.isPressed)
            steer += 1f;
    }






 
    private void FixedUpdate()
    {
        float g =
            Mathf.Clamp(
                gas,
                -1f,
                1f
            );
 
        float s =
            Mathf.Clamp(
                steer,
                -1f,
                1f
            );


 
        MixTracks(
            g,
            s,
            out float vLeft,
            out float vRight
        );
 
        if (useRealRobot)
        {
            if (rosBridge != null)
                rosBridge.SendVelocity(g, s);
        
        }
 
        float leftPwm =
            ApplyMotorModel(
                vLeft * speedToPwm,
                ref prevLeftPwm
            );
 
        float rightPwm =
            ApplyMotorModel(
                vRight * speedToPwm,
                ref prevRightPwm
            );


 
        float effLeft =
            leftPwm / speedToPwm;
 
        float effRight =
            rightPwm / speedToPwm;


 
        float v =
            0.5f *
            (effRight + effLeft);


 
        float diff =
            effRight - effLeft;


 
        float yawDegPerSec =
            (diff /
            (2f * Mathf.Max(moveSpeed, 1e-4f)))
            * turnSpeed;


 
        float dt =
            Time.fixedDeltaTime;


 
        Vector3 delta =
            transform.right *
            (v * dt);
 
        delta.y = 0f;


 
        rb.MovePosition(
            rb.position + delta
        );


 
        rb.MoveRotation(
            rb.rotation *
            Quaternion.Euler(
                0f,
                yawDegPerSec * dt,
                0f
            )
        );
    }






 
    private void MixTracks(
        float g,
        float s,
        out float vLeft,
        out float vRight
    )
    {
        float linear =
            Mathf.Clamp(
                g * moveSpeed,
                -maxLinearCmd,
                maxLinearCmd
            );
 
        float turn =
            s *
            turnK *
            moveSpeed;


 
        vLeft =
            Mathf.Clamp(
                linear - turn,
                -moveSpeed,
                moveSpeed
            );
 
        vRight =
            Mathf.Clamp(
                linear + turn,
                -moveSpeed,
                moveSpeed
            );
    }






 
    private float ApplyMotorModel(
        float rawPwm,
        ref float previous
    )
    {
        float sign =
            Mathf.Sign(rawPwm);
 
        float mag =
            Mathf.Abs(rawPwm);


 
        if (mag < motorDeadzone)
        {
            mag = 0f;
        }
        else if (mag < minMotorPwm)
        {
            mag = minMotorPwm;
        }


 
        mag =
            Mathf.Min(
                mag,
                100f
            );


 
        float target =
            sign * mag;


 
        previous +=
            Mathf.Clamp(
                target - previous,
                -maxPwmStep,
                maxPwmStep
            );
 
        return previous;
    }
    private void CalculatePWM(
        float gas,
        float steer,
        out float leftPwm,
        out float rightPwm)
    {
        MixTracks(
            gas,
            steer,
            out float vLeft,
            out float vRight);
    
        leftPwm =
            ApplyMotorModel(
                vLeft * speedToPwm,
                ref prevLeftPwm);
    
        rightPwm =
            ApplyMotorModel(
                vRight * speedToPwm,
                ref prevRightPwm);
    }
}