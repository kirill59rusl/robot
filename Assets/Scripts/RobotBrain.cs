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

    public bool addSensorNoise = true;
    Rigidbody rb;

    Vector3 startPosition;
    Quaternion startRotation;
    float debugTimer;
    float previousSteer;
    private bool previousHasBall;

    float episodeTimer;

    bool hasBall;
    int holdTicks;        // ������� ����� ������ ������������ ������ (���� �����)
    float stepReward;      // ������� �� ������� ���
    

    
    Transform goalCube;
    Transform ball;

    // Camera memory
    float lastKnownBallAngle = 0f;
    float timeSinceBallSeen = 0f;

    float lastKnownCubeAngle = 0f;
    float timeSinceCubeSeen = 0f;
    
    // Arena size
    const float maxArenaDistance = 6f;


    // Maximum robot speed
    const float maxRobotSpeed = 0.6f;
        // относительный шум (в процентах диапазона)
    [SerializeField] private float ultrasonicNoise = 0.02f;   // 2%
    [SerializeField] private float irNoise = 0.015f;          // 1.5%
    [SerializeField] private float cameraAngleNoise = 0.02f;
    [SerializeField] private float cameraDistanceNoise = 0.03f;
    [SerializeField] private float servoNoise = 0.01f;
    [SerializeField] private float speedNoise = 0.02f;

    private float AddNoise(float value, float stdDev)
    {
        if (!addSensorNoise)
            return value;

        // равномерный шум
        value += Random.Range(-stdDev, stdDev);

        return Mathf.Clamp01(value);
    }

    private float AddSignedNoise(float value, float stdDev)
    {
        if (!addSensorNoise)
            return value;

        value += Random.Range(-stdDev, stdDev);

        return Mathf.Clamp(value, -1f, 1f);
    }
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
        trackController.RandomizeDomain();
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

        // Жёстко привязываем камеру к объектам ИМЕННО этой арены.
        // Без этого SimulatedYoloCamera ищет мяч/кубик по тегу глобально
        // по всей сцене, что ломается при нескольких аренах одновременно
        // (робот арены №3 может "увидеть" мяч арены №17).
        if (cameraSensor != null)
        {
            cameraSensor.targetBall = ball;
            cameraSensor.targetCube = goalCube;
        }

        previousHasBall = false;


        rewardSystem.Reset(
            Vector3.Distance(
                transform.position,
                ball.transform.position
            ),
            Vector3.Distance(
                transform.position,
                goalCube.transform.position
            )
        );
        
        
        previousSteer = 0;

        episodeTimer = 0;

        hasBall = false;
        timeSinceBallSeen = 0f;
        lastKnownBallAngle = 0f;
        timeSinceCubeSeen = 0f;
        lastKnownCubeAngle = 0f;
        startPosition = spawn;
        startRotation = transform.rotation;
        holdTicks = 0;
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
            AddNoise(
                sensors.ultrasonic / sensors.ultrasonicRange,
                ultrasonicNoise
            )
        );

        //-----------------------------------
        // 2-3 Side IR
        //-----------------------------------

        sensor.AddObservation(
            AddNoise(
                sensors.leftIR / sensors.irRange,
                irNoise
            )
        );

        sensor.AddObservation(
            AddNoise(
                sensors.rightIR / sensors.irRange,
                irNoise
            )
        );
        //-----------------------------------
        // 4 Gripper IR
        //-----------------------------------

        sensor.AddObservation(
            AddNoise(
                sensors.gripperIR / sensors.gripperRange,
                irNoise
            )
        );

        //-----------------------------------
        // Camera
        //-----------------------------------

        if (cameraSensor != null && cameraSensor.ballVisible && Random.value>0.03f)
        {
            sensor.AddObservation(
                AddSignedNoise(
                    cameraSensor.horizontalOffset,
                    cameraAngleNoise
                )
            );

            sensor.AddObservation(
                AddNoise(
                    cameraSensor.normalizedDistance,
                    cameraDistanceNoise
                )
            );

            lastKnownBallAngle =
                cameraSensor.horizontalOffset;

            timeSinceBallSeen = 0f;

            sensor.AddObservation(
                AddSignedNoise(
                    lastKnownBallAngle,
                    cameraAngleNoise
                )
            );

            sensor.AddObservation(1f);
        }
        else
        {
            sensor.AddObservation(0f);

            sensor.AddObservation(1f);

            sensor.AddObservation(
                AddSignedNoise(
                    lastKnownBallAngle,
                    cameraAngleNoise
                )
            );

            sensor.AddObservation(0f);

            timeSinceBallSeen += Time.fixedDeltaTime;
        }

        //-----------------------------------
        // Camera: Cube (Goal)
        //-----------------------------------

        if (cameraSensor != null && cameraSensor.cubeVisible && Random.value > 0.03f)
        {
            sensor.AddObservation(
                AddSignedNoise(
                    cameraSensor.cubeHorizontalOffset,
                    cameraAngleNoise
                )
            );

            sensor.AddObservation(
                AddNoise(
                    cameraSensor.cubeNormalizedDistance,
                    cameraDistanceNoise
                )
            );

            lastKnownCubeAngle =
                cameraSensor.cubeHorizontalOffset;

            timeSinceCubeSeen = 0f;

            sensor.AddObservation(
                AddSignedNoise(
                    lastKnownCubeAngle,
                    cameraAngleNoise
                )
            );

            sensor.AddObservation(1f);
        }
        else
        {
            sensor.AddObservation(0f);

            sensor.AddObservation(1f);

            sensor.AddObservation(
                AddSignedNoise(
                    lastKnownCubeAngle,
                    cameraAngleNoise
                )
            );

            sensor.AddObservation(0f);

            timeSinceCubeSeen += Time.fixedDeltaTime;
        }

        //-----------------------------------
        // Camera servo
        //-----------------------------------

        sensor.AddObservation(
            AddNoise(
                cameraPan.CurrentAngle / cameraPan.maxAngle,
                servoNoise
            )
        );

        //-----------------------------------
        // Has ball
        //-----------------------------------

        sensor.AddObservation(hasBall ? 1f : 0f);


        //-----------------------------------
        // Speed
        //-----------------------------------

        sensor.AddObservation(
            AddNoise(
                rb.linearVelocity.magnitude / maxRobotSpeed,
                speedNoise
            )
        );

        //-----------------------------------
        // Time without seeing ball
        //-----------------------------------

        sensor.AddObservation(
            Mathf.Clamp01(timeSinceBallSeen / 10f)
        );

        //-----------------------------------
        // Time without seeing cube
        //-----------------------------------

        sensor.AddObservation(
            Mathf.Clamp01(timeSinceCubeSeen / 10f)
        );
    } 

    private float lastCollisionTime;

    private void OnCollisionEnter(Collision collision)
    {
        if (Time.time - lastCollisionTime < 0.3f)
            return;

        if (collision.collider.CompareTag("Obstacle"))
        {
            rewardSystem.ObstacleCollision();
            lastCollisionTime = Time.time;
        }
        if (collision.collider.CompareTag("Goal"))
        {
            rewardSystem.ObstacleCollision();
            lastCollisionTime = Time.time;
        }
        if (collision.collider.CompareTag("TargetBall"))
        {
            rewardSystem.BallCollision();
            lastCollisionTime = Time.time;
        }
    }
    public override void OnActionReceived(ActionBuffers actions)
    {
        //Debug.Log("Action");
        episodeTimer += Time.fixedDeltaTime;
        float rewardBefore = GetCumulativeReward();

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

    //-------------------------------------------------
    // Current state
    //-------------------------------------------------

    hasBall = gripper.HasBall();

    holdTicks = hasBall ? holdTicks + 1 : 0;

    //-------------------------------------------------
    // Base penalty
    //-------------------------------------------------

    rewardSystem.Step();

    //-------------------------------------------------
    // Progress BEFORE pickup
    //-------------------------------------------------

    if (!hasBall && ball != null)
    {
        float distance =
            Vector3.Distance(
                transform.position,
                ball.position
            );

        rewardSystem.BallProgress(distance);

        if (cameraSensor.ballVisible)
        {
            rewardSystem.BallCentered(
                cameraSensor.horizontalOffset
            );
        }
    }

    //-------------------------------------------------
    // Progress AFTER pickup
    //-------------------------------------------------

    if (hasBall && goalCube != null)
    {
        float distance =
            Vector3.Distance(
                transform.position,
                goalCube.position
            );

        rewardSystem.GoalProgress(distance);

        if (cameraSensor.cubeVisible)
        {
            rewardSystem.CubeCentered(
                cameraSensor.cubeHorizontalOffset
            );
        }
    }

    //-------------------------------------------------
    // Walls
    //-------------------------------------------------

    rewardSystem.WallPenalty(
        sensors.ultrasonic,
        sensors.leftIR,
        sensors.rightIR
    );

    //-------------------------------------------------
    // Smooth steering
    //-------------------------------------------------

    rewardSystem.SteeringPenalty(
        Mathf.Abs(steering - previousSteer)
    );

    previousSteer = steering;

    //-------------------------------------------------
    // Pickup
    //-------------------------------------------------

    if (hasBall && !previousHasBall)
    {
        float dist =
            Vector3.Distance(
                transform.position,
                goalCube.position
            );

        rewardSystem.Pickup(dist);
    }

    //-------------------------------------------------
    // Lost ball
    //-------------------------------------------------

    if (!hasBall && previousHasBall)
    {
        rewardSystem.BallLost();
    }

    //-------------------------------------------------
    // Success
    //-------------------------------------------------

    if (hasBall && goalCube != null)
    {
        float distance =
            Vector3.Distance(
                transform.position,
                goalCube.position
            );

        if (distance < 0.55f)
        {
            rewardSystem.GoalReached();
            EndEpisode();
            return;
        }
    }

    //-------------------------------------------------
    // Ball fell
    //-------------------------------------------------

    if (ball != null && ball.position.y < -1f)
    {
        rewardSystem.BallLost();
        EndEpisode();
        return;
    }

    //-------------------------------------------------
    // Robot fell
    //-------------------------------------------------

    if (transform.position.y < -0.2f)
    {
        rewardSystem.Fell();
        EndEpisode();
        return;
    }

    previousHasBall = hasBall;
    
    stepReward = GetCumulativeReward() - rewardBefore;

        // --- ������ ���������� � TENSORBOARD (ML-AGENTS STATS RECORDER) ---
        StatsRecorder statsRecorder = Academy.Instance.StatsRecorder;

        if (statsRecorder != null)
        {
            // 1. ������� (��������� ������ ����� ����� ���)
            statsRecorder.Add("Sensors/Ultrasonic", sensors.ultrasonic);
            statsRecorder.Add("Sensors/IR_Left", sensors.leftIR);
            statsRecorder.Add("Sensors/IR_Right", sensors.rightIR);
            statsRecorder.Add("Sensors/GripperIR", sensors.gripperIR);

            // ������ (��������� ����)
            statsRecorder.Add("Sensors/BallVisible", cameraSensor.ballVisible ? 1f : 0f);
            if (cameraSensor.ballVisible)
            {
                statsRecorder.Add("Sensors/BallAngleOffset", cameraSensor.horizontalOffset);
                statsRecorder.Add("Sensors/BallCameraDistance", cameraSensor.normalizedDistance);
            }
            statsRecorder.Add("Sensors/TrueDistanceToBall", ball != null ? Vector3.Distance(transform.position, ball.position) : -1f);
            statsRecorder.Add("Sensors/CameraYawAngle", cameraPan != null ? cameraPan.CurrentAngle : 0f);

            statsRecorder.Add("Sensors/CubeVisible", cameraSensor.cubeVisible ? 1f : 0f);
            if (cameraSensor.cubeVisible)
            {
                statsRecorder.Add("Sensors/CubeAngleOffset", cameraSensor.cubeHorizontalOffset);
                statsRecorder.Add("Sensors/CubeCameraDistance", cameraSensor.cubeNormalizedDistance);
            }
            statsRecorder.Add("Sensors/TrueDistanceToCube", goalCube != null ? Vector3.Distance(transform.position, goalCube.position) : -1f);

            // 2. ��������� � ���������� (��������� ��������� � �������������)
            statsRecorder.Add("Actuators/Gas_Raw", gas);
            statsRecorder.Add("Actuators/Steering_Raw", steering);
            // ���� � ��� �������� ���������� ������ �� DR (smoothedGas � finalSteer), ���������������� ��:
            // statsRecorder.Add("Actuators/Gas_Smoothed", smoothedGas);
            // statsRecorder.Add("Actuators/Steering_Final", finalSteer);

            // 3. ��������� ������ � ��������� (��� ����� ����� ����������/��������� ������)
            statsRecorder.Add("Agent_State/HasBall", hasBall ? 1f : 0f);
            statsRecorder.Add("Agent_State/HoldTicks", holdTicks);
            //statsRecorder.Add("Agent_State/IsRetryingBackwards", isRetrying ? 1f : 0f); # �� ����� �����-������
            statsRecorder.Add("Agent_State/Speed_MS", rb.linearVelocity.magnitude);

            // ���������� �������� �� ������ (������� ������, ��������� ������ �������)
            statsRecorder.Add("Agent_State/Displacement_X", transform.position.x - startPosition.x);
            statsRecorder.Add("Agent_State/Displacement_Z", transform.position.z - startPosition.z);

            // 4. ����������� ������ (�������� ������, �� ���� ���� ������ ����� ������ ������)
            statsRecorder.Add("Rewards/StepReward", stepReward); // ������� �� ���������� ��� (����� ���� �������������)
            statsRecorder.Add("Rewards/CumulativeReward", GetCumulativeReward()); // ����������� ������� �� ������� ������

            // ���������� �� ������ � ����� (����������, �������� �� �� ��������� �����)
            if (hasBall)
            {
                statsRecorder.Add("Rewards/ReturnDistanceToStart", Vector3.Distance(transform.position, startPosition));
            }

            if (cameraSensor.ballVisible && ball != null)
            {
                float trueDist = Vector3.Distance(transform.position, ball.position);
                float cameraError = Mathf.Abs(cameraSensor.normalizedDistance - Mathf.Clamp01(trueDist / maxArenaDistance));
                statsRecorder.Add("Sensors/CameraDistanceError", cameraError);
            }

            if (cameraSensor.cubeVisible && goalCube != null)
            {
                float trueCubeDist = Vector3.Distance(transform.position, goalCube.position);
                float cubeCameraError = Mathf.Abs(cameraSensor.cubeNormalizedDistance - Mathf.Clamp01(trueCubeDist / maxArenaDistance));
                statsRecorder.Add("Sensors/CubeCameraDistanceError", cubeCameraError);
            }
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