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

    [Header("Rewards")]
    [SerializeField]
    private RewardSettings rewardSettings = new();

    private RewardSystem rewardSystem;
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

    Transform goalCube;
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
        rewardSystem = new RewardSystem(this, rewardSettings);
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

        spawn.y = 0;

        transform.position = spawn;

        transform.rotation = Quaternion.identity;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        

        ball = mazeGenerator.GetBall();

        if (ball == null)
        {
            Debug.LogError("Ball not found!");
        }
        
        
        goalCube = mazeGenerator.GetGoal();

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
            //Debug.Log(
            //    $"HasBall={hasBall} " +
            //    $"BallVisible={cameraSensor.ballVisible} " +
            //    $"BallOffset={cameraSensor.horizontalOffset:F2} " +
            //    $"BallDist={cameraSensor.normalizedDistance:F2}"
            //    
            //);
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

    rewardSystem.StepPenalty();

    if (ball != null && !hasBall)
    {
        float currentDistance = Vector3.Distance(transform.position, ball.position);
        rewardSystem.DistanceReward(previousDistanceToBall, currentDistance);
        previousDistanceToBall = currentDistance;
        if (cameraSensor.ballVisible)
        {
            rewardSystem.BallVisible(cameraSensor.horizontalOffset, currentDistance);
        }
    }

    

    rewardSystem.WallPenalty(
        sensors.ultrasonic,
        sensors.leftIR,
        sensors.rightIR
    );

    float change =
        Mathf.Abs(gas - previousGas) +
        Mathf.Abs(steering - previousSteer);
    rewardSystem.SmoothDriving(change);

    previousGas = gas;
    previousSteer = steering;

    if (hasBall && !rewardForPickupGiven)
    {
        rewardSystem.Pickup();
        rewardForPickupGiven = true;
    }

    if (hasBall && goalCube != null)
    {
        float dist = Vector3.Distance(transform.position, goalCube.position);
        rewardSystem.GoalApproach(dist);
        if (dist < 0.55f)
        {
            rewardSystem.GoalReached();
            EndEpisode();
        }
    }
    if (ball != null)
        {
            if (ball.position.y < -1f)
            {
                AddReward(-1.0f);   // или rewardSystem.BallLost();
                EndEpisode();
                return;
            }
}
    if (transform.position.y < -0.2f)
    {
        rewardSystem.Fell();
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