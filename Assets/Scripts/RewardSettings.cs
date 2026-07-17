using UnityEngine;

[System.Serializable]
public class RewardSettings
{
    [Header("Base")]
    public float stepPenalty = -0.0005f;

    [Header("Ball")]
    public float distanceRewardMultiplier = 0.2f;
    public float ballVisibleReward = 0.0005f;
    public float centeredBallReward = 0.002f;

    [Header("Walls")]
    public float wallPenalty = -0.003f;

    [Header("Driving")]
    public float smoothDrivingPenalty = 0f;

    [Header("Goals")]
    public float pickupReward = 5f;
    public float goalApproachReward = 0.002f;
    public float goalReward = 20f;

    [Header("Episode")]
    public float fallPenalty = -2f;
}