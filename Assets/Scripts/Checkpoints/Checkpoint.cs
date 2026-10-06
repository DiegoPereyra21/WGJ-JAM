using System;
using UnityEngine;

// zona trigger que avisa cuando el gato la toca
[RequireComponent(typeof(Collider))]
public class Checkpoint : MonoBehaviour
{
    [SerializeField] Transform spawnPoint;   // si esta vacio usa este objeto

    public event Action<Checkpoint> Reached;

    // lo asigna el CheckpointManager segun el orden en la jerarquia
    public int Index { get; internal set; }

    public Vector3 SpawnPosition => spawnPoint != null ? spawnPoint.position : transform.position;
    public Quaternion SpawnRotation => spawnPoint != null ? spawnPoint.rotation : transform.rotation;

    // deja el collider como trigger al agregar el componente
    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<CatMovement>() != null) Reached?.Invoke(this);
    }

    // muestra el punto de reaparicion en el editor
    void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(SpawnPosition, 0.3f);
        Gizmos.DrawRay(SpawnPosition, SpawnRotation * Vector3.forward);
    }
}