using UnityEngine;
using System;

public class RequireInterfaceAttribute : PropertyAttribute
{
    public Type InterfaceType { get; private set; }

    public RequireInterfaceAttribute(Type interfaceType)
    {
        this.InterfaceType = interfaceType;
    }
}
