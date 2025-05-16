// CustomVelocityGridSensor.cs
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using System.Collections.Generic;

public class CustomVelocityGridSensor : GridSensorBase
{
    public CustomVelocityGridSensor(string name, int cellObservationSize, Vector3 cellScale, Vector3Int gridSize,
        GameObject agent, LayerMask mask, string[] detectableTags, bool rotateWithAgent)
        : base(name, cellScale, gridSize, detectableTags, SensorCompressionType.None)
    {
    }

    protected override int GetCellObservationSize()
    {
        // vx, vz, sinθ, cosθ, occupancy = 5
        return 5;
    }

    protected override void GetObjectData(GameObject detectedObject, int tagIndex, float[] dataBuffer)
    {
        var rb = detectedObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 velocity = rb.velocity;
            float vx = Mathf.Clamp(velocity.x, -10f, 10f) / 10f;
            float vz = Mathf.Clamp(velocity.z, -10f, 10f) / 10f;

            float angleRad = detectedObject.transform.rotation.eulerAngles.y * Mathf.Deg2Rad;
            float sinTheta = Mathf.Sin(angleRad);
            float cosTheta = Mathf.Cos(angleRad);

            dataBuffer[0] = vx;           // Normalized vx
            dataBuffer[1] = vz;           // Normalized vz
            dataBuffer[2] = sinTheta;     // Direction
            dataBuffer[3] = cosTheta;
            dataBuffer[4] = 1f;           // Occupancy
        }
    }

    protected override bool IsDataNormalized() => false;

    protected override ProcessCollidersMethod GetProcessCollidersMethod()
    {
        return ProcessCollidersMethod.ProcessClosestColliders;
    }

}
