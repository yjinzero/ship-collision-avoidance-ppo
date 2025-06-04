using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Net.Sockets;
using System.Text;


public struct ShipState
{
    public Vector3 position;
    public Vector3 velocity;
    public float yaw;
    public float yawRate;
    public float rudderAngle;
    public float propellerSpeed;
}


public class ShipAgent : Agent
{
    public int agentId = 0;
    public Transform Goal;
    private int portNumber = 5050;
    public ShipState shipState;

    private NetworkStream stream;
    private TcpClient client;
    private float scaleFactor = 20f;
    private float posLimit = 3000f;
    private float maxRudderAngle = 35f * Mathf.Deg2Rad;
    private float speedStandard = 10.0f;

    private bool avoidanceMode = false;
    private bool lastAvoidanceMode = false;
    int overtakingMode = 0; //0: 일반 1:overtaking -1:overtaken

    private ScenarioManager scenarioManager;

    private float nauticalMile = 1852f; // 1 nautical mile in meters

    private List<int> agentCategories;

    private int stepCount = 0;
    private bool avoidanceFinished = false;

    private float initialDistance = 0f;

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
        shipState = new ShipState();
        client = new TcpClient("localhost", portNumber + agentId);
        stream = client.GetStream();
        scenarioManager = FindObjectOfType<ScenarioManager>();
    }

    public override void OnEpisodeBegin()
    {

        var agentConfig = scenarioManager.GetAgentConfig(agentId);
        if (agentConfig != null)
        {
            ResetAgent(agentConfig);
        }
        else return;


        var agentStates = scenarioManager.GetAgentStates(this);
        agentCategories = new List<int>();

        for (int i = 0; i < agentStates.Count; i++)
        {
            var (position, velocity, yaw) = agentStates[i];
            int category = classifyCategory(position, yaw);
            agentCategories.Add(category);
        }

        // TCP 초기화 메세지 전송
        string message = $"{agentConfig.x * nauticalMile},{agentConfig.y * nauticalMile},{agentConfig.yaw * Mathf.Deg2Rad}";
        byte[] data = Encoding.ASCII.GetBytes(message + "\n");
        stream.Write(data, 0, data.Length);
        
        Debug.Log($"Agent {agentId} initialized at position ({agentConfig.x}, {agentConfig.y}) with yaw {agentConfig.yaw} degrees.");
    }

    public void ResetAgent(AgentConfig agentConfig)
    {

        // Set the initial position and heading of the agent based on the scenario configuration
        this.transform.localPosition = new Vector3(agentConfig.y, 0, agentConfig.x) * nauticalMile / scaleFactor;
        this.transform.localRotation = Quaternion.Euler(0, agentConfig.yaw, 0);
        // Reset the position of the agent and target at the beginning of each episode
        this.shipState.position = new Vector3(agentConfig.y, 0, agentConfig.x);
        this.shipState.velocity = Vector3.zero;
        this.shipState.yaw = agentConfig.yaw * Mathf.Deg2Rad; // Convert to radians
        this.shipState.yawRate = 0.0f;
        this.shipState.rudderAngle = 0.0f;
        this.shipState.propellerSpeed = 17.95f;


        // goal 설정
        this.Goal.localPosition = this.transform.localPosition + this.transform.forward * 20f * nauticalMile / scaleFactor;
        initialDistance = (this.Goal.localPosition - this.transform.localPosition).magnitude / (20f * nauticalMile / scaleFactor);
    }

    // collect observations
    public override void CollectObservations(VectorSensor sensor)
    {

        // Goal의 상대 위치 (Local Frame)
        Vector3 relativePosition = Quaternion.Inverse(this.transform.localRotation) *
                                    (Goal.localPosition - this.transform.localPosition);

        // 1. Goal의 방위각 (선박 기준)
        float bearingToGoal = Mathf.Atan2(relativePosition.x, relativePosition.z) / Mathf.PI;
        sensor.AddObservation(bearingToGoal);

        // 2. 선박 속도의 크기
        float speedMagnitude = this.shipState.velocity.magnitude / speedStandard;
        sensor.AddObservation(speedMagnitude);

        // 3. 선박 헤딩 (속도 방향)
        float speedHeading = Mathf.Atan2(this.shipState.velocity.x, this.shipState.velocity.z) / Mathf.PI;
        sensor.AddObservation(speedHeading);

        // 4. 선회율
        sensor.AddObservation(this.shipState.yawRate / Mathf.PI);

        // 5. 러더각
        sensor.AddObservation(this.shipState.rudderAngle / maxRudderAngle);
    }


    // get action -> do action -> calculate reward
    public override void OnActionReceived(ActionBuffers actions)
    {
        // 1. 행동 값을 문자열로 전송 (예: rudder, engine force)
        string message = $"{actions.ContinuousActions[0]}&{overtakingMode}";
        //Debug.Log("Send: " + message);
        byte[] data = Encoding.ASCII.GetBytes(message + "\n");
        stream.Write(data, 0, data.Length);

        // 2. 결과값 수신 (예: x, y, heading)
        byte[] responseData = new byte[1024];
        int bytes = stream.Read(responseData, 0, responseData.Length);
        string response = Encoding.ASCII.GetString(responseData, 0, bytes);

        string[] parts = response.Split(',');
        float u = float.Parse(parts[0]) / scaleFactor;
        float v = float.Parse(parts[1]) / scaleFactor;
        float r = float.Parse(parts[2]);
        float x = float.Parse(parts[3]) / scaleFactor;
        float y = float.Parse(parts[4]) / scaleFactor;
        float heading = float.Parse(parts[5]);
        float rudder = float.Parse(parts[6]);


        //3. 보상 계산
        // 1) 영역 초과 시 종료
        if (x > posLimit || x < -posLimit || y > posLimit || y < -posLimit)
        {
            SetReward(-1.0f);
            scenarioManager.EndScenarioForAgent(this);
            return;
        }

        var agentStates = scenarioManager.GetAgentStates(this);

        float minCPA = float.MaxValue;
        float minDistance = float.MaxValue;
        int closestCPACategory = -1;
        int closestCategory = -1;
        avoidanceMode = false;
        overtakingMode = 0;

        for (int i = 0; i < agentStates.Count; i++)
        {
            var (position, velocity, yaw) = agentStates[i];
            if (agentCategories[i] == (int)ShipCategory.SAFE_ENCOUNTER)
            {
                agentCategories[i] = classifyCategory(position, yaw);
            }
            else
            {
                if ((this.transform.localPosition - position).magnitude > 6f * nauticalMile / scaleFactor)
                {
                    agentCategories[i] = (int)ShipCategory.SAFE_ENCOUNTER; // 너무 멀리 있는 경우
                }
            }

            if (agentCategories[i] == (int)ShipCategory.SAFE_ENCOUNTER) continue;
            else
            {
                avoidanceMode = true;
                if (agentCategories[i] == (int)ShipCategory.OVERTAKING && overtakingMode != -1) overtakingMode = 1;
                else if (agentCategories[i] == (int)ShipCategory.OVERTAKEN) overtakingMode = -1;
            }

            float cpa = CalculateCPA(this.transform.localPosition, this.shipState.velocity, position, velocity);

            if (cpa < minCPA)
            {
                minCPA = cpa;
                closestCPACategory = agentCategories[i];
            }

            float distance = (this.transform.localPosition - position).magnitude;
            if (distance < minDistance)
            {
                minDistance = distance;
                closestCategory = agentCategories[i];

            }
        }

        if (lastAvoidanceMode && !avoidanceMode)
        {
            //avoidanceFinished = true;
            stepCount = 0;
        }


        if (avoidanceMode)
        {
            float colregsReward = 0f;
            // CPA 조건 처리
            if (minDistance < 0.5f * nauticalMile / scaleFactor)
            {
                SetReward(-1.0f);
                scenarioManager.EndScenarioForAllAgents();
                return;
            }
            else if (minDistance < 1.5f * nauticalMile / scaleFactor)
            {
                AddReward((minDistance - 1.5f * nauticalMile / scaleFactor) / (1.5f * nauticalMile / scaleFactor) * 0.4f);
                colregsReward = EvaluateCOLREGsCompliance(closestCategory, rudder);

            }
            else if (minCPA < 1f * nauticalMile / scaleFactor)
            {
                AddReward((minCPA - 1f * nauticalMile / scaleFactor) / (nauticalMile / scaleFactor) * 0.4f);
                colregsReward = EvaluateCOLREGsCompliance(closestCPACategory, rudder);
            }
            else if (minDistance < 3f * nauticalMile / scaleFactor)
            {
                AddReward((minDistance - 3f * nauticalMile / scaleFactor) / (3f * nauticalMile / scaleFactor) * 0.2f);
                colregsReward = EvaluateCOLREGsCompliance(closestCategory, rudder);
            }
            else
            {
                AddReward((minDistance - 5f * nauticalMile / scaleFactor) / (1.5f * nauticalMile / scaleFactor) * 0.15f);
                colregsReward = EvaluateCOLREGsCompliance(closestCategory, rudder);
                Vector3 directionToGoal = Goal.localPosition - this.transform.localPosition;
                float angleToGoal = AngleToGoal(this.shipState.velocity, directionToGoal);
                AddReward(-angleToGoal / (180f * 10f));
            }
            AddReward(colregsReward);
        }
        else
        {
            // 일반적인 보상 계산
            Vector3 directionToGoal = Goal.localPosition - this.transform.localPosition;
            float angleToGoal = AngleToGoal(this.shipState.velocity, directionToGoal);
            float distanceToGoal = directionToGoal.magnitude / (20f * nauticalMile / scaleFactor);

            // if (!avoidanceFinished)
            // {
            //     AddReward(Mathf.Cos(angleToGoal) / 3f);
            //     // 목표에 가까워질수록 보상
            //     AddReward((initialDistance - distanceToGoal) / initialDistance);

            //     if (distanceToGoal < 0.3f)
            //     {
            //         SetReward(1.0f); // 목표에 도달하면 보상
            //         scenarioManager.EndScenarioForAgent(this);
            //         return;
            //     }
            //     AddReward(-0.1f * Mathf.Abs(v));

            // }
            // else
            // {
            stepCount++;
            if (stepCount > 1000)
            {
                SetReward(-1.0f); // 너무 오래 걸리면 패널티
                scenarioManager.EndScenarioForAgent(this);
                return;
            }
            AddReward(Mathf.Cos(angleToGoal) / 3f);
            // 목표에 가까워질수록 보상
            AddReward((initialDistance - distanceToGoal) / initialDistance);
            if (distanceToGoal > 2 * initialDistance)
            {
                SetReward(-1.0f); // 너무 멀어지면 패널티
                scenarioManager.EndScenarioForAgent(this);
                return;
            }
            if (distanceToGoal < 0.3f)
            {
                SetReward(1.0f); // 목표에 도달하면 보상
                scenarioManager.EndScenarioForAgent(this);
                return;
            }
            AddReward(-0.2f * Mathf.Abs(v));
            



        }
        // 4. Unity 위치에 적용
        this.transform.localPosition = new Vector3(y, 0, x);
        this.transform.localRotation = Quaternion.Euler(0, heading * Mathf.Rad2Deg, 0);

        this.shipState.position = new Vector3(y, 0, x);
        this.shipState.velocity = new Vector3(v, 0, u);
        this.shipState.yaw = heading;
        this.shipState.yawRate = r;
        this.shipState.rudderAngle = rudder;
        
        lastAvoidanceMode = avoidanceMode;
    }

    public Vector3 GetVelocity()
    {
        return this.shipState.velocity;
    }

    private float AngleToGoal(Vector3 velocity, Vector3 directionToGoal)
    {
        if (velocity.magnitude < 1e-5f)
            return 180f; // 속도가 거의 없으면 최악의 방향 간주

        return Vector3.Angle(velocity, directionToGoal);
    }

    private float CalculateCPA(Vector3 posA, Vector3 velA, Vector3 posB, Vector3 velB)
    {
        Vector3 r = posB - posA;
        Vector3 v = velB - velA;
        float vMag = v.magnitude;

        if (vMag < 1e-5f)
        {
            return r.magnitude;  // 상대 속도가 거의 없으면 현재 거리 반환
        }

        float cpaDistance = Mathf.Abs(Vector3.Cross(r, v).magnitude / vMag);
        return cpaDistance;
    }

    private float EvaluateCOLREGsCompliance(int category, float rudderAngle)
    {
        switch (category)
        {
            case 0: // CROSSING_GIVE_WAY
                if (rudderAngle > 0)
                    return 0.1f * rudderAngle;
                else
                    return -0.1f * rudderAngle; // 위반
            case 1: // CROSSING_STAND_ON
                if (Mathf.Abs(rudderAngle) > Mathf.Deg2Rad * 1)
                    return -0.1f * rudderAngle; // 위반
                else
                    return 0.1f;
            case 2: // OVERTAKING
                if (rudderAngle > 0)
                    return 0.1f * rudderAngle;
                else
                    return -0.1f * rudderAngle; // 위반
            case 3: // OVERTAKEN
                if (Mathf.Abs(rudderAngle) > Mathf.Deg2Rad * 1)
                    return -0.1f * rudderAngle; // 위반
                else
                    return 0.1f;
            case 4: // HEAD_ON
                if (rudderAngle > 0)
                    return 0.1f * rudderAngle;
                else
                    return -0.1f * rudderAngle; // 위반
            default:
                return 0f; // 기본 보상
        }
    }


    private int classifyCategory(Vector3 position, float yaw)
    {
        Vector3 relativePosition = position - this.transform.localPosition;

        if (Mathf.Abs(Vector3.Dot(this.transform.forward, relativePosition)) > 5f*nauticalMile/scaleFactor || Mathf.Abs(Vector3.Dot(this.transform.right,relativePosition)) > 5f*nauticalMile/scaleFactor)
        {
            return (int)ShipCategory.SAFE_ENCOUNTER; // 너무 멀리 있는 경우
        }

        float relativeBearing = Mathf.Atan2(relativePosition.x, relativePosition.z);
        float relativeHeading = yaw - this.shipState.yaw;
        if(relativeHeading < -Mathf.PI) relativeHeading += 2 * Mathf.PI;
        if (relativeHeading > Mathf.PI) relativeHeading -= 2 * Mathf.PI;

        if (Mathf.Abs(relativeBearing) >= Mathf.PI * 5 / 8)
        {
            if (Mathf.Abs(relativeHeading) < Mathf.PI * 3 / 8) return (int)ShipCategory.OVERTAKEN;
        }
        else if (relativeBearing >= Mathf.PI / 2)
        {
            if (relativeHeading >= -Mathf.PI * 3 / 8 && relativeHeading <= 0)
            {
                return (int)ShipCategory.CROSSING_GIVE_WAY;
            }
        }
        else if (relativeBearing > Mathf.PI / 30)
        {
            if (relativeHeading <= 0)
            {
                return (int)ShipCategory.CROSSING_GIVE_WAY;
            }
        }
        else if (relativeBearing < -Mathf.PI / 2)
        {
            if (relativeHeading >= 0 && relativeHeading <= Mathf.PI * 3 / 8)
            {
                return (int)ShipCategory.CROSSING_STAND_ON;
            }
        }
        else if (relativeBearing < -Mathf.PI / 30)
        {
            if (relativeHeading >= 0)
            {
                return (int)ShipCategory.CROSSING_STAND_ON;
            }
        }
        else
        {
            if (Mathf.Abs(relativeHeading) >= Mathf.PI * 29 / 30)
            {
                return (int)ShipCategory.HEAD_ON;
            }
            else if (relativeHeading > Mathf.PI * 3 / 8)
            {
                return (int)ShipCategory.CROSSING_STAND_ON;
            }
            else if (relativeHeading < -Mathf.PI * 3 / 8)
            {
                return (int)ShipCategory.CROSSING_GIVE_WAY;
            }
            else
            {
                return (int)ShipCategory.OVERTAKING;
            }
        } 

        return (int)ShipCategory.SAFE_ENCOUNTER; 
    }

    
}
