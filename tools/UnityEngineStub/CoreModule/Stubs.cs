using System;
using System.Collections;

// Numeric values mirror Unity 2022.3 so that compiled switch statements in Photon behave the same.

namespace UnityEngine
{
    public enum RuntimePlatform
    {
        OSXEditor = 0,
        OSXPlayer = 1,
        WindowsPlayer = 2,
        WindowsEditor = 7,
        IPhonePlayer = 8,
        Android = 11,
        LinuxPlayer = 13,
        LinuxEditor = 16,
        WebGLPlayer = 17,
        PS4 = 25,
        XboxOne = 27,
        tvOS = 31,
        Switch = 32,
        GameCoreXboxSeries = 36,
        GameCoreXboxOne = 37,
        PS5 = 38,
    }

    public enum NetworkReachability
    {
        NotReachable = 0,
        ReachableViaCarrierDataNetwork = 1,
        ReachableViaLocalAreaNetwork = 2,
    }

    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad = 0,
        BeforeSceneLoad = 1,
        AfterAssembliesLoaded = 2,
        BeforeSplashScreen = 3,
        SubsystemRegistration = 4,
    }

    public static class Debug
    {
        public static void Log(object message) => Console.WriteLine($"[Unity] {message}");
        public static void Log(object message, Object context) => Log(message);
        public static void LogWarning(object message) => Console.WriteLine($"[Unity:warn] {message}");
        public static void LogWarning(object message, Object context) => LogWarning(message);
        public static void LogError(object message) => Console.Error.WriteLine($"[Unity:error] {message}");
        public static void LogError(object message, Object context) => LogError(message);
    }

    public static class Application
    {
        public static RuntimePlatform platform => RuntimePlatform.WindowsPlayer;
        public static string unityVersion => "2022.3.67f2";
        public static NetworkReachability internetReachability => NetworkReachability.ReachableViaLocalAreaNetwork;
    }

    public class Object
    {
        public static void Destroy(Object obj) { }
        public static void Destroy(Object obj, float t) { }
        public static void DontDestroyOnLoad(Object target) { }

        public static bool operator ==(Object x, Object y) => ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !ReferenceEquals(x, y);
        public static implicit operator bool(Object exists) => exists is not null;

        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }

    public class Component : Object
    {
        internal GameObject owner;
        public GameObject gameObject => owner;
    }

    public class Behaviour : Component { }

    // Coroutines and Invoke are never driven here; the probe calls LoadBalancingClient.Service() itself.
    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) => new();
        public void Invoke(string methodName, float time) { }
        public void InvokeRepeating(string methodName, float time, float repeatRate) { }
        public void CancelInvoke() { }
        public void CancelInvoke(string methodName) { }
    }

    public sealed class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }

        public T AddComponent<T>() where T : Component
        {
            var component = Activator.CreateInstance<T>();
            component.owner = this;
            return component;
        }

        public Component AddComponent(Type componentType)
        {
            var component = (Component)Activator.CreateInstance(componentType);
            component.owner = this;
            return component;
        }
    }

    public class YieldInstruction { }
    public sealed class Coroutine : YieldInstruction { }
    public sealed class WaitForSeconds : YieldInstruction { public WaitForSeconds(float seconds) { } }
    public class WaitForSecondsRealtime { public WaitForSecondsRealtime(float time) { } }

    public struct Vector2 { public float x, y; }
    public struct Vector3 { public float x, y, z; }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => new() { w = 1f };
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AddComponentMenu : Attribute
    {
        public AddComponentMenu(string menuName) { }
        public AddComponentMenu(string menuName, int order) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
}
