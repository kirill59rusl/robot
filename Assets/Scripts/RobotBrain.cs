using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using UnityEngine.InputSystem;
[RequireComponent(typeof(Rigidbody))]
public class RobotBrain : Agent
{
    [Header("Robot Components")]
    public TrackController trackController;
    public VirtualSensors sensors;
    public GripperController gripper;
    public SimulatedYoloCamera cameraSensor;
    public CameraPanController cameraPan;
    [Header("Environment")]
    public MazeGenerator mazeGenerator;

    bool rewardForPickupGiven;
    Rigidbody rb;

    Vector3 startPosition;
    Quaternion startRotation;
    float debugTimer;
    float previousDistanceToBall;
    float previousGas;
    float previousSteer;

    float episodeTimer;

    bool hasBall;

    Transform ball;

    // Camera memory
    float lastKnownBallAngle = 0f;
    float timeSinceBallSeen = 0f;

    // Arena size
    const float maxArenaDistance = 1.5f;


    // Maximum robot speed
    const float maxRobotSpeed = 0.6f;

    public override void Initialize()
    {

        rb = GetComponent<Rigidbody>();

        if (trackController == null)
            trackController = GetComponent<TrackController>();

        if (sensors == null)
            sensors = GetComponent<VirtualSensors>();

        if (gripper == null)
            gripper = GetComponent<GripperController>();
        if (cameraPan == null)
        {
            cameraPan = GetComponentInChildren<CameraPanController>();
            if (cameraPan == null)
            {
                Debug.LogWarning("CameraPanController not found!");
            }
        }
        if (cameraSensor == null)
        {
            cameraSensor = GetComponentInChildren<SimulatedYoloCamera>();
            if (cameraSensor == null)
            {
                Debug.LogWarning("SimulatedYoloCamera not found!");
            }
        }
        startPosition = transform.position;
        startRotation = transform.rotation;

        Debug.Log($"RobotBrain initialized");
    }

    public override void OnEpisodeBegin()
    {   
        if (gripper != null && gripper.HasBall())
        {
            gripper.Release();
        }
        
        if (mazeGenerator != null)
            mazeGenerator.RegenerateMaze();

        Vector3 spawn =
            mazeGenerator.GetStartPosition();

        spawn.y = 0.04f;

        transform.position = spawn;

        transform.rotation = Quaternion.identity;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        

        var obj = GameObject.FindGameObjectWithTag("TargetBall");
        if (obj != null)
        {
            ball = obj.transform;
        }
        else
        {
            Debug.LogError("TargetBall not found!");
        }
        previousDistanceToBall =
            Vector3.Distance(transform.position, ball.position);

        rewardForPickupGiven = false;
        previousGas = 0;
        previousSteer = 0;

        episodeTimer = 0;

        hasBall = false;
        timeSinceBallSeen = 0f;
        lastKnownBallAngle = 0f;
        startPosition = spawn;
        startRotation = transform.rotation;
        
    }

    

    public override void CollectObservations(VectorSensor sensor)
    {
        //Debug.Log("CollectObservations");
        //-----------------------------------
        // 1. Ultrasonic
        //-----------------------------------
        debugTimer += Time.fixedDeltaTime;

        if (debugTimer > 1f)
        {
            debugTimer = 0f;
            Debug.Log(
                $"HasBall={hasBall} " +
                $"BallVisible={cameraSensor.ballVisible} " +
                $"BallOffset={cameraSensor.horizontalOffset:F2} " +
                $"BallDist={cameraSensor.normalizedDistance:F2}"
                
            );
            Debug.Log(
                $"Total: {GetCumulativeReward():0.000}"
            );

        }
        sensor.AddObservation(
            Mathf.Clamp01(sensors.ultrasonic / sensors.ultrasonicRange)
        );

        //-----------------------------------
        // 2-3 Side IR
        //-----------------------------------

        sensor.AddObservation(
            Mathf.Clamp01(sensors.leftIR / sensors.irRange)
        );

        sensor.AddObservation(
            Mathf.Clamp01(sensors.rightIR / sensors.irRange)
        );

        //-----------------------------------
        // 4 Gripper IR
        //-----------------------------------

        sensor.AddObservation(
            Mathf.Clamp01(sensors.gripperIR / sensors.gripperRange)
        );

        //-----------------------------------
        // Camera
        //-----------------------------------

        if (cameraSensor != null && cameraSensor.ballVisible)
        {
            sensor.AddObservation(cameraSensor.horizontalOffset);

            sensor.AddObservation(cameraSensor.normalizedDistance);

            lastKnownBallAngle =
                cameraSensor.horizontalOffset;

            timeSinceBallSeen = 0f;

            sensor.AddObservation(lastKnownBallAngle);

            sensor.AddObservation(1f);
        }
        else
        {
            sensor.AddObservation(0f);

            sensor.AddObservation(1f);

            sensor.AddObservation(lastKnownBallAngle);

            sensor.AddObservation(0f);

            timeSinceBallSeen += Time.fixedDeltaTime;
        }

        //-----------------------------------
        // Camera servo
        //-----------------------------------

        sensor.AddObservation(cameraPan.CurrentAngle / cameraPan.maxAngle);

        //-----------------------------------
        // Has ball
        //-----------------------------------

        sensor.AddObservation(hasBall ? 1f : 0f);

        //-----------------------------------
        // Ego position
        //-----------------------------------

        Vector3 delta =
            transform.position - startPosition;

        sensor.AddObservation(
            Mathf.Clamp(delta.x / maxArenaDistance, -1f, 1f)
        );

        sensor.AddObservation(
            Mathf.Clamp(delta.z / maxArenaDistance, -1f, 1f)
        );

        //-----------------------------------
        // Heading
        //-----------------------------------

        float heading =
            Mathf.Sin(transform.eulerAngles.y * Mathf.Deg2Rad);

        sensor.AddObservation(heading);

        //-----------------------------------
        // Speed
        //-----------------------------------

        sensor.AddObservation(
            rb.linearVelocity.magnitude / maxRobotSpeed
        );

        //-----------------------------------
        // Time without seeing ball
        //-----------------------------------

        sensor.AddObservation(
            Mathf.Clamp01(timeSinceBallSeen / 10f)
        );
    } 


    public override void OnActionReceived(ActionBuffers actions)
    {
        //Debug.Log("Action");
    episodeTimer += Time.fixedDeltaTime;

    //-----------------------------------
    // Continuous actions
    //-----------------------------------
    if (trackController == null || sensors == null || gripper == null)
    {
        Debug.LogError("Missing components in RobotBrain!");
        return;
    }
    
    float gas =
        Mathf.Clamp(
            actions.ContinuousActions[0],
            -1f,
            1f
        );

    float steering =
        Mathf.Clamp(
            actions.ContinuousActions[1],
            -1f,
            1f
        );

    float cameraInput =
        Mathf.Clamp(
            actions.ContinuousActions[2],
            -1f,
            1f
        );

    //-----------------------------------
    // Apply movement
    //-----------------------------------

    trackController.gas = gas;
    trackController.steer = steering;

    //-----------------------------------
    // Camera
    //-----------------------------------

    if (cameraPan != null)
    {
        cameraPan.SetInput(cameraInput);
    }

    //-----------------------------------
    // Gripper
    //-----------------------------------

    int grip =
        actions.DiscreteActions[0];

    switch (grip)
    {
        case 1:
            gripper.Grab();
            break;

        case 2:
            gripper.Release();
            break;
    }

    hasBall = gripper.HasBall();

    //-----------------------------------
    // Small time penalty
    //-----------------------------------

    AddReward(-0.0005f);

    //-----------------------------------
    // Distance reward
    //-----------------------------------

    if (ball != null)
    {
        float currentDistance =
            Vector3.Distance(
                transform.position,
                ball.position
            );

        float delta =
            previousDistanceToBall - currentDistance;

        AddReward(delta * 0.2f);

        previousDistanceToBall =
            currentDistance;
    }

    //-----------------------------------
    // Ball visible reward
    //-----------------------------------

    if (cameraSensor.ballVisible)
    {
        AddReward(0.005f);

        AddReward(
            (1f -
            Mathf.Abs(cameraSensor.horizontalOffset))
            * 0.002f
        );
    }

    //-----------------------------------
    // Wall avoidance
    //-----------------------------------

    if (sensors.ultrasonic < 0.20f)
        AddReward(-0.003f);

    if (sensors.leftIR < 0.10f)
        AddReward(-0.003f);

    if (sensors.rightIR < 0.10f)
        AddReward(-0.003f);

    //-----------------------------------
    // Smooth driving
    //-----------------------------------

    float change =
        Mathf.Abs(gas - previousGas)
        +
        Mathf.Abs(steering - previousSteer);

    AddReward(-change * 0.001f);

    previousGas = gas;
    previousSteer = steering;
    
    //-----------------------------------
    // Success
    //-----------------------------------

    if (hasBall && !rewardForPickupGiven)
    {
        AddReward(5f);
        rewardForPickupGiven = true;
    }
    if (hasBall)
    {
        float distToStart = Vector3.Distance(transform.position, startPosition);

        if (distToStart < 0.6f) // радиус успеха
        {
            AddReward(10f);      // большая награда за выполнение всей задачи
            EndEpisode();
        }
    }
    //-----------------------------------
    // Robot escaped
    //-----------------------------------

    if (transform.position.y < -0.2f)
    {
        AddReward(-2f);

        EndEpisode();
    }

    //-----------------------------------
    // Timeout
    //-----------------------------------

    if (episodeTimer > 120f)
    {
        EndEpisode();
    }
}
public override void Heuristic(in ActionBuffers actionsOut)
{
    var continuous = actionsOut.ContinuousActions;
    var discrete = actionsOut.DiscreteActions;

    var keyboard = Keyboard.current;

    if (keyboard == null)
        return;

    //----------------------------------
    // Gas
    //----------------------------------

    float gas = 0f;

    if (keyboard.wKey.isPressed)
        gas = 1f;

    if (keyboard.sKey.isPressed)
        gas = -1f;

    //----------------------------------
    // Steering
    //----------------------------------

    float steering = 0f;

    if (keyboard.aKey.isPressed)
        steering = -1f;

    if (keyboard.dKey.isPressed)
        steering = 1f;

    //----------------------------------
    // Camera
    //----------------------------------

    float camera = 0f;

    if (keyboard.qKey.isPressed)
        camera = -1f;

    if (keyboard.eKey.isPressed)
        camera = 1f;

    //----------------------------------
    // Write actions
    //----------------------------------

    continuous[0] = gas;
    continuous[1] = steering;
    continuous[2] = camera;

    //----------------------------------
    // Gripper
    //----------------------------------

    discrete[0] = 0;

    if (keyboard.spaceKey.isPressed)
        discrete[0] = 1;

    if (keyboard.leftShiftKey.isPressed)
        discrete[0] = 2;
}

}   