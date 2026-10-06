using UnityEngine;

// guarda todos los valores de movimiento del gato (Create > Cat > Movement Config)
[CreateAssetMenu(fileName = "CatMoveConfig", menuName = "Cat/Movement Config")]
public class CatMoveConfig : ScriptableObject
{
    [Header("Movimiento")]
    public float walkSpeed = 11f;
    public float acceleration = 60f;
    public float deceleration = 80f;
    public float rotationSpeed = 900f;
    public float chargingRotationSpeed = 300f;

    [Header("Salto cargado")]
    public float minJumpHeight = 1.5f;
    public float maxJumpHeight = 12f;
    public float minJumpSpeed = 5f;
    public float maxJumpSpeed = 16f;
    public float maxChargeTime = 1.6f;
    public AnimationCurve chargeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public bool autoReleaseAtMax = true;
    public bool jumpForwardIfNoInput = true;
    [Range(0f, 1f)] public float airControl = 0.05f;
    public float jumpBufferTime = 0.2f;
    [Range(0f, 1f)] public float wallHitSpeedKeep = 0.3f;

    [Header("Gravedad")]
    public float gravity = 40f;
    public float fallMultiplier = 1.5f;
    public float maxFallSpeed = 40f;
    public float groundedStick = -2f;
    public float coyoteTime = 0.1f;

    [Header("Aterrizaje")]
    public float landingLockTime = 0.1f;   // bloqueo en caidas chicas
    public float stickyLockTime = 0.3f;    // bloqueo en caidas fuertes
    public float softFallHeight = 2f;      // debajo de esto el impacto es minimo
    public float rollFallHeight = 16f;     // desde esta caida rueda (medida desde el punto mas alto)
    public float rollDuration = 0.6f;
    public float rollSpeed = 8f;
    public float rollRecoveryTime = 0.1f;

    [Header("Ledge nudge")]
    public float ledgeNudgeHeight = 0.4f;      // cuanto lo sube como maximo
    public float ledgeNudgeStep = 0.05f;
    public float ledgeNudgeMaxUpSpeed = 4f;    // solo si no esta subiendo rapido
}