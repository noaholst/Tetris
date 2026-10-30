
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
    
    [Header("Line Clear Rewards")]
    [SerializeField] private float singleLineReward = 0.5f;
    [SerializeField] private float doubleLineReward = 1.5f;
    [SerializeField] private float tripleLineReward = 3.0f;
    [SerializeField] private float tetrisReward = 5.0f;
    
    [Header("Board Quality Penalties")]
    [Tooltip("Penalty applied for each row that the maximum stack height increases.")]
    [SerializeField] private float heightPenaltyPerRow = 0.02f;

    [Tooltip("Penalty applied for each newly created hole.")]
    [SerializeField] private float holePenalty = 0.2f;

    private int previousMaxHeight;
    private int previousHoleCount;

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
    
    ResetBoardQualityTracking();
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

    for (int i = 0; i < 5; i++)
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
    
    public void RewardLinesCleared(int lines)
    {
        switch (lines)
        {
            case 1:
                AddReward(singleLineReward);
                break;

            case 2:
                AddReward(doubleLineReward);
                break;

            case 3:
                AddReward(tripleLineReward);
                break;

            case 4:
                AddReward(tetrisReward);
                break;
        }
    }
    
    public void ResetBoardQualityTracking()
    {
        if (gameController == null)
        {
            previousMaxHeight = 0;
            previousHoleCount = 0;
            return;
        }

        previousMaxHeight = CalculateMaxHeight();
        previousHoleCount = CalculateHoleCount();
    }
    
    private int CalculateMaxHeight()
    {
        int maxHeight = 0;

        for (int x = 0; x < Helper.WIDTH; x++)
        {
            for (int y = Helper.HEIGHT - 1; y >= 0; y--)
            {
                if (gameController.IsCellOccupied(x, y))
                {
                    maxHeight = Mathf.Max(maxHeight, y + 1);
                    break;
                }
            }
        }

        return maxHeight;
    }
    
    private int CalculateHoleCount()
    {
        int holes = 0;

        for (int x = 0; x < Helper.WIDTH; x++)
        {
            bool foundBlockAbove = false;

            for (int y = Helper.HEIGHT - 1; y >= 0; y--)
            {
                if (gameController.IsCellOccupied(x, y))
                {
                    foundBlockAbove = true;
                }
                else if (foundBlockAbove)
                {
                    holes++;
                }
            }
        }

        return holes;
    }
    
    public void EvaluateBoardQuality()
    {
        if (gameController == null)
            return;

        int currentHeight = CalculateMaxHeight();
        int currentHoles = CalculateHoleCount();

        // Penalize only increases in the maximum stack height.
        int heightIncrease = currentHeight - previousMaxHeight;

        if (heightIncrease > 0)
            AddReward(-heightIncrease * heightPenaltyPerRow);

        // Penalize only holes newly created by this placement.
        int newHoles = currentHoles - previousHoleCount;

        if (newHoles > 0)
            AddReward(-newHoles * holePenalty);

        previousMaxHeight = currentHeight;
        previousHoleCount = currentHoles;
    }
}
