using UnityEngine;
using UnityEngine.InputSystem;

// contrato de input de movimiento: el movimiento solo conoce esto, no el Input System
public interface IMoveInput
{
    Vector2 Move { get; }
    bool JumpPressedThisFrame { get; }
    bool JumpReleasedThisFrame { get; }
    bool JumpHeld { get; }
}

// contrato de input de reinicio
public interface IRestartInput
{
    bool RestartHeld { get; }
}

// lee el teclado / gamepad y bloquea el cursor
public class CatInput : MonoBehaviour, IMoveInput, IRestartInput
{
    [SerializeField] bool lockCursor = true;

    InputAction moveAction;
    InputAction jumpAction;
    InputAction restartAction;

    public Vector2 Move => moveAction.ReadValue<Vector2>();
    public bool JumpPressedThisFrame => jumpAction.WasPressedThisFrame();
    public bool JumpReleasedThisFrame => jumpAction.WasReleasedThisFrame();
    public bool JumpHeld => jumpAction.IsPressed();
    public bool RestartHeld => restartAction.IsPressed();

    void Awake()
    {
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

        // crea el input de reinicio (R, select del gamepad)
        restartAction = new InputAction("Restart", InputActionType.Button);
        restartAction.AddBinding("<Keyboard>/r");
        restartAction.AddBinding("<Gamepad>/select");
    }

    void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
        restartAction.Enable();
    }

    void OnDisable()
    {
        moveAction.Disable();
        jumpAction.Disable();
        restartAction.Disable();
    }

    void Start()
    {
        ApplyCursor();
    }

    // vuelve a bloquear el cursor al recuperar el foco
    void OnApplicationFocus(bool focus)
    {
        if (focus) ApplyCursor();
    }

    // bloquea y oculta el cursor
    void ApplyCursor()
    {
        if (!lockCursor) return;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}