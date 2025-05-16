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
    private float posLimit = 500f;
    private float maxRudderAngle = 35f*Mathf.Deg2Rad;
    private float speedStandard = 10.0f;

    private ScenarioManager scenarioManager;    

    
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
        this.shipState.velocity = Vector3.zero;
        this.shipState.yaw = 0.0f;
        this.shipState.yawRate = 0.0f;
        this.shipState.rudderAngle = 0.0f;
        this.shipState.propellerSpeed = 17.95f;

        this.transform.localPosition = Vector3.zero;
        this.transform.localRotation = Quaternion.Euler(0, 0, 0);

        //Debug.Log("Episode Begin: " + this.transform.localPosition.ToString());

        string message = $"{50.0}";
        byte[] data = Encoding.ASCII.GetBytes(message + "\n");
        stream.Write(data, 0, data.Length);

        // TODO: Randomize the target position
        Goal.localPosition = new Vector3((Random.value-0.5f) * 100, 0.0f, Random.value * 50 + 100);

        // Scenario 적용
        Scenario scenario = scenarioManager.GetNextScenario(); // 순차적 or 랜덤하게
        scenarioManager.SpawnScenario(scenario);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 goalPosition = Quaternion.Inverse(this.transform.localRotation) * (Goal.localPosition - this.shipState.position);

        sensor.AddObservation(goalPosition.z / posLimit);
        sensor.AddObservation(goalPosition.x / posLimit);
        // sensor.AddObservation(Goal.localPosition.z / posLimit);
        // sensor.AddObservation(Goal.localPosition.x / posLimit);
        // sensor.AddObservation(this.transform.localPosition.z / posLimit);
        // sensor.AddObservation(this.transform.localPosition.x / posLimit);

        
        sensor.AddObservation(this.shipState.velocity.z / speedStandard);
        sensor.AddObservation(this.shipState.velocity.x / speedStandard);

        Vector3 worldVelocity = this.transform.localRotation*this.shipState.velocity;
        float angleDiff = Vector3.Angle(worldVelocity, this.shipState.position - Goal.localPosition);

        sensor.AddObservation(angleDiff / 180f);
        //sensor.AddObservation(this.shipState.yaw / Mathf.PI);
        sensor.AddObservation(this.shipState.yawRate / Mathf.PI);
        sensor.AddObservation(this.shipState.rudderAngle / maxRudderAngle);
    }


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

        //4. 보상 계산
        //distance 계산
        float distanceToTargetCurrent = Vector3.Distance(this.transform.localPosition, Goal.localPosition);
        float distanceToTargetNext = Vector3.Distance(new Vector3(y, 0, x), Goal.localPosition);

        if(x > posLimit || x < -posLimit || y > posLimit || y < -posLimit)
        {
            SetReward(-1.0f);
            EndEpisode();
        }

        if (distanceToTargetNext < 5f)
        {
            SetReward(1.0f);
            EndEpisode();
        }else if(distanceToTargetNext > 20f){
            Vector3 worldVelocityCurrent = this.transform.localRotation*this.shipState.velocity;
            Vector3 worldVelocityNext = Quaternion.Euler(0, heading * Mathf.Rad2Deg, 0)*(new Vector3(v, 0, u));
            float angleDiffCurrent = Vector3.Angle(worldVelocityCurrent, this.shipState.position - Goal.localPosition);
            float angleDiffNext = Vector3.Angle(worldVelocityNext, new Vector3(y, 0, x) - Goal.localPosition);
            float angleDiff = angleDiffCurrent - angleDiffNext;
            SetReward(angleDiff/180*0.05f);    
        }else{
            SetReward(0.0f);
        }
        
        
        float distanceDiff = distanceToTargetCurrent - distanceToTargetNext;
        AddReward(distanceDiff*0.01f);

        AddReward(-0.1f*Mathf.Abs(v));
        
        // 3. Unity 위치에 적용
        this.transform.localPosition = new Vector3(y, 0, x);
        this.transform.localRotation = Quaternion.Euler(0, heading * Mathf.Rad2Deg, 0);

        this.shipState.position = new Vector3(y, 0, x);
        this.shipState.velocity = new Vector3(v, 0, u);
        this.shipState.yaw = heading;
        this.shipState.yawRate = r;
        this.shipState.rudderAngle = rudder;

    }
}
