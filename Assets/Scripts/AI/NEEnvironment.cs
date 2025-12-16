// SerialID: [77a855b2-f53d-4b80-9c94-c40562952b74]
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System;
using System.Linq;

// プロット用
using System.IO;
using UnityEngine.SceneManagement;


#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

// プロット用
[Serializable]
public class GenerationLog
{
    public int generation;        // 0,1,2,... (内部世代番号)
    public float bestRecord;      // 全期間でのベスト
    public float genBestRecord;   // この世代のベスト
    public float avgReward;       // この世代の平均
}



public class NEEnvironment : Environment
{
    // ランダム用
    [SerializeField] private ObstacleRandomizer obstacleRandomizer;
    [SerializeField] private int obstacleSeedBase = 12345;
    [SerializeField] private bool randomizeObstaclesEachGeneration = true;



    [Header("Settings"), SerializeField] private int totalPopulation = 100;
    private int TotalPopulation { get { return totalPopulation; } }

    [SerializeField] private int tournamentSelection = 85;
    private int TournamentSelection { get { return tournamentSelection; } }

    [SerializeField] private int eliteSelection = 4;
    private int EliteSelection { get { return eliteSelection; } }

    [SerializeField] public bool[] selectedInputs = new bool[46];
    [SerializeField] public List<double> sensorAngleConfig = new List<double>();

    private int InputSize { get; set; }

    private List<int> SelectedInputsList { get; set; }

    [SerializeField] private int hiddenSize = 8;
    private int HiddenSize { get { return hiddenSize; } }

    [SerializeField] private int hiddenLayers = 1;
    private int HiddenLayers { get { return hiddenLayers; } }

    [SerializeField] private int outputSize = 4;
    private int OutputSize { get { return outputSize; } }

    [SerializeField] private int nAgents = 4;
    private int NAgents { get { return nAgents; } }

    [Header("Agent Prefab"), SerializeField] private GameObject gObject = null;
    private GameObject GObject => gObject;

    [SerializeField] private bool isChallenge4 = false;
    private bool IsChallenge4 { get { return isChallenge4; } }

    [Header("UI References"), SerializeField] private Text populationText = null;
    private Text PopulationText { get { return populationText; } }

    private float GenBestRecord { get; set; }

    private float SumReward { get; set; }
    private float AvgReward { get; set; }

    private List<NNBrain> Brains { get; set; } = new List<NNBrain>();
    private List<GameObject> GObjects { get; } = new List<GameObject>();
    private List<Agent> Agents { get; } = new List<Agent>();
    private int Generation { get; set; }

    private float BestRecord { get; set; }

    private List<AgentPair> AgentsSet { get; } = new List<AgentPair>();
    private Queue<NNBrain> CurrentBrains { get; set; }

    private List<Obstacle> Obstacles { get; } = new List<Obstacle>();

    // プロット用
    private List<GenerationLog> generationLogs = new List<GenerationLog>();


    void Start() {
        // Calculate and set input size.
        int sensorCount = 0;
        foreach (bool value in selectedInputs)
        {
            if (value) sensorCount++;
        }
        InputSize = sensorCount;

        // Calculate and set sensors list.
        List<int> selectedInputsList = new List<int>();
        for (int i = 0; i < selectedInputs.Length; i++)
        {
            if (selectedInputs[i]) selectedInputsList.Add(i);
        }
        SelectedInputsList = selectedInputsList;

        // Initialize brain.
        for(int i = 0; i < TotalPopulation; i++) {
            Brains.Add(new NNBrain(InputSize, HiddenSize, HiddenLayers, OutputSize));
        }

        for(int i = 0; i < NAgents; i++) {
            var obj = Instantiate(GObject);
            obj.SetActive(true);
            GObjects.Add(obj);
            Agents.Add(obj.GetComponent<Agent>());
        }
        
        foreach(Agent agent in Agents)
        {
            agent.SetAgentConfig(sensorAngleConfig);
        }

        BestRecord = -9999;

        // ランダム用
        if (randomizeObstaclesEachGeneration && obstacleRandomizer != null)
        {
            obstacleRandomizer.RandomizeObstacles(obstacleSeedBase + Generation);
        }

        SetStartAgents();
        if (IsChallenge4) {
            Obstacles.AddRange(FindObjectsOfType<Obstacle>());
        }
    }

    void SetStartAgents() {
        CurrentBrains = new Queue<NNBrain>(Brains);
        AgentsSet.Clear();
        var size = Math.Min(NAgents, TotalPopulation);
        for(var i = 0; i < size; i++) {
            AgentsSet.Add(new AgentPair {
                agent = Agents[i],
                brain = CurrentBrains.Dequeue()
            });
        }
    }

    void FixedUpdate() {
        foreach(var pair in AgentsSet.Where(p => !p.agent.IsDone)) {
            AgentUpdate(pair.agent, pair.brain);
        }

        AgentsSet.RemoveAll(p => {
            if(p.agent.IsDone) {
                p.agent.Stop();
                p.agent.gameObject.SetActive(false);
                float r = p.agent.Reward;
                BestRecord = Mathf.Max(r, BestRecord);
                GenBestRecord = Mathf.Max(r, GenBestRecord);
                p.brain.Reward = r;
                SumReward += r;
            }
            return p.agent.IsDone;
        });

        if(CurrentBrains.Count == 0 && AgentsSet.Count == 0) {
            SetNextGeneration();
        }
        else {
            SetNextAgents();
        }
    }

    private void AgentUpdate(Agent a, NNBrain b) {
        double[] action;
        if (a.IsBackingUp) {
            action = a.UpdateBackupTimerAndGetAction(Time.fixedDeltaTime);
        } else {
            var observation = a.GetAllObservations();
            var rearranged = RearrangeObservation(observation, SelectedInputsList);
            action = b.GetAction(rearranged);
        }
        a.AgentAction(action, a.IsBackingUp);
    }

    private void SetNextAgents() {
        int size = Math.Min(NAgents - AgentsSet.Count, CurrentBrains.Count);
        for(var i = 0; i < size; i++) {
            var nextAgent = Agents.First(a => a.IsDone);
            var nextBrain = CurrentBrains.Dequeue();
            nextAgent.Reset();
            AgentsSet.Add(new AgentPair {
                agent = nextAgent,
                brain = nextBrain
            });
        }
        UpdateText();
    }

    // プロット用
    private void SetNextGeneration() {
        // 従来通り
        AvgReward = SumReward / TotalPopulation;


        // プロット用、ここでログを追加
        generationLogs.Add(new GenerationLog
        {
            generation    = Generation,     // 世代番号
            bestRecord    = BestRecord,     // これまでの最大
            genBestRecord = GenBestRecord,  // この世代内の最大
            avgReward     = AvgReward       // この世代の平均
        });

        // 毎世代ファイルに保存
        SaveGenerationLogsToCsv();


        // 従来通り
        GenPopulation();
        SumReward = 0;
        GenBestRecord = -9999;


        // ランダム用
        if (randomizeObstaclesEachGeneration && obstacleRandomizer != null)
        {
            obstacleRandomizer.RandomizeObstacles(obstacleSeedBase + Generation);
        }    


        Agents.ForEach(a => a.Reset());
        SetStartAgents();
        UpdateText();
    }

    private static int CompareBrains(Brain a, Brain b) {
        if(a.Reward > b.Reward) return -1;
        if(b.Reward > a.Reward) return 1;
        return 0;
    }

    private void GenPopulation() {
        var children = new List<NNBrain>();
        var bestBrains = Brains.ToList();

        // Elite selection
        bestBrains.Sort(CompareBrains);
        if(EliteSelection > 0) {
            children.AddRange(bestBrains.Take(EliteSelection));
        }

#if UNITY_EDITOR
        var path = string.Format("Assets/LearningData/NE/{0}.json", EditorSceneManager.GetActiveScene().name);
        bestBrains[0].Save(path);
#endif

        while(children.Count < TotalPopulation) {
            var tournamentMembers = Brains.AsEnumerable().OrderBy(x => Guid.NewGuid()).Take(tournamentSelection).ToList();
            tournamentMembers.Sort(CompareBrains);
            children.Add(tournamentMembers[0].Mutate(Generation));
            children.Add(tournamentMembers[1].Mutate(Generation));
        }
        Brains = children;
        Generation++;
    }

    protected List<double> RearrangeObservation(List<double> observation, List<int> indexesToUse)
    {
        if(observation == null || indexesToUse == null) return null;

        List<double> rearranged = new List<double>();
        foreach(int index in indexesToUse)
        {
            if(index >= observation.Count)
            {
                rearranged.Add(0);
                continue;
            }
            rearranged.Add(observation[index]);
        }

        return rearranged;
    }

    private void UpdateText() {
        PopulationText.text = "Population: " + (TotalPopulation - CurrentBrains.Count) + "/" + TotalPopulation
            + "\nGeneration: " + (Generation + 1)
            + "\nBest Record: " + BestRecord
            + "\nBest this gen: " + GenBestRecord
            + "\nAverage: " + AvgReward;
    }

    private struct AgentPair
    {
        public NNBrain brain;
        public Agent agent;
    }

    private void SaveGenerationLogsToCsv()
    {
        // Assets/LearningData/NE/SceneName_log.csv に保存する
        string sceneName = SceneManager.GetActiveScene().name;
        string dir = Path.Combine(Application.dataPath, "LearningData/NE");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string filePath = Path.Combine(dir, sceneName + "_log.csv");

        // CSV を組み立てる
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        // ヘッダ
        sb.AppendLine("generation,bestRecord,genBestRecord,avgReward");

        // データ行
        foreach (var log in generationLogs)
        {
            sb.AppendLine(string.Format(
                "{0},{1},{2},{3}",
                log.generation,
                log.bestRecord,
                log.genBestRecord,
                log.avgReward
            ));
        }

        File.WriteAllText(filePath, sb.ToString());
    }

}
