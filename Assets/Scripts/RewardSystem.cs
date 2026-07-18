using UnityEngine;
using Unity.MLAgents;

public class RewardSystem
{
    private readonly Agent agent;
    private readonly RewardSettings settings;

    private float lastDistanceToBall = float.MaxValue;
    private float lastDistanceToGoal = float.MaxValue;

    private float previousTurnInput = 0f;

    public RewardSystem(Agent agent, RewardSettings settings)
    {
        this.agent = agent;
        this.settings = settings;
    }

    /// <summary>
    /// Вызывать в начале каждого эпизода.
    /// </summary>
    public void Reset()
    {
        lastDistanceToBall = float.MaxValue;
        lastDistanceToGoal = float.MaxValue;
        previousTurnInput = 0f;
    }

    public void StepPenalty()
    {
        agent.AddReward(settings.stepPenalty);
    }

    public void DistanceReward(float previousDistance, float currentDistance)
    {
        float delta = previousDistance - currentDistance;
        agent.AddReward(delta * settings.distanceRewardMultiplier);
    }

    public void BallVisible(float horizontalOffset, float currentDistance)
    {
        if (currentDistance < lastDistanceToBall - 0.01f)
        {
            float improvement =
                (lastDistanceToBall - currentDistance) /
                Mathf.Max(lastDistanceToBall, 0.001f);

            agent.AddReward(
                improvement * settings.distanceRewardMultiplier * 2f
            );

            agent.AddReward(
                (1f - Mathf.Abs(horizontalOffset))
                * settings.centeredBallReward
                * 0.5f
            );
        }
        else if (currentDistance > lastDistanceToBall + 0.05f)
        {
            agent.AddReward(-0.001f);
        }

        lastDistanceToBall = currentDistance;
    }

    /// <summary>
    /// Вызывать только после того как робот взял мяч.
    /// </summary>
    public void GoalApproach(float currentDistance)
    {
        if (lastDistanceToGoal == float.MaxValue)
        {
            lastDistanceToGoal = currentDistance;
            return;
        }

        float delta =
    Mathf.Clamp(lastDistanceToGoal - currentDistance,
                -0.1f,
                 0.1f);

        agent.AddReward(delta * settings.goalApproachReward);

        if (delta < -0.02f)
        {
            agent.AddReward(-0.001f);
        }

        lastDistanceToGoal = currentDistance;
    }

    public void WallPenalty(float ultrasonic, float leftIR, float rightIR)
    {
        if (ultrasonic < 0.20f)
            agent.AddReward(settings.wallPenalty);

        if (leftIR < 0.10f)
            agent.AddReward(settings.wallPenalty);

        if (rightIR < 0.10f)
            agent.AddReward(settings.wallPenalty);
    }

    public void SmoothDriving(float change)
    {
        agent.AddReward(change * settings.smoothDrivingPenalty);
    }

    /// <summary>
    /// Штраф за вращение на месте.
    /// </summary>
    public void RotationPenalty(float linearSpeed, float angularSpeed)
    {
        if (linearSpeed < 0.05f && Mathf.Abs(angularSpeed) > 0.4f)
        {
            agent.AddReward(-0.001f);
        }
    }
    
    /// <summary>
    /// Штраф за резкую смену направления поворота.
    /// </summary>
    public void SteeringSwitchPenalty(float currentTurnInput)
    {
        if (Mathf.Abs(currentTurnInput) > 0.5f &&
            Mathf.Abs(previousTurnInput) > 0.5f &&
            Mathf.Sign(currentTurnInput) != Mathf.Sign(previousTurnInput))
        {
            agent.AddReward(-0.001f);
        }

        previousTurnInput = currentTurnInput;
    }

    public void Pickup()
    {
        agent.AddReward(settings.pickupReward);

        lastDistanceToGoal = float.MaxValue;
    }

    public void GoalReached()
    {
        agent.AddReward(settings.goalReward);
    }

    public void Fell()
    {
        agent.AddReward(settings.fallPenalty);
    }
}