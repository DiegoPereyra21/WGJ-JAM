using System;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.SceneManagement;

// guarda cual es el ultimo checkpoint, lo guarda en disco y lo carga al iniciar
public class CheckpointManager : MonoBehaviour
{
    public event Action<Checkpoint> CheckpointActivated;

    Checkpoint[] checkpoints;
    Checkpoint current;

    // true si ya hay un checkpoint activo (por ejemplo cargado del guardado)
    public bool HasCheckpoint => current != null;

    void Awake()
    {
        // los checkpoints son hijos de este objeto, el indice es el orden en la jerarquia
        checkpoints = GetComponentsInChildren<Checkpoint>();
        for (int i = 0; i < checkpoints.Length; i++)
        {
            checkpoints[i].Index = i;
            checkpoints[i].Reached += OnCheckpointReached;
        }

        LoadProgress();
    }

    void OnDestroy()
    {
        if (checkpoints == null) return;
        foreach (Checkpoint cp in checkpoints)
        {
            if (cp != null) cp.Reached -= OnCheckpointReached;
        }
    }

    // devuelve el punto de reaparicion del ultimo checkpoint
    public bool TryGetRespawn(out Vector3 position, out Quaternion rotation)
    {
        if (current == null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        position = current.SpawnPosition;
        rotation = current.SpawnRotation;
        return true;
    }

    // activa el checkpoint y guarda el progreso
    void OnCheckpointReached(Checkpoint checkpoint)
    {
        if (checkpoint == current) return;

        current = checkpoint;
        SaveProgress();
        CheckpointActivated?.Invoke(checkpoint);
    }

    // guarda el checkpoint actual en disco
    void SaveProgress()
    {
        SaveSystem.Save(new SaveData
        {
            sceneName = SceneManager.GetActiveScene().name,
            checkpointIndex = current.Index
        });
    }

    // carga el checkpoint guardado si es de esta escena
    void LoadProgress()
    {
        SaveData data = SaveSystem.Load();
        if (data == null) return;
        if (data.sceneName != SceneManager.GetActiveScene().name) return;
        if (data.checkpointIndex < 0 || data.checkpointIndex >= checkpoints.Length) return;

        current = checkpoints[data.checkpointIndex];
    }

    // borra el progreso guardado (clic derecho en el componente)
    [ContextMenu("Delete Save")]
    void DeleteSave()
    {
        SaveSystem.Delete();
    }
}