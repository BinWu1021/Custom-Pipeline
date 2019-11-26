using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InstancedColor : MonoBehaviour
{
    public Color color = Color.white;

    void Awake() 
    {
        OnValidate();
    }

    void OnValidate() 
    {
        var propertyBlock = new MaterialPropertyBlock();
		propertyBlock.SetColor("_Color", color);
		GetComponent<MeshRenderer>().SetPropertyBlock(propertyBlock);
    }
}
