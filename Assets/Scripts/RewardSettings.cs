using UnityEngine;

[System.Serializable]
public class RewardSettings
{
    [Header("Episode")]
    public float stepPenalty = -0.0007f;
    public float fallPenalty = -2.0f;
    public float ballLostPenalty = -8.0f;

    [Header("Main goals")]
    public float pickupReward = 8.0f;
    public float goalReward = 24.0f;

    [Header("Progress")]
    public float ballProgressReward = 0.6f;
    public float homeProgressReward = 2.4f;

    [Header("Vision")]
    public float centeredBallReward = 0.0003f;
    public float centeredCubeReward = 0.0003f;
    
    [Header("Walls")]
    public float wallNearPenalty = -0.001f;
    public float wallVeryNearPenalty = -0.005f;
    public float wallDangerPenalty = -0.02f;

    [Header("Collisions")]
    public float wallCollisionPenalty = -0.20f;
    public float obstacleCollisionPenalty = -8f;
    public float ballCollisionPenalty = -1f;

    [Header("Driving")]
    public float steeringPenalty = -0.0005f;
}