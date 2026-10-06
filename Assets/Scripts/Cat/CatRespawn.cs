using System;
using Unity.Cinemachine;
using UnityEngine;

// mantener R reinicia al ultimo checkpoint (o al inicio si no hay ninguno)
[RequireComponent(typeof(CatMovement), typeof(CatInput))]
public class CatRespawn : MonoBehaviour
{
    [SerializeField] CheckpointManager checkpoints;
    [SerializeField] CinemachineCamera cinemachineCamera;   // opcional, evita que la camara "vuele"
    [SerializeField] float holdTime = 0.8f;

    public event Action Respawned;

    // progreso de mantener el boton (0 a 1), para una barra de UI futura
    public float RestartProgress { get; private set; }

    CatMovement movement;
    IRestartInput input;
    Vector3 startPosition;
    Quaternion startRotation;
    bool needRelease;

    void Awake()
    {
        movement = GetComponent<CatMovement>();
        input = GetComponent<IRestartInput>();
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    void Start()
    {
        // si hay progreso guardado, empieza en ese checkpoint
        if (checkpoints != null && checkpoints.HasCheckpoint) Respawn();
    }

    void Update()
    {
        // al soltar se reinicia el progreso
        if (!input.RestartHeld)
        {
            RestartProgress = 0f;
            needRelease = false;
            return;
        }

        // despues de reaparecer hay que soltar antes de volver a reiniciar
        if (needRelease) return;

        RestartProgress += Time.deltaTime / holdTime;
        if (RestartProgress >= 1f)
        {
            Respawn();
            RestartProgress = 0f;
            needRelease = true;
        }
    }

    // lleva al gato al ultimo checkpoint
    void Respawn()
    {
        Vector3 position = startPosition;
        Quaternion rotation = startRotation;

        if (checkpoints != null && checkpoints.TryGetRespawn(out Vector3 p, out Quaternion r))
        {
            position = p;
            rotation = r;
        }

        Vector3 delta = position - transform.position;
        movement.TeleportTo(position, rotation);

        // avisa a la camara del salto de posicion
        if (cinemachineCamera != null) cinemachineCamera.OnTargetObjectWarped(transform, delta);

        Respawned?.Invoke();
    }
}