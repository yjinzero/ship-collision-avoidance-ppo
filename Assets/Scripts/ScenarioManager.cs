using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;


[System.Serializable]
public class AgentConfig
{
    public float x;
    public float y;
    public float yaw;
} 

[System.Serializable]
public class Scenario
{
    public List<AgentConfig> agents = new List<AgentConfig>();
}

public class ScenarioManager : MonoBehaviour
{
    public List<ShipAgent> allAgents;  // 미리 연결
    private List<bool> endedEpisodes;  // 에이전트별 에피소드 종료 여부
    private List<bool> startedEpisodes;  // 에이전트별 에피소드 시작 여부
    public List<Scenario> predefinedScenarios;  // 시나리오 직접 작성
    private Scenario currentScenario;
    //private SimpleMultiAgentGroup agentGroup;
    bool newScenario = false;

    private enum ShipCategory
    {
        CROSSING_GIVE_WAY,
        CROSSING_STAND_ON,
        OVERTAKING,
        OVERTAKEN,
        HEAD_ON,
        SAFE_ENCOUNTER
    }

    void Start()
    {
        if (predefinedScenarios.Count == 0)
        {
            Debug.LogError("No predefined scenarios available!");
            return;
        }

        SetNewScenario();  // 초기 시나리오 설정
        SetActiveAgent();  // 활성 에이전트 설정
    }

    public void SetNewScenario()
    {
        // 새로운 시나리오 선택
        int randomIndex = Random.Range(0, predefinedScenarios.Count);
        currentScenario = predefinedScenarios[randomIndex];
        endedEpisodes = new List<bool>(new bool[currentScenario.agents.Count]);
        startedEpisodes = new List<bool>(new bool[currentScenario.agents.Count]);
        Debug.Log($"New scenario set: {randomIndex} with {currentScenario.agents.Count} agents.");

    }

    public void SetActiveAgent()
    {
        for (int i = 0; i < allAgents.Count; i++)
        {
            if (i < currentScenario.agents.Count)
            {
                allAgents[i].gameObject.SetActive(true);
            }
            else
            {
                allAgents[i].gameObject.SetActive(false);
            }
        }
    }


    public Scenario GetCurrentScenario()
    {
        return currentScenario;
    }

    public AgentConfig GetAgentConfig(int agentId)
    {
        if (currentScenario == null || agentId >= currentScenario.agents.Count)
            return null;
        if (startedEpisodes[agentId] == true) return null;
        else startedEpisodes[agentId] = true;
        return currentScenario.agents[agentId];
    }
    
    public void EndScenarioForAgent(ShipAgent agent)
    {
        endedEpisodes[agent.agentId] = true;
        agent.gameObject.SetActive(false);

        if (endedEpisodes.TrueForAll(x => x))
        {
            for(int i = 0; i < endedEpisodes.Count; i++)
            {
                allAgents[i].gameObject.SetActive(true);
            }
            EndScenarioForAllAgents();
        }
        
    }

    public void EndScenarioForAllAgents()
    {
        SetNewScenario();

        for (int i = 0; i < allAgents.Count; i++)
        {
            ShipAgent agent = allAgents[i];
            if (agent.gameObject.activeSelf)
            {
                Debug.Log($"Ending episode for agent {agent.agentId}, steps: {agent.StepCount}");
                agent.EndEpisode();
            }
            if (i >= currentScenario.agents.Count) agent.gameObject.SetActive(false);
            else agent.gameObject.SetActive(true);

        }
    }
  
    public List<(Vector3 position, Vector3 velocity, float yaw)> GetAgentStates(ShipAgent requestingAgent)
    {
        List<(Vector3, Vector3, float)> agentStates = new List<(Vector3, Vector3, float)>();
        for(int i = 0; i < currentScenario.agents.Count; i++)
        {
            
            ShipAgent agent = allAgents[i];
            if (agent != requestingAgent)
            {
                agentStates.Add((agent.transform.localPosition, agent.GetVelocity(), agent.shipState.yaw));
            }
        }
        return agentStates;
    }
    

}
