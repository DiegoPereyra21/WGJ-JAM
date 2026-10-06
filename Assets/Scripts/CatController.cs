using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class CatController : MonoBehaviour
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
    public float rollFallHeight = 16f;     // desde esta caida (medida desde el punto mas alto) rueda
    public float rollDuration = 0.6f;
    public float rollSpeed = 8f;
    public float rollRecoveryTime = 0.1f;

    [Header("Ledge nudge")]
    public float ledgeNudgeHeight = 0.4f;      // cuanto lo sube como maximo
    public float ledgeNudgeStep = 0.05f;
    public float ledgeNudgeMaxUpSpeed = 4f;    // solo si no esta subiendo rapido

    [Header("Visual procedural")]
    public Transform visual;                   // hijo con el modelo, pivot en los pies
    public Transform tail;                     // opcional
    public Vector3 tailAxis = Vector3.up;
    public float bodyCenterHeight = 0.3f;      // altura del centro del cuerpo (para rodar/inclinar)
    public float chargeSquash = 0.35f;
    public float jumpStretchImpulse = 4f;
    public float squashStiffness = 180f;
    public float squashDamping = 12f;
    public float maxAirPitch = 35f;
    public float pitchPerSpeed = 2.5f;
    public float pitchSmoothing = 12f;
    public float chargeShake = 0.02f;

    // estado publico para animator / UI
    public bool IsCharging { get; private set; }
    public float ChargeNormalized { get; private set; }
    public bool IsGrounded { get; private set; }
    public bool IsRolling { get; private set; }

    CharacterController controller;
    Transform cam;
    InputAction moveAction;
    InputAction jumpAction;

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
    float rollAngle;

    // visual
    Vector3 visualBasePos;
    Quaternion visualBaseRot;
    Vector3 visualBaseScale;
    Quaternion tailBaseRot;
    float squash;
    float squashVel;
    float pitch;
    float tailPhase;
    float tailAmp;

    // buffers para chequeos de fisica
    readonly Collider[] overlapBuffer = new Collider[8];
    readonly RaycastHit[] hitBuffer = new RaycastHit[8];

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        cam = Camera.main != null ? Camera.main.transform : null;

        // guarda el estado base del visual
        if (visual != null)
        {
            visualBasePos = visual.localPosition;
            visualBaseRot = visual.localRotation;
            visualBaseScale = visual.localScale;
        }
        if (tail != null) tailBaseRot = tail.localRotation;

        // crea el input de movimiento (WASD, flechas, stick)
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
        moveAction.AddBinding("<Gamepad>/leftStick");

        // crea el input de salto (espacio, boton sur del gamepad)
        jumpAction = new InputAction("Jump", InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");
    }

    // bloquea y oculta el cursor
    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        apexY = transform.position.y;
    }

    // vuelve a bloquearlo al recuperar el foco de la ventana
    void OnApplicationFocus(bool focus)
    {
        if (!focus) return;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
    }

    void OnDisable()
    {
        moveAction.Disable();
        jumpAction.Disable();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (cam == null && Camera.main != null) cam = Camera.main.transform;

        // chequeo de suelo y coyote time
        bool grounded = controller.isGrounded;
        IsGrounded = grounded;
        if (grounded) coyoteTimer = coyoteTime;
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
        if (jumpAction.WasPressedThisFrame() && (!groundControl || landingTimer > 0f || IsRolling))
            jumpBufferTimer = jumpBufferTime;
        if (!groundControl) jumpBufferTimer -= dt;
        if (!jumpAction.IsPressed()) jumpBufferTimer = 0f;

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
                horizontalVel = Vector3.MoveTowards(horizontalVel, Vector3.zero, deceleration * dt);
            }
            else if (IsCharging)
            {
                HandleCharging(moveDir, dt);
            }
            else
            {
                // empieza a cargar el salto (o usa el salto guardado)
                if (jumpAction.WasPressedThisFrame() || jumpBufferTimer > 0f)
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
            if (IsRolling) { IsRolling = false; rollAngle = 0f; }
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
            if (!TryLedgeNudge()) horizontalVel *= wallHitSpeedKeep;
        }
    }

    void LateUpdate()
    {
        UpdateVisual(Time.deltaTime);
    }

    // input relativo a la camara
    Vector3 GetCameraRelativeInput()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();
        if (input.sqrMagnitude < 0.01f) return Vector3.zero;

        Vector3 fwd = cam != null ? cam.forward : Vector3.forward;
        Vector3 right = cam != null ? cam.right : Vector3.right;
        fwd.y = 0f; right.y = 0f;
        fwd.Normalize(); right.Normalize();

        Vector3 dir = fwd * input.y + right * input.x;
        return Vector3.ClampMagnitude(dir, 1f);
    }

    // caminar con aceleracion y rotacion suave
    void HandleWalking(Vector3 moveDir, float dt)
    {
        Vector3 target = moveDir * walkSpeed;
        float rate = moveDir.sqrMagnitude > 0.01f ? acceleration : deceleration;
        horizontalVel = Vector3.MoveTowards(horizontalVel, target, rate * dt);

        RotateTowards(moveDir, rotationSpeed, dt);
    }

    // carga el salto: frena, apunta y suelta al soltar el boton
    void HandleCharging(Vector3 moveDir, float dt)
    {
        horizontalVel = Vector3.MoveTowards(horizontalVel, Vector3.zero, deceleration * dt);
        RotateTowards(moveDir, chargingRotationSpeed, dt);

        chargeTime += dt;
        ChargeNormalized = Mathf.Clamp01(chargeTime / maxChargeTime);

        bool released = jumpAction.WasReleasedThisFrame() || !jumpAction.IsPressed();
        bool maxed = autoReleaseAtMax && chargeTime >= maxChargeTime;

        if (released || maxed) Jump(moveDir);
    }

    // ejecuta el salto segun la carga
    void Jump(Vector3 moveDir)
    {
        float c = chargeCurve.Evaluate(ChargeNormalized);
        float height = Mathf.Lerp(minJumpHeight, maxJumpHeight, c);
        float speed = Mathf.Lerp(minJumpSpeed, maxJumpSpeed, c);

        // velocidad vertical a partir de la altura deseada
        verticalVel = Mathf.Sqrt(2f * gravity * height);

        // direccion: input al soltar, si no hay usa hacia donde mira (o vertical)
        Vector3 dir = moveDir;
        if (dir.sqrMagnitude < 0.01f)
            dir = jumpForwardIfNoInput ? transform.forward : Vector3.zero;
        dir.y = 0f;
        dir.Normalize();

        horizontalVel = dir * speed;
        if (dir.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(dir);

        // impulso de estiramiento (stretch) al despegar
        squashVel += Mathf.Lerp(jumpStretchImpulse * 0.5f, jumpStretchImpulse, c);

        IsCharging = false;
        ChargeNormalized = 0f;
        chargeTime = 0f;
        coyoteTimer = 0f;
    }

    // control minimo en el aire
    void HandleAir(Vector3 moveDir, float dt)
    {
        if (airControl <= 0f || moveDir.sqrMagnitude < 0.01f) return;
        Vector3 target = moveDir * maxJumpSpeed;
        horizontalVel = Vector3.MoveTowards(horizontalVel, target, airControl * acceleration * dt);
    }

    // gravedad con caida mas rapida
    void ApplyGravity(bool grounded, float dt)
    {
        if (grounded && verticalVel <= 0f)
        {
            // pega al suelo
            verticalVel = groundedStick;
            return;
        }

        float g = verticalVel < 0f ? gravity * fallMultiplier : gravity;
        verticalVel -= g * dt;
        if (verticalVel < -maxFallSpeed) verticalVel = -maxFallSpeed;
    }

    // al aterrizar: decide rebote, bloqueo o roll segun la altura de caida
    void OnLanded(float fallHeight)
    {
        float impact = Mathf.InverseLerp(softFallHeight, rollFallHeight, fallHeight);
        Vector3 flat = new Vector3(horizontalVel.x, 0f, horizontalVel.z);
        horizontalVel = Vector3.zero;

        if (fallHeight >= rollFallHeight)
        {
            // caida muy alta: rueda
            squashVel -= 3f;
            StartRoll(flat);
        }
        else
        {
            // rebote visual y bloqueo mas largo segun el impacto
            squashVel -= Mathf.Lerp(1.5f, 9f, impact);
            landingTimer = Mathf.Lerp(landingLockTime, stickyLockTime, impact);
        }
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
        float t = Mathf.Clamp01(rollTimer / rollDuration);
        rollAngle = 360f * (1f - (1f - t) * (1f - t));
        horizontalVel = rollDir * Mathf.Lerp(rollSpeed, 0f, t);

        if (t >= 1f)
        {
            IsRolling = false;
            rollAngle = 0f;
            horizontalVel = Vector3.zero;
            landingTimer = rollRecoveryTime;
        }
    }

    // si casi llega a un borde, lo sube un poco para que quede encima
    bool TryLedgeNudge()
    {
        Vector3 dir = new Vector3(horizontalVel.x, 0f, horizontalVel.z);
        if (dir.sqrMagnitude < 0.25f || verticalVel > ledgeNudgeMaxUpSpeed) return false;
        dir.Normalize();

        float probe = controller.radius * 1.2f;
        for (float h = ledgeNudgeStep; h <= ledgeNudgeHeight; h += ledgeNudgeStep)
        {
            // busca la menor altura donde el cuerpo entra libre sobre el borde
            if (!CapsuleFree(Vector3.up * h + dir * probe)) continue;

            // verifica que haya suelo para apoyarse
            Vector3 origin = transform.position + dir * probe + Vector3.up * (h + 0.05f);
            if (HasGround(origin, ledgeNudgeStep + 0.15f))
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

    // animacion procedural: squash/stretch, temblor, inclinacion, roll y cola
    void UpdateVisual(float dt)
    {
        if (dt <= 0f) return;

        // resorte de squash (negativo = aplastado, positivo = estirado)
        float target = IsCharging ? -chargeSquash * ChargeNormalized : 0f;
        float acc = (target - squash) * squashStiffness - squashVel * squashDamping;
        squashVel += acc * dt;
        squash += squashVel * dt;
        squash = Mathf.Clamp(squash, -0.6f, 0.6f);

        // inclinacion segun velocidad vertical en el aire
        float targetPitch = 0f;
        if (!IsGrounded && !IsRolling)
            targetPitch = Mathf.Clamp(-verticalVel * pitchPerSpeed, -maxAirPitch, maxAirPitch);
        pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-pitchSmoothing * dt));

        if (visual != null)
        {
            // escala conservando volumen aproximado
            float sy = 1f + squash;
            float sxz = 1f - squash * 0.5f;
            visual.localScale = new Vector3(visualBaseScale.x * sxz, visualBaseScale.y * sy, visualBaseScale.z * sxz);

            // rotacion (inclinacion + roll) alrededor del centro del cuerpo
            Quaternion rot = Quaternion.Euler(pitch + rollAngle, 0f, 0f);
            Vector3 pivot = Vector3.up * bodyCenterHeight;
            Vector3 pos = visualBasePos + pivot - rot * pivot;

            // temblor al cargar fuerte
            if (IsCharging)
            {
                float s = ChargeNormalized * ChargeNormalized;
                pos += Random.insideUnitSphere * (chargeShake * s);
            }

            visual.localRotation = rot * visualBaseRot;
            visual.localPosition = pos;
        }

        // cola: mueve mas rapido y fuerte mientras carga
        if (tail != null)
        {
            float freq = IsCharging ? Mathf.Lerp(4f, 20f, ChargeNormalized) : 2f;
            float amp = IsCharging ? Mathf.Lerp(10f, 30f, ChargeNormalized) : 8f;
            tailAmp = Mathf.Lerp(tailAmp, amp, 1f - Mathf.Exp(-8f * dt));
            tailPhase += freq * dt;
            tail.localRotation = tailBaseRot * Quaternion.AngleAxis(Mathf.Sin(tailPhase) * tailAmp, tailAxis);
        }
    }
}