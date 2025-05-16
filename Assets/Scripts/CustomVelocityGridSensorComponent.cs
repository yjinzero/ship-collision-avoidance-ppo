using System.Collections;
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class CustomVelocityGridSensorComponent : GridSensorComponent
{
    protected override GridSensorBase[] GetGridSensors()
    {
        return new GridSensorBase[] {
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
    }
}

