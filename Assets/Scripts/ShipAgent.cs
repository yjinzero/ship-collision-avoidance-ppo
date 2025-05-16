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
    // Start is called before the first frame update
    public Transform Goal;
    public int portNumber = 5050;
    private ShipState shipState;

    private NetworkStream stream;
    private TcpClient client;
    private float scaleFactor = 20f;
    private float posLimit = 1000f;
    private float maxRudderAngle = 35f * Mathf.Deg2Rad;
    private float speedStandard = 10.0f;

    public float cpaSafeThreshold = 90f; 

    public float cpaThreshold = 20f;

    private ScenarioManager scenarioManager;

    private TargetShip targetShip;
    private Transform targetShipTransform;


    void Start()
    {
        shipState = new ShipState();
        client = new TcpClient("localhost", portNumber);
        stream = client.GetStream();
        scenarioManager = FindObjectOfType<ScenarioManager>();
    }

    public override void OnEpisodeBegin()
    {
        // Reset the position of the agent and target at the beginning of each episode
        this.shipState.position = Vector3.zero;
        this.shipState.velocity = Vector3.zero; // 0 아니게 수정 필요
        this.shipState.yaw = 0.0f;
        this.shipState.yawRate = 0.0f;
        this.shipState.rudderAngle = 0.0f;
        this.shipState.propellerSpeed = 17.95f;

        this.transform.localPosition = Vector3.zero;
        this.transform.localRotation = Quaternion.Euler(0, 0, 0);

        // initialize calculator
        string message = $"{50.0}";
        byte[] data = Encoding.ASCII.GetBytes(message + "\n");
        stream.Write(data, 0, data.Length);

        // initialize goal position
        Goal.localPosition = new Vector3(9000f, 0.0f, 0.0f);

        // Scenario 적용
        Scenario scenario = scenarioManager.GetNextScenario();
        scenarioManager.SpawnScenario(scenario);
        targetShip = scenarioManager.GetSpawnedShips()[0].GetComponent<TargetShip>();
        targetShipTransform = scenarioManager.GetSpawnedShips()[0].transform;
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
        string message = $"{actions.ContinuousActions[0]}";
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
            EndEpisode();
            return;
        }

        // 2) 목표 도달 시 종료
        float cpa = CalculateCPA(new Vector3(y, 0, x), new Vector3(v, 0, u), targetShipTransform.localPosition, targetShip.GetVelocity());

        // CPA가 안전 임계값(1해리) 이상이면 "충돌 위험 해소"로 간주하고 종료
        if (cpa > cpaSafeThreshold)
        {
            SetReward(1.0f);  // 충돌 회피 성공 보상
            EndEpisode();
            return;
        }

        if (cpa < 10f)
        {
            SetReward(-1.0f);
            EndEpisode();
            return;
        }


        // 3) 충돌 위험 평가
        bool isHighRisk = IsHighCollisionRisk(cpa);

        

        if (isHighRisk)
        {
            // CPA가 커질수록 보상
            AddReward((cpaThreshold - cpa) * 0.1f);
        }
        else
        {
            // 목표 접근 보상
            float angleCurrent = AngleToGoal(this.shipState.velocity, Goal.localPosition - this.transform.localPosition);
            float angleNext = AngleToGoal(new Vector3(v, 0, u), Goal.localPosition - new Vector3(y, 0, x));

            float angleImprovement = angleCurrent - angleNext;
            AddReward(Mathf.Clamp(angleImprovement * 0.01f, -0.05f, 0.05f));

            // 드리프트 억제
            AddReward(-0.1f * Mathf.Abs(v));

            // COLREGs 준수 평가 및 보상
            float colregsReward = EvaluateCOLREGsCompliance(targetShip.GetCategory(), rudder);
            AddReward(colregsReward);
        }
        
        // 4. Unity 위치에 적용
        this.transform.localPosition = new Vector3(y, 0, x);
        this.transform.localRotation = Quaternion.Euler(0, heading * Mathf.Rad2Deg, 0);

        this.shipState.position = new Vector3(y, 0, x);
        this.shipState.velocity = new Vector3(v, 0, u);
        this.shipState.yaw = heading;
        this.shipState.yawRate = r;
        this.shipState.rudderAngle = rudder;



    }
    
    private bool IsHighCollisionRisk(float cpa)
    {
        return cpa < cpaThreshold;
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
}
