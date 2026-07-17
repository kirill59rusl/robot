using UnityEngine;
using Unity.MLAgents;

public class RewardSystem
{
    private readonly Agent agent;
    private readonly RewardSettings settings;

    public RewardSystem(Agent agent, RewardSettings settings)
    {
        this.agent = agent;
        this.settings = settings;
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

    public void BallVisible(float horizontalOffset)
    {
        agent.AddReward(settings.ballVisibleReward);

        agent.AddReward(
            (1f - Mathf.Abs(horizontalOffset))
            * settings.centeredBallReward
        );
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

    public void Pickup()
    {
        agent.AddReward(settings.pickupReward);
    }

    public void GoalApproach(float distance)
    {
        agent.AddReward(
            (1f - Mathf.Clamp01(distance / 3f))
            * settings.goalApproachReward
        );
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