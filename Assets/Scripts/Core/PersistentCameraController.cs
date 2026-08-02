using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
[RequireComponent(typeof(CinemachineConfiner2D))]
public sealed class PersistentCameraController : MonoBehaviour
{
    private const string PersistentSceneName = "Persistent";
    private const string CameraConfinerName = "CameraConfiner";

    private CinemachineCamera cinemachineCamera;
    private CinemachineConfiner2D confiner;
    private Coroutine bindRoutine;

    private void Awake()
    {
        cinemachineCamera = GetComponent<CinemachineCamera>();
        confiner = GetComponent<CinemachineConfiner2D>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        BindToActiveLevel();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (bindRoutine != null)
        {
            StopCoroutine(bindRoutine);
            bindRoutine = null;
        }
    }

    private void OnSceneLoaded(Scene loadedScene, LoadSceneMode loadSceneMode)
    {
        if (loadedScene.name == PersistentSceneName)
            return;

        if (bindRoutine != null)
            StopCoroutine(bindRoutine);

        bindRoutine = StartCoroutine(BindAfterSceneInitialization(loadedScene));
    }

    private IEnumerator BindAfterSceneInitialization(Scene loadedScene)
    {
        yield return null;
        bindRoutine = null;

        if (loadedScene.isLoaded)
            BindToScene(loadedScene);
    }

    private void BindToActiveLevel()
    {
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (scene.isLoaded && scene.name != PersistentSceneName)
            {
                BindToScene(scene);
                return;
            }
        }
    }

    private void BindToScene(Scene scene)
    {
        PlayerController player = FindComponentInScene<PlayerController>(scene);
        Collider2D boundingShape = FindConfinerInScene(scene);

        if (player == null && boundingShape == null)
            return;

        if (player == null)
        {
            Debug.LogWarning($"Persistent camera could not find a {nameof(PlayerController)} in scene '{scene.name}'.", this);
            return;
        }

        if (boundingShape == null)
        {
            Debug.LogWarning($"Persistent camera could not find a Collider2D on '{CameraConfinerName}' in scene '{scene.name}'.", this);
            return;
        }

        cinemachineCamera.Follow = player.transform;
        cinemachineCamera.LookAt = player.transform;
        confiner.BoundingShape2D = boundingShape;
        confiner.InvalidateBoundingShapeCache();
        cinemachineCamera.CancelDamping(true);
    }

    private static Collider2D FindConfinerInScene(Scene scene)
    {
        GameObject[] rootObjects = scene.GetRootGameObjects();
        foreach (GameObject rootObject in rootObjects)
        {
            Transform[] transforms = rootObject.GetComponentsInChildren<Transform>(true);
            foreach (Transform transform in transforms)
            {
                if (transform.name == CameraConfinerName && transform.TryGetComponent(out Collider2D collider))
                    return collider;
            }
        }

        return null;
    }

    private static T FindComponentInScene<T>(Scene scene) where T : Component
    {
        GameObject[] rootObjects = scene.GetRootGameObjects();
        foreach (GameObject rootObject in rootObjects)
        {
            T component = rootObject.GetComponentInChildren<T>(true);
            if (component != null)
                return component;
        }

        return null;
    }
}
