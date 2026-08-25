using System;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(RequireInterfaceAttribute))]
public class RequireInterfaceDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        RequireInterfaceAttribute requireInterface = attribute as RequireInterfaceAttribute;
        Type interfaceType = requireInterface.InterfaceType;

        if (!interfaceType.IsInterface)
        {
            EditorGUI.HelpBox(
                position,
                $"[RequireInterface] {interfaceType.Name} is not an interface.",
                UnityEditor.MessageType.Error
            );
            return;
        }

        EditorGUI.BeginChangeCheck();

        UnityEngine.Object obj = EditorGUI.ObjectField(
            position,
            label,
            property.objectReferenceValue,
            typeof(UnityEngine.Object),
            true
        );

        if (EditorGUI.EndChangeCheck())
        {
            if (obj == null)
            {
                property.objectReferenceValue = null;
            }
            else
            {
                // Validation logic
                UnityEngine.Object validatedObj = null;

                if (obj is GameObject go)
                {
                    validatedObj = go.GetComponent(interfaceType);
                }
                else if (obj is Component c)
                {
                    validatedObj = c.GetComponent(interfaceType);
                }
                else if (interfaceType.IsInstanceOfType(obj))
                {
                    validatedObj = obj;
                }

                if (validatedObj != null)
                {
                    property.objectReferenceValue = validatedObj;
                }
                else
                {
                    Debug.LogError($"{obj.name} does not implement interface {interfaceType.Name}");
                    property.objectReferenceValue = null;
                }
            }
        }
    }
}
