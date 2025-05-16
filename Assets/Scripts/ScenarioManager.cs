using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System.Text;

[System.Serializable]
public class TargetShipConfig
{
    public float posRadius;
    public float posAngle;
    public float yaw;
    public float yawRate;
    public float speed;
    public int category;
}

[System.Serializable]
public class Scenario
{
    public List<TargetShipConfig> targetShips;
}

public class ScenarioManager : MonoBehaviour
{

    public GameObject targetShipPrefab;


    private List<GameObject> spawnedShips = new List<GameObject>();

    private float nauticalMile = 1852f; // 1 nautical mile in meters
    private float knot = 0.514444f; // 1 knot in m/s
    private float speedStandard = 12.0f;

    private enum ShipCategory
    {
        CROSSING_GIVE_WAY,
        CROSSING_STAND_ON,
        OVERTAKING,
        OVERTAKEN,
        HEAD_ON,
        SAFE_ENCOUNTER
    }

    List<int> riskyCategories = new List<int>
    {
        (int)ShipCategory.CROSSING_GIVE_WAY,
        (int)ShipCategory.CROSSING_STAND_ON,
        (int)ShipCategory.OVERTAKING,
        (int)ShipCategory.OVERTAKEN,
        (int)ShipCategory.HEAD_ON
    };

    public List<GameObject> GetSpawnedShips()
    {
        return spawnedShips;
    }


    public Scenario GetNextScenario()
    {
        int randomCategory = riskyCategories[Random.Range(0, riskyCategories.Count)];
        TargetShipConfig shipConfig = CreateConfigByCategory(randomCategory);

        Scenario scenario = new Scenario();
        scenario.targetShips = new List<TargetShipConfig>();
        scenario.targetShips.Add(shipConfig);

        return scenario;
    }

    public void SpawnScenario(Scenario scenario)
    {
        ClearScenario();

        foreach (var shipConfig in scenario.targetShips)
        {
            Vector3 shipPosition = new Vector3(shipConfig.posRadius * Mathf.Sin(shipConfig.posAngle * Mathf.Deg2Rad), 0, shipConfig.posRadius * Mathf.Cos(shipConfig.posAngle * Mathf.Deg2Rad));
            Quaternion shipRotation = Quaternion.Euler(0, shipConfig.yaw, 0);
            GameObject ship = Instantiate(targetShipPrefab, shipPosition, shipRotation);
            ship.GetComponent<TargetShip>().Initialize(shipConfig.speed, shipConfig.yawRate, shipConfig.category);
            spawnedShips.Add(ship);
        }
    }

    public void ClearScenario()
    {
        foreach (var obj in spawnedShips)
        {
            Destroy(obj);
        }
        spawnedShips.Clear();
    }

    TargetShipConfig CreateConfigByCategory(int category)
    {
        TargetShipConfig config = new TargetShipConfig();
        config.posRadius = Random.Range(0.8f, 1.0f) * 50f;

        switch ((ShipCategory)category)
        {
            case ShipCategory.CROSSING_GIVE_WAY:
                config.posAngle = Random.Range(-6f, 112.5f);
                if (config.posAngle < 6f) { config.yaw = Random.Range(-174f, -67.5f); }
                else if (config.posAngle < 90f) { config.yaw = Random.Range(-180f, 0f); }
                else { config.yaw = Random.Range(-67.5f, 0f); }
                break;
            case ShipCategory.CROSSING_STAND_ON:
                config.posAngle = Random.Range(-112.5f, 6f);
                if (config.posAngle > -6f) { config.yaw = Random.Range(67.5f, 174f); }
                else if (config.posAngle > -90f) { config.yaw = Random.Range(0f, 180f); }
                else { config.yaw = Random.Range(0f, 67.5f); }
                break;
            case ShipCategory.OVERTAKING:
                config.posAngle = Random.Range(-6f, 6f);
                config.yaw = Random.Range(-67.5f, 67.5f);
                break;
            case ShipCategory.OVERTAKEN:
                config.posAngle = Random.Range(112.5f, 247.5f);
                config.yaw = Random.Range(-67.5f, 67.5f);
                break;
            case ShipCategory.HEAD_ON:
                config.posAngle = Random.Range(-6f, 6f);
                config.yaw = Random.Range(-6f, 6f);
                break;
        }

        config.category = category;
        config.yawRate = yawRateByCatergory(category);
        if (category == (int)ShipCategory.OVERTAKEN)
        {
            config.speed = speedStandard * 1.5f * knot / 20.0f;
        }
        else if (category == (int)ShipCategory.OVERTAKING)
        {
            config.speed = speedStandard * 0.5f * knot / 20.0f;
        }
        else
        {
            config.speed = speedStandard * knot / 20.0f;
        }
        

        return config;
    }
    
    private float yawRateByCatergory(int category)
    {
        switch (category)
        {
            case (int)ShipCategory.CROSSING_STAND_ON:
                return Random.Range(0.0f, 0.5f);
            case (int)ShipCategory.OVERTAKEN:
                return Random.Range(0.0f, 1.0f);
            case (int)ShipCategory.HEAD_ON:
                return Random.Range(0.0f, 1.0f);
            case (int)ShipCategory.SAFE_ENCOUNTER:
                return Random.Range(-10.0f, 10.0f);
            default:
                return 0f;
        }
    }
}
