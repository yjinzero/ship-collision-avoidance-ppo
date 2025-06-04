using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class CustomVelocityGridSensorComponent : GridSensorComponent
{
    // GetGridSensors() 호출 시 만들어지는 센서들
    private GridSensorBase[] _gridSensors;

    protected override GridSensorBase[] GetGridSensors()
    {
        // 생성된 센서를 필드에도 저장
        _gridSensors = new GridSensorBase[]
        {
            new CustomVelocityGridSensor(
                "VelocityGrid",
                5,
                CellScale,
                GridSize,
                gameObject,
                ColliderMask,
                DetectableTags,
                RotateWithAgent
            )
        };
        return _gridSensors;
    }

    public void ResetSensor()
    {
        // 센서가 리셋될 때 호출됨
        if (_gridSensors != null)
        {
            foreach (var sensor in _gridSensors)
            {
                sensor.Reset();
            }
        }
    }
}

