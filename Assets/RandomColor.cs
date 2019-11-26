using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RandomColor : MonoBehaviour
{
    
    void Awake() 
    {
        OnValidate();
    }

    // Start is called before the first frame update
    void OnValidate() 
    {
        InstancedColor[] insColors = GetComponentsInChildren<InstancedColor>(true);

        foreach(var insColor in insColors)
        {
            insColor.color = Random.ColorHSV();
        }
    }

}
