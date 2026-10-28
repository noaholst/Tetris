
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class TetrisAgent : Agent
{
    private bool hasStartedEpisode = false;
    [Header("References")]
    [SerializeField] private GameController gameController;

    [Header("Survival Reward")]
    [SerializeField] private float survivalRewardPerSecond = 0.001f;

    public override void Initialize()
    {
        if (gameController == null)
        {
            gameController = GetComponent<GameController>();
        }

        if (gameController == null)
        {
            Debug.LogError(
                "TetrisAgent requires a GameController reference.",
                this
            );
        }
    }

    public override void OnEpisodeBegin()
{
    if (hasStartedEpisode)
    {
        Debug.Log("[TetrisAgent] Restarting Tetris for a new episode.");
        gameController.Restart();
    }
    else
    {
        hasStartedEpisode = true;
        Debug.Log("[TetrisAgent] First episode started.");
    }
}

    public override void CollectObservations(VectorSensor sensor)
{
    // 1. Board occupancy: 160 observations (8 x 20).
    for (int y = 0; y < Helper.HEIGHT; y++)
    {
        for (int x = 0; x < Helper.WIDTH; x++)
        {
            sensor.AddObservation(
                gameController.IsCellOccupied(x, y) ? 1f : 0f
            );
        }
    }

    // 2. Current falling piece: 8 observations (4 squares x 2 coordinates).
    TetrisBlock currentBlock = gameController.GetCurrentBlock();

    if (currentBlock != null)
    {
        foreach (Transform child in currentBlock.transform)
        {
            int x = Mathf.RoundToInt(child.position.x);
            int y = Mathf.RoundToInt(child.position.y);

            sensor.AddObservation((float)x / (Helper.WIDTH - 1));
            sensor.AddObservation((float)y / (Helper.HEIGHT - 1));
        }
    }
    else
    {
        // Maintain a fixed observation count if no piece exists.
        for (int i = 0; i < 8; i++)
            sensor.AddObservation(0f);
    }

    // 3. Next piece identity: 7 one-hot observations.
    int nextBlock = gameController.GetNextBlock();

    for (int i = 0; i < 7; i++)
    {
        sensor.AddObservation(nextBlock == i ? 1f : 0f);
    }
}
   public void NotifyGameOver()
{
    Debug.Log("[TetrisAgent] Game Over notification received!");

    AddReward(-1.0f);

    Debug.Log($"[TetrisAgent] Final episode reward: {GetCumulativeReward():F4}");

    EndEpisode();
}
   public override void OnActionReceived(ActionBuffers actions)
{
    int action = actions.DiscreteActions[0];

    gameController.ApplyAction(action);
}
    public void RewardSurvivalTime(float elapsedSeconds)
    {
        if (elapsedSeconds > 0f)
        {
            AddReward(
                survivalRewardPerSecond * elapsedSeconds
            );
        }
    }
}
