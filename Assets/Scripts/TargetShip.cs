using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class TargetShip : MonoBehaviour
{
    private Vector3 moveDirection;
    private float speed;

    public void Initialize(Vector3 direction, float speed)
    {
        this.moveDirection = direction.normalized;
        this.speed = speed;
    }

    void Update()
    {
        transform.position += moveDirection * speed * Time.deltaTime;
    }
}

