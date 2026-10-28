using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

using Random = UnityEngine.Random;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;
using Mode = Helper.Mode;


public class GameController : MonoBehaviour
{
    // ============================================================
    // GAME CONFIGURATION
    // ============================================================

    private readonly string STAGES_PATH = "Assets/Stages/";

    private readonly int[] scores =
    {
        0,
        40,
        100,
        300,
        1200
    };

    private readonly int NUM_OF_STAGES = 20;

    [Header("Timing")]
    public float fallTime = 0.8f;

    [Tooltip("Base falling speed. The actual fallTime is calculated from this.")]
    [SerializeField]
    private float baseFallTime = 0.8f;

    [Tooltip("Duration of the line-clearing visual animation.")]
    [SerializeField]
    private float lineClearDuration = 0.3f;

    [Header("Board")]
    private float N = 20;

    public Vector3 startPos = new Vector3();

    private readonly Vector3[] Pivots =
    {
        new Vector3(-0.33f, 0f, 0f),
        new Vector3(-0.27f, -0.15f, 0f),
        new Vector3(-0.27f, 0.1f, 0f),
        new Vector3(-0.12f, -0.1f, 0f),
        new Vector3(-0.22f, -0.1f, 0f),
        new Vector3(-0.02f, -0.1f, 0f),
        new Vector3(-0.2f, 0.1f, 0f)
    };


    // ============================================================
    // GAME STATE
    // ============================================================

    private float fallTimer = 0f;
    private float playTime = 0f;

    private int score = 0;
    private int linesDeleted = 0;
    private int numGems = 0;

    private int nextLevel = 1;
    private int currentLevel = 1;

    private List<int> deletingRow = new List<int>();

    private int currStage = 0;

    private HashSet<int> deck = new HashSet<int>();

    private Block[,] grid =
        new Block[Helper.HEIGHT, Helper.WIDTH];


    // ============================================================
    // PREFABS / OBJECTS
    // ============================================================

    public TetrisBlock[] Blocks;

    public GhostBlock[] Ghosts;

    private int nextBlock;

    public TetrisBlock nextBlockObject;

    public TetrisBlock currBlock;

    public TetrisBlock deadBlock;

    public GameObject nextBlockBackground;
    public GameObject infoText;
    public GameObject restartButton;
    public GameObject resumeButton;
    public GameObject pauseButton;
    public GameObject speakerButton;
    public GameObject muteButton;

    public GemBlock gemBlock;

    private GhostBlock ghostBlock;


    // ============================================================
    // GAME FLAGS
    // ============================================================

    private bool hardDropped;
    private bool gameOver;
    private bool gameClear;

    private bool isDestroying;
    private bool isPaused;
    private bool isShowingAnimation;
    private bool isRowDown;
    private bool isAnimating;
    private bool isEndTurn;


    // ============================================================
    // CONTROLLER / UI
    // ============================================================

    private ModeController controller;

    public Text timeValue;
    public Text levelValue;
    public Text linesValue;
    public Text stageValue;
    public Text scoreValue;
    public Text gameModeValue;


    // ============================================================
    // FIXED-STEP STATE MACHINE
    // ============================================================

    private enum SimulationState
    {
        Initializing,
        PlayerTurn,
        ClearingLines,
        MovingRows,
        SpawningBlock,
        GameOver,
        GameClear,
        GameClearCountdown,
        Paused
    }

    private SimulationState simulationState =
        SimulationState.Initializing;


    // ============================================================
    // LINE CLEAR STATE
    // ============================================================

    private float lineClearTimer = 0f;


    // ============================================================
    // GAME CLEAR COUNTDOWN
    // ============================================================

    private float gameClearTimer = 0f;

    private int gameClearCountdownStep = 0;


    // ============================================================
    // OPTIONAL KEYBOARD CONTROL
    // ============================================================

    [Header("RL / Input")]

    [Tooltip(
        "When enabled, keyboard input can be used to test the game. " +
        "For pure RL training, disable this."
    )]
    public bool enableKeyboardInput = true;

    private bool previousUpInput;
    private bool previousSpaceInput;
    private bool previousPInput;


    // ============================================================
    // RL ACTIONS
    // ============================================================

    /*
     * Action mapping:
     *
     * 0 = Nothing
     * 1 = Move Left
     * 2 = Move Right
     * 3 = Rotate
     * 4 = Soft Drop
     * 5 = Hard Drop
     *
     * An ML-Agent can call:
     *
     *     gameController.ApplyAction(action);
     *
     * once per decision.
     */

    public const int ACTION_NONE = 0;
    public const int ACTION_LEFT = 1;
    public const int ACTION_RIGHT = 2;
    public const int ACTION_ROTATE = 3;
    public const int ACTION_SOFT_DROP = 4;
    public const int ACTION_HARD_DROP = 5;


    // ============================================================
    // START
    // ============================================================

    void Start()
    {
        if (baseFallTime <= 0f)
            baseFallTime = fallTime;

        if (muteButton != null)
            muteButton.SetActive(true);

        if (speakerButton != null)
            speakerButton.SetActive(false);

        InitGame();
    }


    // ============================================================
    // FIXED UPDATE
    // ============================================================

    void FixedUpdate()
    {
        float fixedDeltaTime = Time.fixedDeltaTime;

        switch (simulationState)
        {
            case SimulationState.Initializing:

                break;


            case SimulationState.PlayerTurn:

                ProcessPlayerTurn(fixedDeltaTime);

                break;


            case SimulationState.ClearingLines:

                ProcessLineClear(fixedDeltaTime);

                break;


            case SimulationState.MovingRows:

                ProcessRowMovement();

                break;


            case SimulationState.SpawningBlock:

                SpawnNextBlock();

                break;


            case SimulationState.GameClearCountdown:

                ProcessGameClearCountdown(fixedDeltaTime);

                break;


            case SimulationState.GameOver:

                break;


            case SimulationState.GameClear:

                break;


            case SimulationState.Paused:

                ProcessPauseInput();

                break;
        }
    }


    // ============================================================
    // INITIALIZE GAME
    // ============================================================

    private void InitGame()
    {
        simulationState = SimulationState.Initializing;

        FindObjectOfType<AudioManager>().Play("GameStart");

        controller =
            GameObject.FindWithTag("ModeController")
                .GetComponent<ModeController>();

        if (gameModeValue != null)
        {
            gameModeValue.text =
                (controller.GetMode() == Mode.stage
                    ? "S T A G E"
                    : "I N F I N I T E")
                + "  M O D E";
        }

        if (infoText != null)
            infoText.SetActive(false);

        if (restartButton != null)
            restartButton.SetActive(false);

        if (resumeButton != null)
            resumeButton.SetActive(false);

        gameOver = false;
        gameClear = false;

        isShowingAnimation = false;
        isEndTurn = false;
        isAnimating = false;
        isDestroying = false;
        isRowDown = false;
        isPaused = false;

        hardDropped = false;

        fallTimer = 0f;
        playTime = 0f;
        lineClearTimer = 0f;
        gameClearTimer = 0f;

        linesDeleted = 0;
        score = 0;
        numGems = 0;

        currentLevel = 1;
        nextLevel = 1;

        fallTime = baseFallTime;

        deletingRow.Clear();

        previousUpInput = false;
        previousSpaceInput = false;
        previousPInput = false;

        ClearBoard();

        if (currBlock != null)
        {
            currBlock.Destroy();
            currBlock = null;
        }

        if (ghostBlock != null)
        {
            ghostBlock.Destroy();
            ghostBlock = null;
        }

        NextBlock();

        if (controller.GetMode() == Mode.stage)
        {
            SetStage();
        }

        simulationState = SimulationState.SpawningBlock;

        UpdateUI();
    }


    // ============================================================
    // CLEAR BOARD
    // ============================================================

    private void ClearBoard()
    {
        for (int y = 0; y < Helper.HEIGHT; y++)
        {
            for (int x = 0; x < Helper.WIDTH; x++)
            {
                if (grid[y, x] != null)
                {
                    grid[y, x].Destroy();
                    grid[y, x] = null;
                }
            }
        }
    }


    // ============================================================
    // PAUSE
    // ============================================================

    public void Pause()
    {
        if (gameOver || gameClear)
            return;

        isPaused = true;

        simulationState = SimulationState.Paused;

        if (pauseButton != null)
            pauseButton.SetActive(false);

        if (resumeButton != null)
            resumeButton.SetActive(true);

        FindObjectOfType<AudioManager>()
            .Mute("GameStart", true);
    }


    public void Resume()
    {
        if (!isPaused)
            return;

        isPaused = false;

        simulationState = SimulationState.PlayerTurn;

        if (resumeButton != null)
            resumeButton.SetActive(false);

        if (pauseButton != null)
            pauseButton.SetActive(true);

        FindObjectOfType<AudioManager>()
            .Mute("GameStart", false);
    }


    private void ProcessPauseInput()
    {
        if (!enableKeyboardInput)
            return;

        bool p = Input.GetKey(KeyCode.P);

        if (p && !previousPInput)
        {
            Resume();
        }

        previousPInput = p;
    }


    // ============================================================
    // MUTE
    // ============================================================

    public void Mute(bool isMute)
    {
        FindObjectOfType<AudioManager>()
            .Mute("GameStart", isMute);

        if (isMute)
        {
            if (muteButton != null)
                muteButton.SetActive(false);

            if (speakerButton != null)
                speakerButton.SetActive(true);
        }
        else
        {
            if (muteButton != null)
                muteButton.SetActive(true);

            if (speakerButton != null)
                speakerButton.SetActive(false);
        }
    }


    // ============================================================
    // NEXT BLOCK
    // ============================================================

    private void NextBlock()
    {
        if (Blocks == null || Blocks.Length == 0)
            return;

        if (deck.Count == Blocks.Length)
            deck.Clear();

        do
        {
            nextBlock = Random.Range(0, Blocks.Length);
        }
        while (deck.Contains(nextBlock));

        deck.Add(nextBlock);

        if (nextBlockObject != null)
        {
            nextBlockObject.Destroy();
            nextBlockObject = null;
        }

        nextBlockObject =
            Instantiate(Blocks[nextBlock]);

        if (nextBlockBackground != null)
        {
            nextBlockObject.transform.parent =
                nextBlockBackground.transform;

            nextBlockObject.transform.localPosition =
                Pivots[nextBlock];
        }
    }


    // ============================================================
    // SET STAGE
    // ============================================================

    private void SetStage()
    {
        numGems = 0;

        for (int y = 0; y < Helper.HEIGHT; y++)
        {
            for (int x = 0; x < Helper.WIDTH; x++)
            {
                if (grid[y, x] != null)
                {
                    grid[y, x].Destroy();
                    grid[y, x] = null;
                }

                int blockType =
                    Helper.Stages[
                        currStage,
                        Helper.HEIGHT - y - 1,
                        x
                    ];

                switch (blockType)
                {
                    case 0:

                        grid[y, x] = null;

                        break;


                    case 1:

                        grid[y, x] =
                            Instantiate(
                                deadBlock,
                                new Vector3(x, y, 0),
                                Quaternion.identity
                            );

                        break;


                    case 2:

                        numGems++;

                        grid[y, x] =
                            Instantiate(
                                gemBlock,
                                new Vector3(x, y, 0),
                                Quaternion.identity
                            );

                        break;
                }
            }
        }
    }


    // ============================================================
    // PLAYER TURN
    // ============================================================

    private void ProcessPlayerTurn(float fixedDeltaTime)
    {
        if (gameOver || gameClear || isPaused)
            return;

        ProcessKeyboardInput();

        ProcessFalling(fixedDeltaTime);

        playTime += fixedDeltaTime;
        // Award survival reward while actively playing.
        TetrisAgent agent = GetComponent<TetrisAgent>();

        if (agent != null)
        {
            agent.RewardSurvivalTime(fixedDeltaTime);
        }   
        UpdateLevel();

        UpdateTimeUI();
        GhostBlockImgUpdate();
        InfoUpdate();
    }


    // ============================================================
    // KEYBOARD INPUT
    // ============================================================

    private void ProcessKeyboardInput()
    {
        if (!enableKeyboardInput)
            return;

        if (Input.GetKey(KeyCode.LeftArrow))
        {
            MoveLeft();
        }

        if (Input.GetKey(KeyCode.RightArrow))
        {
            MoveRight();
        }

        bool up =
            Input.GetKey(KeyCode.UpArrow);

        if (up && !previousUpInput)
        {
            RotateBlock();
        }

        bool space =
            Input.GetKey(KeyCode.Space);

        if (space && !previousSpaceInput)
        {
            HardDrop();
        }

        bool p =
            Input.GetKey(KeyCode.P);

        if (p && !previousPInput)
        {
            Pause();
        }

        previousUpInput = up;
        previousSpaceInput = space;
        previousPInput = p;
    }


    // ============================================================
    // RL ACTION INTERFACE
    // ============================================================

    public void ApplyAction(int action)
    {
        if (!CanAcceptAction())
            return;

        switch (action)
        {
            case ACTION_NONE:

                break;


            case ACTION_LEFT:

                MoveLeft();

                break;


            case ACTION_RIGHT:

                MoveRight();

                break;


            case ACTION_ROTATE:

                RotateBlock();

                break;


            case ACTION_SOFT_DROP:

                SoftDrop();

                break;


            case ACTION_HARD_DROP:

                HardDrop();

                break;
        }
    }


    // ============================================================
    // ACTION VALIDATION
    // ============================================================

    private bool CanAcceptAction()
    {
        return
            simulationState == SimulationState.PlayerTurn &&
            !gameOver &&
            !gameClear &&
            !isPaused &&
            !isShowingAnimation &&
            !isEndTurn &&
            currBlock != null;
    }


    // ============================================================
    // RL ACTION: LEFT
    // ============================================================

    public void MoveLeft()
    {
        if (!CanAcceptAction())
            return;

        HorizontalMove(Vector3.left);
    }


    // ============================================================
    // RL ACTION: RIGHT
    // ============================================================

    public void MoveRight()
    {
        if (!CanAcceptAction())
            return;

        HorizontalMove(Vector3.right);
    }


    // ============================================================
    // RL ACTION: ROTATE
    // ============================================================

    public void RotateBlock()
    {
        if (!CanAcceptAction())
            return;

        Rotate();
    }


    // ============================================================
    // RL ACTION: SOFT DROP
    // ============================================================

    public void SoftDrop()
    {
        if (!CanAcceptAction())
            return;

        VerticalMove(Vector3.down);
    }


    // ============================================================
    // RL ACTION: HARD DROP
    // ============================================================

    public void HardDrop()
    {
        if (!CanAcceptAction())
            return;

        if (hardDropped)
            return;

        hardDropped = true;

        while (true)
        {
            currBlock.transform.position += Vector3.down;

            if (!ValidMove(currBlock.transform))
            {
                currBlock.transform.position += Vector3.up;

                LockCurrentBlock();

                break;
            }
        }
    }


    // ============================================================
    // AUTOMATIC FALLING
    // ============================================================

    private void ProcessFalling(float fixedDeltaTime)
    {
        bool softDrop =
            enableKeyboardInput &&
            Input.GetKey(KeyCode.DownArrow);

        float currentFallTime =
            softDrop
                ? fallTime / 10f
                : fallTime;

        if (currentFallTime <= 0f)
            currentFallTime = 0.01f;

        fallTimer += fixedDeltaTime;

        if (fallTimer >= currentFallTime)
        {
            fallTimer -= currentFallTime;

            VerticalMove(Vector3.down);
        }
    }


    // ============================================================
    // LEVEL
    // ============================================================

    private void UpdateLevel()
    {
        nextLevel =
            Mathf.FloorToInt(linesDeleted / N) + 1;

        if (nextLevel < 1)
            nextLevel = 1;

        if (nextLevel != currentLevel)
        {
            currentLevel = nextLevel;

            fallTime =
                baseFallTime /
                (
                    1f +
                    ((currentLevel - 1) * 0.1f)
                );
        }
    }


    // ============================================================
    // UI
    // ============================================================

    private void UpdateUI()
    {
        InfoUpdate();
        UpdateTimeUI();
    }


    private void InfoUpdate()
    {
        if (levelValue != null)
            levelValue.text = nextLevel.ToString();

        if (linesValue != null)
            linesValue.text = linesDeleted.ToString();

        if (stageValue != null)
            stageValue.text = (currStage + 1).ToString();

        if (scoreValue != null)
            scoreValue.text = score.ToString();
    }


    private void UpdateTimeUI()
    {
        if (timeValue == null)
            return;

        int totalSeconds =
            Mathf.FloorToInt(playTime);

        int minutes =
            totalSeconds / 60;

        int seconds =
            totalSeconds % 60;

        int milliseconds =
            Mathf.FloorToInt(
                (playTime - totalSeconds) * 1000f
            );

        timeValue.text =
            string.Format(
                "{0:00}:{1:00}:{2:000}",
                minutes,
                seconds,
                milliseconds
            );
    }


    // ============================================================
    // GHOST
    // ============================================================

    private void GhostBlockImgUpdate()
    {
        if (ghostBlock == null ||
            currBlock == null)
            return;

        if (!ghostBlock.IsDestroyed())
        {
            ghostBlock.transform.position =
                GhostPosition(
                    currBlock.transform.position
                );
        }
    }


    // ============================================================
    // ROTATE
    // ============================================================

    private void Rotate()
    {
        if (currBlock == null)
            return;

        Transform currTransform =
            currBlock.transform;

        currTransform.RotateAround(
            currTransform.TransformPoint(
                currBlock.rotationPoint
            ),
            Vector3.forward,
            90
        );

        if (ghostBlock != null)
        {
            ghostBlock.transform.RotateAround(
                ghostBlock.transform.TransformPoint(
                    currBlock.rotationPoint
                ),
                Vector3.forward,
                90
            );
        }

        if (!ValidMove(currBlock.transform))
        {
            currTransform.RotateAround(
                currTransform.TransformPoint(
                    currBlock.rotationPoint
                ),
                Vector3.forward,
                -90
            );

            if (ghostBlock != null)
            {
                ghostBlock.transform.RotateAround(
                    ghostBlock.transform.TransformPoint(
                        currBlock.rotationPoint
                    ),
                    Vector3.forward,
                    -90
                );
            }
        }
    }


    // ============================================================
    // HORIZONTAL MOVE
    // ============================================================

    private void HorizontalMove(Vector3 nextMove)
    {
        if (currBlock == null)
            return;

        currBlock.transform.position += nextMove;

        if (!ValidMove(currBlock.transform))
        {
            currBlock.transform.position -= nextMove;
        }
    }


    // ============================================================
    // VERTICAL MOVE
    // ============================================================

    private void VerticalMove(Vector3 nextMove)
    {
        if (currBlock == null)
            return;

        currBlock.transform.position += nextMove;

        if (!ValidMove(currBlock.transform))
        {
            currBlock.transform.position -= nextMove;

            LockCurrentBlock();
        }
    }


    // ============================================================
    // LOCK CURRENT BLOCK
    // ============================================================

    private void LockCurrentBlock()
    {
        if (currBlock == null)
            return;

        CreateDeadBlock();

        DestroyCurrBlock();

        CheckForLines();
    }


    // ============================================================
    // CREATE DEAD BLOCK
    // ============================================================

    private void CreateDeadBlock()
    {
        foreach (Transform children in currBlock.transform)
        {
            int roundedY =
                Mathf.RoundToInt(
                    children.transform.position.y
                );

            int roundedX =
                Mathf.RoundToInt(
                    children.transform.position.x
                );

            if (
                roundedY < 0 ||
                roundedY >= Helper.HEIGHT ||
                roundedX < 0 ||
                roundedX >= Helper.WIDTH
            )
            {
                continue;
            }

            SpriteRenderer renderer =
                children.GetComponent<SpriteRenderer>();

            if (renderer == null)
                continue;

            Color currColor =
                renderer.color;

            if (grid[roundedY, roundedX] == null)
            {
                TetrisBlock curr =
                    Instantiate(
                        deadBlock,
                        new Vector3(
                            roundedX,
                            roundedY,
                            0
                        ),
                        Quaternion.identity
                    );

                curr.sprite
                    .GetComponent<SpriteRenderer>()
                    .color = currColor;

                grid[roundedY, roundedX] = curr;
            }
        }
    }


    // ============================================================
    // DESTROY CURRENT BLOCK
    // ============================================================

    private void DestroyCurrBlock()
    {
        if (currBlock != null)
        {
            currBlock.Destroy();
            currBlock = null;
        }

        if (ghostBlock != null)
        {
            ghostBlock.Destroy();
            ghostBlock = null;
        }
    }


    // ============================================================
    // CHECK FOR LINES
    // ============================================================

    private void CheckForLines()
    {
        deletingRow.Clear();

        for (
            int y = Helper.HEIGHT - 1;
            y >= 0;
            y--
        )
        {
            if (HasLine(y))
            {
                deletingRow.Add(y);
            }
        }

        if (deletingRow.Count == 0)
        {
            isAnimating = false;
            isShowingAnimation = false;
            isEndTurn = false;

            simulationState =
                SimulationState.SpawningBlock;

            return;
        }

        int lineCount =
            deletingRow.Count;

        score +=
            scores[lineCount] *
            Mathf.FloorToInt(
                (linesDeleted / N) + 1
            );

        linesDeleted += lineCount;

        EndTurn();
    }


    // ============================================================
    // HAS LINE
    // ============================================================

    private bool HasLine(int y)
    {
        for (int x = 0; x < Helper.WIDTH; x++)
        {
            if (grid[y, x] == null)
                return false;
        }

        return true;
    }


    // ============================================================
    // BEGIN END TURN
    // ============================================================

    private void EndTurn()
    {
        isEndTurn = true;
        isAnimating = true;
        isShowingAnimation = true;

        lineClearTimer = 0f;

        simulationState =
            SimulationState.ClearingLines;

        FindObjectOfType<AudioManager>()
            .Play("Blip");
    }


    // ============================================================
    // PROCESS LINE CLEAR
    // ============================================================

    private void ProcessLineClear(float fixedDeltaTime)
    {
        if (!isShowingAnimation)
        {
            simulationState =
                SimulationState.MovingRows;

            return;
        }

        lineClearTimer += fixedDeltaTime;

        float progress =
            Mathf.Clamp01(
                lineClearTimer /
                lineClearDuration
            );

        float alpha =
            1f - progress;

        foreach (int y in deletingRow)
        {
            for (int x = 0; x < Helper.WIDTH; x++)
            {
                Block block =
                    grid[y, x];

                if (block == null)
                    continue;

                if (block.sprite == null)
                    continue;

                SpriteRenderer renderer =
                    block.sprite
                        .GetComponent<SpriteRenderer>();

                if (renderer == null)
                    continue;

                Color color =
                    renderer.color;

                renderer.color =
                    new Color(
                        color.r,
                        color.g,
                        color.b,
                        alpha
                    );
            }
        }

        if (progress >= 1f)
        {
            isDestroying = true;

            RemoveClearedRows();

            isDestroying = false;

            isShowingAnimation = false;

            simulationState =
                SimulationState.MovingRows;
        }
    }


    // ============================================================
    // REMOVE CLEARED ROWS
    // ============================================================

    private void RemoveClearedRows()
    {
        bool[] deleted =
            new bool[Helper.HEIGHT];

        foreach (int y in deletingRow)
        {
            if (
                y >= 0 &&
                y < Helper.HEIGHT
            )
            {
                deleted[y] = true;
            }
        }


        // --------------------------------------------------------
        // Destroy blocks belonging to deleted rows.
        // --------------------------------------------------------

        for (int y = 0; y < Helper.HEIGHT; y++)
        {
            if (!deleted[y])
                continue;

            for (int x = 0; x < Helper.WIDTH; x++)
            {
                if (grid[y, x] != null)
                {
                    grid[y, x].Destroy();
                    grid[y, x] = null;
                }
            }
        }


        // --------------------------------------------------------
        // Compact all remaining rows downward.
        //
        // This is deterministic and also correctly handles
        // multiple lines being removed simultaneously.
        // --------------------------------------------------------

        int writeY = 0;

        for (int readY = 0;
             readY < Helper.HEIGHT;
             readY++)
        {
            if (deleted[readY])
                continue;

            for (int x = 0;
                 x < Helper.WIDTH;
                 x++)
            {
                Block block =
                    grid[readY, x];

                grid[readY, x] = null;

                if (block == null)
                    continue;

                grid[writeY, x] = block;

                block.transform.position =
                    new Vector3(
                        x,
                        writeY,
                        0
                    );
            }

            writeY++;
        }


        // --------------------------------------------------------
        // Clear remaining cells above the compacted board.
        // --------------------------------------------------------

        for (int y = writeY;
             y < Helper.HEIGHT;
             y++)
        {
            for (int x = 0;
                 x < Helper.WIDTH;
                 x++)
            {
                grid[y, x] = null;
            }
        }


        // --------------------------------------------------------
        // Recalculate gems.
        // --------------------------------------------------------

        numGems = 0;

        for (int y = 0;
             y < Helper.HEIGHT;
             y++)
        {
            for (int x = 0;
                 x < Helper.WIDTH;
                 x++)
            {
                if (grid[y, x] == null)
                    continue;

                if (
                    grid[y, x]
                        .GetComponent<GemBlock>() != null
                )
                {
                    numGems++;
                }
            }
        }

        deletingRow.Clear();

        isRowDown = false;
    }


    // ============================================================
    // PROCESS ROW MOVEMENT
    // ============================================================

    private void ProcessRowMovement()
    {
        isRowDown = false;
        isAnimating = false;
        isEndTurn = false;

        simulationState =
            SimulationState.SpawningBlock;
    }


    // ============================================================
    // SPAWN NEXT BLOCK
    // ============================================================

    private void SpawnNextBlock()
    {
        NewBlock();
    }


    // ============================================================
    // NEW BLOCK
    // ============================================================

    private void NewBlock()
    {
        if (Blocks == null ||
            Blocks.Length == 0)
        {
            return;
        }

        currBlock =
            Instantiate(
                Blocks[nextBlock],
                startPos,
                Quaternion.identity
            );

        NewGhost();

        NextBlock();

        hardDropped = false;
        fallTimer = 0f;

        isShowingAnimation = false;
        isEndTurn = false;
        isAnimating = false;

        // --------------------------------------------------------
        // Check game over.
        // --------------------------------------------------------

        int checkY = 18;
        int checkX = 4;

        if (
            checkY >= 0 &&
            checkY < Helper.HEIGHT &&
            checkX >= 0 &&
            checkX < Helper.WIDTH &&
            grid[checkY, checkX] != null
        )
        {
            gameOver = true;

            GameOver();

            return;
        }


        // --------------------------------------------------------
        // Check stage completion.
        // --------------------------------------------------------

        if (
            controller.GetMode() == Mode.stage &&
            numGems == 0
        )
        {
            gameClear = true;

            GameClear();

            return;
        }

        simulationState =
            SimulationState.PlayerTurn;
    }


    // ============================================================
    // NEW GHOST
    // ============================================================

    private void NewGhost()
    {
        if (ghostBlock != null)
        {
            ghostBlock.Destroy();
            ghostBlock = null;
        }

        ghostBlock =
            Instantiate(
                Ghosts[nextBlock],
                currBlock.transform.position,
                Quaternion.identity
            );
    }


    // ============================================================
    // GAME OVER
    // ============================================================

    private void GameOver()
    {
        simulationState =
            SimulationState.GameOver;

        if (ghostBlock != null)
        {
            ghostBlock.Destroy();
            ghostBlock = null;
        }

        if (infoText != null)
        {
            infoText.SetActive(true);

            TextMeshProUGUI text =
                infoText
                    .GetComponent<TextMeshProUGUI>();

            if (text != null)
                text.text = "GAME OVER";
        }

        FindObjectOfType<AudioManager>()
            .Stop("GameStart");

        if (restartButton != null)
            restartButton.SetActive(true);

        UpdateUI();

    // Notify the ML-Agents agent that the game has ended.
    TetrisAgent agent = GetComponent<TetrisAgent>();

    if (agent != null)
    {
        agent.NotifyGameOver();
    }
    }


    // ============================================================
    // GAME CLEAR
    // ============================================================

    private void GameClear()
    {
        simulationState =
            SimulationState.GameClearCountdown;

        if (ghostBlock != null)
        {
            ghostBlock.Destroy();
            ghostBlock = null;
        }

        if (infoText != null)
        {
            infoText.SetActive(true);

            TextMeshProUGUI text =
                infoText
                    .GetComponent<TextMeshProUGUI>();

            if (text != null)
                text.text = "GAME CLEAR";
        }

        FindObjectOfType<AudioManager>()
            .Stop("GameStart");

        FindObjectOfType<AudioManager>()
            .Play("GameClear");

        gameClearTimer = 0f;
        gameClearCountdownStep = 0;
    }


    // ============================================================
    // FIXED-STEP GAME CLEAR COUNTDOWN
    // ============================================================

    private void ProcessGameClearCountdown(
        float fixedDeltaTime
    )
    {
        gameClearTimer += fixedDeltaTime;

        if (gameClearCountdownStep == 0)
        {
            if (gameClearTimer >= 0.5f)
            {
                gameClearTimer = 0f;
                gameClearCountdownStep = 1;

                SetInfoText("3");
            }

            return;
        }


        if (gameClearCountdownStep == 1)
        {
            if (gameClearTimer >= 0.5f)
            {
                gameClearTimer = 0f;
                gameClearCountdownStep = 2;

                SetInfoText("2");
            }

            return;
        }


        if (gameClearCountdownStep == 2)
        {
            if (gameClearTimer >= 0.5f)
            {
                gameClearTimer = 0f;
                gameClearCountdownStep = 3;

                SetInfoText("1");
            }

            return;
        }


        if (gameClearCountdownStep == 3)
        {
            if (gameClearTimer >= 0.5f)
            {
                currStage++;

                InitGame();
            }
        }
    }


    // ============================================================
    // SET INFO TEXT
    // ============================================================

    private void SetInfoText(string value)
    {
        if (infoText == null)
            return;

        infoText.SetActive(true);

        TextMeshProUGUI text =
            infoText
                .GetComponent<TextMeshProUGUI>();

        if (text != null)
            text.text = value;
    }


    // ============================================================
    // GHOST POSITION
    // ============================================================

    public Vector3 GhostPosition(Vector3 vec)
    {
        if (ghostBlock == null)
            return vec;

        int x =
            Mathf.RoundToInt(vec.x);

        int y =
            Mathf.Max(
                Mathf.RoundToInt(vec.y),
                0
            );

        int z =
            Mathf.RoundToInt(vec.z);

        ghostBlock.transform.position =
            new Vector3(
                x,
                y,
                z
            );

        while (
            ValidMove(
                ghostBlock.transform
            )
        )
        {
            ghostBlock.transform.position +=
                Vector3.down;
        }

        return
            ghostBlock.transform.position +
            Vector3.up;
    }


    // ============================================================
    // VALID MOVE
    // ============================================================

    private bool ValidMove(Transform transform)
    {
        foreach (Transform children in transform)
        {
            int roundedY =
                Mathf.RoundToInt(
                    children.transform.position.y
                );

            int roundedX =
                Mathf.RoundToInt(
                    children.transform.position.x
                );

            if (
                roundedX < 0 ||
                roundedX >= Helper.WIDTH ||
                roundedY < 0 ||
                roundedY >= Helper.HEIGHT
            )
            {
                return false;
            }

            if (grid[roundedY, roundedX] != null)
            {
                return false;
            }
        }

        return true;
    }


    // ============================================================
    // RESTART
    // ============================================================

    public void Restart()
    {
        InitGame();
    }


    // ============================================================
    // GO BACK
    // ============================================================

    public void GoBack()
    {
        SceneManager.LoadScene(0);
    }


    // ============================================================
    // RL / TRAINING HELPERS
    // ============================================================
    public bool IsCellOccupied(int x, int y)
    {
        if (x < 0 || x >= Helper.WIDTH ||
            y < 0 || y >= Helper.HEIGHT)
        {
            return false;
        }

        return grid[y, x] != null;
    }
    public bool IsGameOver()
    {
        return gameOver;
    }


    public bool IsGameClear()
    {
        return gameClear;
    }


    public bool IsPaused()
    {
        return isPaused;
    }


    public bool IsPlayerTurn()
    {
        return
            simulationState ==
            SimulationState.PlayerTurn;
    }


    public int GetScore()
    {
        return score;
    }


    public int GetLinesDeleted()
    {
        return linesDeleted;
    }


    public int GetCurrentLevel()
    {
        return currentLevel;
    }


    public int GetCurrentStage()
    {
        return currStage;
    }


    public int GetGemCount()
    {
        return numGems;
    }


    public float GetPlayTime()
    {
        return playTime;
    }


    public float GetFallTime()
    {
        return fallTime;
    }


    public TetrisBlock GetCurrentBlock()
    {
        return currBlock;
    }


    public int GetNextBlock()
    {
        return nextBlock;
    }
}


// ================================================================
// GAME OVER EXCEPTION
// ================================================================

[Serializable]
public class GameOverException : Exception
{
    public GameOverException()
    {
    }


    public GameOverException(string message)
        : base(message)
    {
    }
}