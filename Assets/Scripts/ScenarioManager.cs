using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class TargetShipConfig
{
    public Vector3 position;
    public Vector3 direction;  // normalized 이동 방향
    public float speed;
}


[System.Serializable]
public class Scenario
{
    public List<TargetShipConfig> targetShips;
}

public class ScenarioManager : MonoBehaviour
{
    public GameObject targetShipPrefab;

    public List<Scenario> scenarios; // Inspector에서 설정하거나 코드로 초기화
    private int currentScenarioIndex = 0;

    private List<GameObject> spawnedShips = new List<GameObject>();

    public Scenario GetNextScenario()
    {
        Scenario s = scenarios[currentScenarioIndex];
        currentScenarioIndex = (currentScenarioIndex + 1) % scenarios.Count;
        return s;
    }

    public void SpawnScenario(Scenario scenario)
    {
        ClearScenario();

        foreach (var shipConfig in scenario.targetShips)
        {
            GameObject ship = Instantiate(targetShipPrefab, shipConfig.position, Quaternion.LookRotation(shipConfig.direction));
            ship.GetComponent<TargetShip>().Initialize(shipConfig.direction, shipConfig.speed);
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
}
