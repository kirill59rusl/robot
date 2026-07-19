using UnityEngine;

[System.Serializable]
public class RewardSettings
{
    [Header("Episode")]
    public float stepPenalty = -0.001f;
    public float fallPenalty = -2.0f;
    public float ballLostPenalty = -1.0f;

    [Header("Main goals")]
    public float pickupReward = 1.0f;
    public float goalReward = 10.0f;

    [Header("Progress")]
    public float ballProgressReward = 0.02f;
    public float homeProgressReward = 0.03f;

    [Header("Vision")]
    public float centeredBallReward = 0.002f;

    [Header("Walls")]
    public float wallNearPenalty = -0.001f;
    public float wallVeryNearPenalty = -0.005f;
    public float wallDangerPenalty = -0.02f;

    [Header("Collisions")]
    public float wallCollisionPenalty = -0.20f;
    public float obstacleCollisionPenalty = -0.15f;
    public float boxCollisionPenalty = -0.10f;

    [Header("Driving")]
    public float steeringPenalty = -0.0005f;
}