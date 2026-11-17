using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public abstract class SceneSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    public static T Instance { get; private set; }
    protected virtual void Awake() => Instance = this as T;

    protected virtual void OnApplicationQuit() {
        Instance = null;
        Destroy(gameObject);
    }
}

public abstract class Singleton<T> where T : new() {
    private static T m_instance;
    public static T Instance { 
        get {
            if (m_instance == null) m_instance = new();
            return m_instance;
        } private set {}
    }
}

public abstract class SceneSingletonPersistent<T> : SceneSingleton<T> where T : MonoBehaviour
{
    protected override void Awake()
    {
        if (Instance != null) Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
        base.Awake();
    }
}
