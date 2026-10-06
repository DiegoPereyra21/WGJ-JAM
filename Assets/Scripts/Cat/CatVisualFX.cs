using UnityEngine;

// animacion procedural del gato: solo escucha al movement, no decide gameplay
public class CatVisualFX : MonoBehaviour
{
    [SerializeField] CatMovement movement;           // si esta vacio lo busca en este objeto
    [SerializeField] Transform visual;         // hijo con el modelo, pivot en los pies
    [SerializeField] Transform tail;           // opcional
    [SerializeField] Vector3 tailAxis = Vector3.up;
    [SerializeField] float bodyCenterHeight = 0.3f;   // altura del centro del cuerpo

    [Header("Squash & stretch")]
    [SerializeField] float chargeSquash = 0.35f;
    [SerializeField] float jumpStretchImpulse = 4f;
    [SerializeField] float landSquashMin = 1.5f;
    [SerializeField] float landSquashMax = 9f;
    [SerializeField] float rollSquash = 3f;
    [SerializeField] float squashStiffness = 180f;
    [SerializeField] float squashDamping = 12f;

    [Header("Inclinacion en el aire")]
    [SerializeField] float maxAirPitch = 35f;
    [SerializeField] float pitchPerSpeed = 2.5f;
    [SerializeField] float pitchSmoothing = 12f;

    [Header("Carga")]
    [SerializeField] float chargeShake = 0.02f;

    Vector3 visualBasePos;
    Quaternion visualBaseRot;
    Vector3 visualBaseScale;
    Quaternion tailBaseRot;

    float squash;
    float squashVel;
    float pitch;
    float tailPhase;
    float tailAmp;

    void Awake()
    {
        if (movement == null) movement = GetComponent<CatMovement>();

        // guarda el estado base del visual
        if (visual != null)
        {
            visualBasePos = visual.localPosition;
            visualBaseRot = visual.localRotation;
            visualBaseScale = visual.localScale;
        }
        if (tail != null) tailBaseRot = tail.localRotation;
    }

    void OnEnable()
    {
        if (movement == null) return;
        movement.Jumped += OnJumped;
        movement.Landed += OnLanded;
    }

    void OnDisable()
    {
        if (movement == null) return;
        movement.Jumped -= OnJumped;
        movement.Landed -= OnLanded;
    }

    // estiramiento al despegar
    void OnJumped(float charge)
    {
        squashVel += Mathf.Lerp(jumpStretchImpulse * 0.5f, jumpStretchImpulse, charge);
    }

    // aplastamiento al aterrizar segun el impacto
    void OnLanded(float impact, bool rolled)
    {
        squashVel -= rolled ? rollSquash : Mathf.Lerp(landSquashMin, landSquashMax, impact);
    }

    void LateUpdate()
    {
        if (movement == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // resorte de squash (negativo = aplastado, positivo = estirado)
        float target = movement.IsCharging ? -chargeSquash * movement.ChargeNormalized : 0f;
        float acc = (target - squash) * squashStiffness - squashVel * squashDamping;
        squashVel += acc * dt;
        squash += squashVel * dt;
        squash = Mathf.Clamp(squash, -0.6f, 0.6f);

        // inclinacion segun velocidad vertical en el aire
        float targetPitch = 0f;
        if (!movement.IsGrounded && !movement.IsRolling)
            targetPitch = Mathf.Clamp(-movement.VerticalVelocity * pitchPerSpeed, -maxAirPitch, maxAirPitch);
        pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-pitchSmoothing * dt));

        UpdateBody();
        UpdateTail(dt);
    }

    // escala, rotacion y temblor del cuerpo
    void UpdateBody()
    {
        if (visual == null) return;

        // escala conservando volumen aproximado
        float sy = 1f + squash;
        float sxz = 1f - squash * 0.5f;
        visual.localScale = new Vector3(visualBaseScale.x * sxz, visualBaseScale.y * sy, visualBaseScale.z * sxz);

        // rotacion (inclinacion + roll) alrededor del centro del cuerpo
        Quaternion rot = Quaternion.Euler(pitch + movement.RollAngle, 0f, 0f);
        Vector3 pivot = Vector3.up * bodyCenterHeight;
        Vector3 pos = visualBasePos + pivot - rot * pivot;

        // temblor al cargar fuerte
        if (movement.IsCharging)
        {
            float s = movement.ChargeNormalized * movement.ChargeNormalized;
            pos += Random.insideUnitSphere * (chargeShake * s);
        }

        visual.localRotation = rot * visualBaseRot;
        visual.localPosition = pos;
    }

    // cola: mueve mas rapido y fuerte mientras carga
    void UpdateTail(float dt)
    {
        if (tail == null) return;

        float freq = movement.IsCharging ? Mathf.Lerp(4f, 20f, movement.ChargeNormalized) : 2f;
        float amp = movement.IsCharging ? Mathf.Lerp(10f, 30f, movement.ChargeNormalized) : 8f;
        tailAmp = Mathf.Lerp(tailAmp, amp, 1f - Mathf.Exp(-8f * dt));
        tailPhase += freq * dt;
        tail.localRotation = tailBaseRot * Quaternion.AngleAxis(Mathf.Sin(tailPhase) * tailAmp, tailAxis);
    }
}