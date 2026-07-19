using UnityEngine;
using Unity.MLAgents;

public class RewardSystem
{
    private readonly Agent agent;
    private readonly RewardSettings settings;

    private float bestBallDistance;
    private float bestGoalDistance;

    private bool initialized;

    public RewardSystem(Agent agent, RewardSettings settings)
    {
        this.agent = agent;
        this.settings = settings;
    }

    public void Reset(float distanceToBall, float distanceToGoal)
    {
        bestBallDistance = distanceToBall;
        bestGoalDistance = distanceToGoal;
        initialized = true;
    }

    //-----------------------------------------
    // Base
    //-----------------------------------------

    public void Step()
    {
        agent.AddReward(settings.stepPenalty);
    }

    //-----------------------------------------
    // Progress to ball
    //-----------------------------------------

    public void BallProgress(float currentDistance)
    {
        if (!initialized)
            return;

        if (currentDistance < bestBallDistance)
        {
            float delta = bestBallDistance - currentDistance;

            agent.AddReward(delta * settings.ballProgressReward);

            bestBallDistance = currentDistance;
        }
    }

    //-----------------------------------------
    // Progress to home
    //-----------------------------------------

    public void GoalProgress(float currentDistance)
    {
        if (currentDistance < bestGoalDistance)
        {
            float delta = bestGoalDistance - currentDistance;

            agent.AddReward(delta * settings.homeProgressReward);

            bestGoalDistance = currentDistance;
        }
    }

    //-----------------------------------------
    // Camera
    //-----------------------------------------

    public void BallCentered(float horizontalOffset)
    {
        float reward =
            (1f - Mathf.Abs(horizontalOffset))
            * settings.centeredBallReward;

        agent.AddReward(reward);
    }

    //-----------------------------------------
    // Pickup
    //-----------------------------------------

    public void Pickup(float currentDistanceToGoal)
    {
        agent.AddReward(settings.pickupReward);

        bestGoalDistance = currentDistanceToGoal;
    }

    //-----------------------------------------
    // Goal
    //-----------------------------------------

    public void GoalReached()
    {
        agent.AddReward(settings.goalReward);
    }

    //-----------------------------------------
    // Wall proximity
    //-----------------------------------------

    public void WallPenalty(float front,float left,float right)
    {
        float min =
            Mathf.Min(front,left,right);

        if(min<0.05f)
        {
            agent.AddReward(settings.wallDangerPenalty);
        }
        else
        if(min<0.10f)
        {
            agent.AddReward(settings.wallVeryNearPenalty);
        }
        else
        if(min<0.20f)
        {
            agent.AddReward(settings.wallNearPenalty);
        }
    }

    //-----------------------------------------
    // Smooth driving
    //-----------------------------------------

    public void SteeringPenalty(float actionChange)
    {
        agent.AddReward(
            actionChange *
            settings.steeringPenalty
        );
    }

    //-----------------------------------------
    // Terminal
    //-----------------------------------------

    public void BallLost()
    {
        agent.AddReward(settings.ballLostPenalty);
    }

    public void Fell()
    {
        agent.AddReward(settings.fallPenalty);
    }

    //-----------------------------------------
    // Collisions
    //-----------------------------------------

    public void WallCollision()
    {
        agent.AddReward(settings.wallCollisionPenalty);
    }

    public void ObstacleCollision()
    {
        agent.AddReward(settings.obstacleCollisionPenalty);
    }

    public void BoxCollision()
    {
        agent.AddReward(settings.boxCollisionPenalty);
    }
}