using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class TargetShip : MonoBehaviour
{
    //private Quaternion moveDirection;
    private float speed;
    private float yawRate;
    private int category;

    public void Initialize(float speed, float yawRate, int category)
    {
        //this.moveDirection = direction;
        this.speed = speed;
        this.yawRate = yawRate;
        this.category = category;
    }

    void Update()
    {
        transform.Rotate(0, yawRate * Time.deltaTime, 0);
        transform.position += transform.forward * speed * Time.deltaTime;
    }

    public Vector3 GetVelocity()
    {
        Vector3 linearVelocity = transform.forward * speed;
        return linearVelocity;
    }

    public int GetCategory()
    {
        return category;
    }
}

