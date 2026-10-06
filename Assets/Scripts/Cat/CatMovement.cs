using System;
using UnityEngine;

// logica de movimiento del gato: caminar, salto cargado, gravedad, aterrizaje, roll y ledge nudge
[RequireComponent(typeof(CharacterController), typeof(CatInput))]
public class CatMovement : MonoBehaviour
{
    [SerializeField] CatMoveConfig config;
    [SerializeField] Transform cameraTransform;   // si esta vacio usa Camera.main

    // eventos para efectos, sonido, UI, etc.
    public event Action<float> Jumped;            // carga del salto (0 a 1, ya con la curva)
    public event Action<float, bool> Landed;      // impacto (0 a 1) y si rueda

    // estado publico
    public bool IsCharging { get; private set; }
    public float ChargeNormalized { get; private set; }
    public bool IsGrounded { get; private set; }
    public bool IsRolling { get; private set; }
    public float RollAngle { get; private set; }
    public float VerticalVelocity => verticalVel;

    CharacterController controller;
    IMoveInput input;

    Vector3 horizontalVel;
    float verticalVel;
    float chargeTime;
    float coyoteTimer;
    float landingTimer;
    float jumpBufferTimer;
    float apexY;
    bool wasGrounded;

    // roll
    Vector3 rollDir;
    float rollTimer;

    // buffers para chequeos de fisica
    readonly Collider[] overlapBuffer = new Collider[8];
    readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        input = GetComponent<IMoveInput>();

        if (config == null)
        {
            Debug.LogError("CatMovement: falta asignar el CatMoveConfig", this); enabled = false;
        }
    }

    void Start()
    {
        apexY = transform.position.y;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

        // chequeo de suelo y coyote time
        bool grounded = controller.isGrounded;
        IsGrounded = grounded;
        if (grounded) coyoteTimer = config.coyoteTime;
        else coyoteTimer -= dt;
        bool groundControl = grounded || coyoteTimer > 0f;

        // registra altura maxima y detecta el aterrizaje
        if (grounded)
        {
            if (!wasGrounded) OnLanded(apexY - transform.position.y);
            apexY = transform.position.y;
        }
        else
        {
            apexY = Mathf.Max(apexY, transform.position.y);
        }
        wasGrounded = grounded;

        // jump buffer: recuerda el salto pulsado en el aire o durante el aterrizaje
        if (input.JumpPressedThisFrame && (!groundControl || landingTimer > 0f || IsRolling))
            jumpBufferTimer = config.jumpBufferTime;
        if (!groundControl) jumpBufferTimer -= dt;
        if (!input.JumpHeld) jumpBufferTimer = 0f;

        Vector3 moveDir = GetCameraRelativeInput();

        if (groundControl)
        {
            if (IsRolling)
            {
                UpdateRoll(dt);
            }
            else if (landingTimer > 0f)
            {
                // recuperacion de aterrizaje
                landingTimer -= dt;
                horizontalVel = Vector3.MoveTowards(horizontalVel, Vector3.zero, config.deceleration * dt);
            }
            else if (IsCharging)
            {
                HandleCharging(moveDir, dt);
            }
            else
            {
                // empieza a cargar el salto (o usa el salto guardado)
                if (input.JumpPressedThisFrame || jumpBufferTimer > 0f)
                {
                    IsCharging = true;
                    chargeTime = 0f;
                    jumpBufferTimer = 0f;
                }
                else
                {
                    HandleWalking(moveDir, dt);
                }
            }
        }
        else
        {
            // cancela la carga y el roll si se cae
            IsCharging = false;
            ChargeNormalized = 0f;
            if (IsRolling) { IsRolling = false; RollAngle = 0f; }
            HandleAir(moveDir, dt);
        }

        ApplyGravity(grounded, dt);

        // mueve el controller
        Vector3 vel = new Vector3(horizontalVel.x, verticalVel, horizontalVel.z);
        CollisionFlags flags = controller.Move(vel * dt);

        // golpe en el techo
        if ((flags & CollisionFlags.Above) != 0 && verticalVel > 0f) verticalVel = 0f;

        // golpe en pared en el aire: intenta subirse al borde, si no frena
        if ((flags & CollisionFlags.Sides) != 0 && !grounded)
        {
            if (!TryLedgeNudge()) horizontalVel *= config.wallHitSpeedKeep;
        }
    }

    // input relativo a la camara
    Vector3 GetCameraRelativeInput()
    {
        Vector2 move = input.Move;
        if (move.sqrMagnitude < 0.01f) return Vector3.zero;

        Vector3 fwd = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
        Vector3 right = cameraTransform != null ? cameraTransform.right : Vector3.right;
        fwd.y = 0f; right.y = 0f;
        fwd.Normalize(); right.Normalize();

        Vector3 dir = fwd * move.y + right * move.x;
        return Vector3.ClampMagnitude(dir, 1f);
    }

    // caminar con aceleracion y rotacion suave
    void HandleWalking(Vector3 moveDir, float dt)
    {
        Vector3 target = moveDir * config.walkSpeed;
        float rate = moveDir.sqrMagnitude > 0.01f ? config.acceleration : config.deceleration;
        horizontalVel = Vector3.MoveTowards(horizontalVel, target, rate * dt);

        RotateTowards(moveDir, config.rotationSpeed, dt);
    }

    // carga el salto: frena, apunta y suelta al soltar el boton
    void HandleCharging(Vector3 moveDir, float dt)
    {
        horizontalVel = Vector3.MoveTowards(horizontalVel, Vector3.zero, config.deceleration * dt);
        RotateTowards(moveDir, config.chargingRotationSpeed, dt);

        chargeTime += dt;
        ChargeNormalized = Mathf.Clamp01(chargeTime / config.maxChargeTime);

        bool released = input.JumpReleasedThisFrame || !input.JumpHeld;
        bool maxed = config.autoReleaseAtMax && chargeTime >= config.maxChargeTime;

        if (released || maxed) Jump(moveDir);
    }

    // ejecuta el salto segun la carga
    void Jump(Vector3 moveDir)
    {
        float c = config.chargeCurve.Evaluate(ChargeNormalized);
        float height = Mathf.Lerp(config.minJumpHeight, config.maxJumpHeight, c);
        float speed = Mathf.Lerp(config.minJumpSpeed, config.maxJumpSpeed, c);

        // velocidad vertical a partir de la altura deseada
        verticalVel = Mathf.Sqrt(2f * config.gravity * height);

        // direccion: input al soltar, si no hay usa hacia donde mira (o vertical)
        Vector3 dir = moveDir;
        if (dir.sqrMagnitude < 0.01f)
            dir = config.jumpForwardIfNoInput ? transform.forward : Vector3.zero;
        dir.y = 0f;
        dir.Normalize();

        horizontalVel = dir * speed;
        if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir);

        IsCharging = false;
        ChargeNormalized = 0f;
        chargeTime = 0f;
        coyoteTimer = 0f;

        Jumped?.Invoke(c);
    }

    // control minimo en el aire
    void HandleAir(Vector3 moveDir, float dt)
    {
        if (config.airControl <= 0f || moveDir.sqrMagnitude < 0.01f) return;
        Vector3 target = moveDir * config.maxJumpSpeed;
        horizontalVel = Vector3.MoveTowards(horizontalVel, target, config.airControl * config.acceleration * dt);
    }

    // gravedad con caida mas rapida
    void ApplyGravity(bool grounded, float dt)
    {
        if (grounded && verticalVel <= 0f)
        {
            // pega al suelo
            verticalVel = config.groundedStick;
            return;
        }

        float g = verticalVel < 0f ? config.gravity * config.fallMultiplier : config.gravity;
        verticalVel -= g * dt;
        if (verticalVel < -config.maxFallSpeed) verticalVel = -config.maxFallSpeed;
    }

    // al aterrizar: decide bloqueo o roll segun la altura de caida
    void OnLanded(float fallHeight)
    {
        float impact = Mathf.InverseLerp(config.softFallHeight, config.rollFallHeight, fallHeight);
        Vector3 flat = new Vector3(horizontalVel.x, 0f, horizontalVel.z);
        horizontalVel = Vector3.zero;

        bool rolls = fallHeight >= config.rollFallHeight;
        if (rolls) StartRoll(flat);
        else landingTimer = Mathf.Lerp(config.landingLockTime, config.stickyLockTime, impact);

        Landed?.Invoke(impact, rolls);
    }

    // empieza la rodada
    void StartRoll(Vector3 flat)
    {
        rollDir = flat.sqrMagnitude > 0.25f ? flat.normalized : transform.forward;
        rollDir.y = 0f;
        rollDir.Normalize();
        transform.rotation = Quaternion.LookRotation(rollDir);

        IsRolling = true;
        IsCharging = false;
        rollTimer = 0f;
        landingTimer = 0f;
    }

    // avanza la rodada
    void UpdateRoll(float dt)
    {
        rollTimer += dt;
        float t = Mathf.Clamp01(rollTimer / config.rollDuration);
        RollAngle = 360f * (1f - (1f - t) * (1f - t));
        horizontalVel = rollDir * Mathf.Lerp(config.rollSpeed, 0f, t);

        if (t >= 1f)
        {
            IsRolling = false;
            RollAngle = 0f;
            horizontalVel = Vector3.zero;
            landingTimer = config.rollRecoveryTime;
        }
    }

    // si casi llega a un borde, lo sube un poco para que quede encima
    bool TryLedgeNudge()
    {
        Vector3 dir = new Vector3(horizontalVel.x, 0f, horizontalVel.z);
        if (dir.sqrMagnitude < 0.25f || verticalVel > config.ledgeNudgeMaxUpSpeed) return false;
        dir.Normalize();

        float probe = controller.radius * 1.2f;
        for (float h = config.ledgeNudgeStep; h <= config.ledgeNudgeHeight; h += config.ledgeNudgeStep)
        {
            // busca la menor altura donde el cuerpo entra libre sobre el borde
            if (!CapsuleFree(Vector3.up * h + dir * probe)) continue;

            // verifica que haya suelo para apoyarse
            Vector3 origin = transform.position + dir * probe + Vector3.up * (h + 0.05f);
            if (HasGround(origin, config.ledgeNudgeStep + 0.15f))
            {
                controller.Move(Vector3.up * h);
                verticalVel = 0f;
                return true;
            }
            return false;
        }
        return false;
    }

    // revisa si la capsula del gato entra libre en esa posicion
    bool CapsuleFree(Vector3 offset)
    {
        float r = controller.radius * 0.95f;
        Vector3 center = transform.position + controller.center + offset;
        float half = Mathf.Max(controller.height * 0.5f - r, 0f);
        Vector3 p1 = center + Vector3.up * half;
        Vector3 p2 = center - Vector3.up * half;

        int n = Physics.OverlapCapsuleNonAlloc(p1, p2, r, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (!overlapBuffer[i].transform.IsChildOf(transform)) return false;
        }
        return true;
    }

    // revisa si hay suelo debajo de un punto
    bool HasGround(Vector3 origin, float distance)
    {
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, hitBuffer, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (hitBuffer[i].collider.transform.IsChildOf(transform)) continue;
            if (hitBuffer[i].normal.y > 0.5f) return true;
        }
        return false;
    }

    // rota el modelo hacia una direccion
    void RotateTowards(Vector3 dir, float speed, float dt)
    {
        if (dir.sqrMagnitude < 0.01f) return;
        Quaternion target = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, speed * dt);
    }

    // teletransporta al gato y reinicia su estado
    public void TeleportTo(Vector3 position, Quaternion rotation)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;

        horizontalVel = Vector3.zero;
        verticalVel = 0f;
        IsCharging = false;
        ChargeNormalized = 0f;
        chargeTime = 0f;
        IsRolling = false;
        RollAngle = 0f;
        landingTimer = 0f;
        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        apexY = position.y;
        wasGrounded = true; // evita un aterrizaje falso
    }
}