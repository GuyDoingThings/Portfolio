using Synty.AnimationBaseLocomotion.Samples;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
using static UnityEngine.Rendering.ProbeAdjustmentVolume;

public class PlayerController : MonoBehaviour
{
    private Rigidbody playerRigidbody;
    public Vector3 intendedMovementDirection;

    #region AnimationSystem
    [SerializeField] SamplePlayerAnimationController sPAC;
    [SerializeField] private Transform cameraHeightReference; // Das GameObject, dessen Höhe die Kamera haben soll
    [SerializeField] private bool lockCameraYPosition = true;  // Optionaler Schalter im Inspector
    private float cameraHeightVelocity = 0f; // intern: für SmoothDamp
    #endregion

    #region AudioSources
    [SerializeField] private AudioSource swordStrikeSound;
    [SerializeField] private AudioSource sprintStepSound;
    [SerializeField] private AudioSource walkStepSound;
    [SerializeField] private AudioSource sneakStepSound;
    [SerializeField] private AudioSource dashSound;
    [SerializeField] private AudioSource hitSound;
    [SerializeField] private AudioSource jumpSound;
    #endregion

    #region SoundManagementAndSounds
    [SerializeField] private SoundManager soundManager;
    private bool wasGroundedLastFrame = false;
    #endregion

    #region SoundPositions
    private Vector3 lastPosition;
    #endregion

    #region StepSounds
    [SerializeField] private FootSounds footSoundsScript1;
    [SerializeField] private FootSounds footSoundsScript2;
    private float footStepTimer = 0f;
    [SerializeField] private float footStepTimerMax = 0.1f;
    // Edge-Detection der stepSound-Flags
    private bool _prevStep1;
    private bool _prevStep2;

    #endregion

    #region MovementModeReference
    //[Header("MovementModeReference")]
    private enum MovementMode
    {
        None,
        Idle,
        Sneak,
        SneakSprint,   // <— NEU
        Walk,
        Sprint,
        Slide,
        Air
    }

    private MovementMode movementMode = MovementMode.Idle;
    private MovementMode lastMovementMode;
    public bool movementIP = false;
    public float movementIPDuration = 0f;

    #endregion

    #region EquipmentReference
    [SerializeField] private Transform armCarrier;
    // 1) Einheitliche Referenz auf den "Arms-Space"
    private Transform armsSpace => armCarrier.transform.parent; // immer konsistent
    [SerializeField] private GameObject playerMachinePistolArms;
    [SerializeField] private GameObject playerRailgunArms;
    [SerializeField] private GameObject playerSwordArms;
    [SerializeField] private PlayerMachinePistolScript playerMachinePistolScript;
    [SerializeField] private PlayerRailGun playerRailGunScript;
    [SerializeField] private Inventory inventoryScript;

    private enum Equipment
    {
        None,
        Sword,
        MachinePistol,
        RailGun
    }
    private Equipment requestEquipment;
    private Equipment requestedEquipment;
    private Equipment activeEquipment;
    private Equipment previousActiveEquipment;

    public bool playerIsChangingEquipment = false;
    private bool playerCanChangeEquipment = true; // xcv
    private bool playerIsUnequipping = true;
    private bool playerIsEquipping = true;

    private bool cancelEquipmentChange = false;

    public bool playerFireIP = false;
    private bool playerIsStriking = false;
    
    #region MomentaryTransforms
    private Vector3 momentaryAimedLocalArmCarrierPosition = new Vector3(0f, 0f, 0f);
    private Quaternion momentaryAimedLocalArmCarrierRotation = Quaternion.Euler(0f, 0f, 0f);

    private Vector3 momentaryLoweredLocalArmCarrierPosition = new Vector3(0f, 0f, 0f);
    private Quaternion momentaryLoweredLocalArmCarrierRotation = Quaternion.Euler(0f, 0f, 0f);

    private Vector3 momentaryOffLocalArmCarrierPosition = new Vector3(0f, 0f, 0f);
    private Quaternion momentaryOffLocalArmCarrierRotation = Quaternion.Euler(0f, 0f, 0f);

    private Vector3 momentaryStrokeLocalArmCarrierPosition = new Vector3(0f, 0f, 0f);
    private Quaternion momentaryStrokeLocalArmCarrierRotation = Quaternion.Euler(0f, 0f, 0f);
    #endregion

    #region MachinePistolTransforms
    private Vector3 aimedLocalArmCarrierPositionMachinePistol = new Vector3(0f, 0f, 0f);
    private Quaternion aimedLocalArmCarrierRotationMachinePistol = Quaternion.Euler(0f, 0f, 0f);

    private Vector3 loweredLocalArmCarrierPositionMachinePistol = new Vector3(0.19f, -0.03f, 0.18f);
    private Quaternion loweredLocalArmCarrierRotationMachinePistol = Quaternion.Euler(0f, 0f, 0f);

    private Vector3 offLocalArmCarrierPositionMachinePistol = new Vector3(0f, -0.2f, 0f);
    private Quaternion offLocalArmCarrierRotationMachinePistol = Quaternion.Euler(60f, 0f, 0f);
    #endregion

    #region RailGunTransforms
    private Vector3 aimedLocalArmCarrierPositionRailGun = new Vector3(0f, 0f, 0f);
    private Quaternion aimedLocalArmCarrierRotationRailGun = Quaternion.Euler(0f, 0f, 0f);

    private Vector3 loweredLocalArmCarrierPositionRailGun = new Vector3(0.19f, -0.04f, 0.04f);
    private Quaternion loweredLocalArmCarrierRotationRailGun = Quaternion.Euler(0f, 0f, 0f);

    private Vector3 offLocalArmCarrierPositionRailGun = new Vector3(0f, -0.2f, 0f); 
    private Quaternion offLocalArmCarrierRotationRailGun = Quaternion.Euler(60f, 0f, 0f);
    #endregion

    #region SwordTransforms
    private Vector3 aimedLocalArmCarrierPositionSword = new Vector3(0f, 0f, 0.1f);
    private Quaternion aimedLocalArmCarrierRotationSword = Quaternion.Euler(-7.12f, 12.41f, 60.43f);

    private Vector3 loweredLocalArmCarrierPositionSword = new Vector3(-0.037f, -0.008458376f, -0.032f);
    private Quaternion loweredLocalArmCarrierRotationSword = Quaternion.Euler(-12.782f, 38.501f, -48.168f);

    private Vector3 offLocalArmCarrierPositionSword = new Vector3(0f, -0.2f, 0f);
    private Quaternion offLocalArmCarrierRotationSword = Quaternion.Euler(90f, 0f, 0f);

    private Vector3 strokeLocalArmCarrierPositionSword = new Vector3(-0.489f, -0.61f, 0.361f);
    private Quaternion stokeLocalArmCarrierRotationSword = Quaternion.Euler(35.109f, -149.062f, -107.5f);
    #endregion

    [SerializeField] private GameObject knifeHitbox;
    [SerializeField] private GameObject shieldObject;

    private float spreadValue = 0f; // The spread value that increases when not aiming
    private float maxSpread = 1f; // The spread value that increases when not aiming

    public bool shieldIsActive = false;

    private bool playerAimIP;
    private bool playerIsAimingForward;
    private bool playerArmsCloseToAimed = false;
    private bool playerArmsCloseToLowered = false;
    private bool playerArmsCloseToOff = false;
    private bool playerArmsCloseToStroke = false;
    private bool equipmentActionBlocksEqChange = false;
    #endregion

    #region GroundAndWallDetectionReference
    [Header("GroundAndWallDetection")]
    [SerializeField] private GroundDetection groundDetectionScript;
    private int groundLayerMask = (1 << 8);
    [SerializeField] private WallDetection wallDetectionScript;
    #endregion

    #region CameraControlReference
    [Header("CameraControl")]
    [SerializeField] private Transform cameraCarrierTransform; // Camera object that rotates up/down (pitch)
    [SerializeField] private float mouseSensitivity = 100f; // Sensitivity for mouse movement
    [SerializeField] private float maxLookAngle = 85f; // Limits vertical camera rotation to prevent flipping
    private float yRotation = 0f; // Tracks up/down rotation (pitch)
    private Vector2 mouseInput = Vector2.zero;

    private float chargeYOffset = 0f;
    public float totalManualYOffset => chargeYOffset;
    // —— Camera Y offset durch Charge ——
    [SerializeField] private float chargeYOffsetMax = -0.15f;   // Ziel-Y-Offset bei voller Charge (inspector)
    [SerializeField, Min(0f)] private float chargeYOffsetReturnTime = 0.10f; // Rückkehrdauer (inspector)

    private Coroutine chargeYOffsetReturnCo; // laufender Rückkehr-Lerp (falls aktiv)

    #endregion

    #region Walking-/MovementForcesAndFrictionReference
    [Header("WalkingMovementAndFriction")]
    public Vector3 localMovementDirection2DProjectedIP = new Vector3(0, 0, 0);
    [SerializeField] private float walkingForce;
    [SerializeField] private float standardWalkingForce;
    [SerializeField] private float sprintWalkingForce;
    [SerializeField] private float sneakWalkingForce;
    [SerializeField] private float quadraticFrictionFactor;
    [SerializeField] private float linearFrictionFactor;
    public float maxSpeedFromEquilibrium;
    [SerializeField] private float maxFrictionLimitFactor;

    #endregion

    #region HealthAndDamageReference
    [Header("Health")]
    [SerializeField] private Healthbar healthBar;
    [SerializeField] public float playerMaxHealth = 100f;
    [SerializeField] public float playerHealth = 100f;
    [SerializeField] public float playerMaxEnergy = 100f;
    [SerializeField] public float playerEnergy = 100f;
    [SerializeField] private float damagePerParticle = 1f;
    [SerializeField] private float damagePerRocket = 15f;

    #endregion

    #region JumpAndLandingReference
    [Header("Jump")]
    public bool playerGroundJumpIP = false;
    public bool playerTuneJumpIP = false;
    [SerializeField] public float jumpForce = 5f;
    [SerializeField] public float coyoteTimeDuration = 0.2f;
    [SerializeField] private float coyoteTimeCounter;

    [SerializeField] public float jumpBufferTimeDuration = 0.15f;
    private float jumpBufferCounter;

    [SerializeField] public float tuneJumpFactor;
    [SerializeField] public float tuneJumpTimeDuration = 0.2f;
    [SerializeField] private float tuneJumpTimeCounter;

    [SerializeField] private float maxAirSpeed = 5f; // Maximum speed while airborne
    [SerializeField] private float airControlForce = 5f; // How much force to apply for air strafing
    [SerializeField] private float airFrictionFactor = 0.1f; // Small air friction for balance

    [SerializeField] private float landingStumbleConeAngle = 45f; // Kegel-Halbwinkel
    [SerializeField] private float landingMinPlanarSpeed = 0.2f;  // ab welcher Speed wir werten

    // ===== Charge Jump Settings =====
    [Header("Charge Jump")]
    [SerializeField] private bool chargeJumpEnabled = true;
    [SerializeField] private float chargeJumpMinForce = 3f;
    [SerializeField] private float chargeJumpMaxForce = 12f;
    [SerializeField] private float chargeJumpTimeToMax = 0.5f; // Sekunden bis volle Ladung
    [SerializeField] private AnimationCurve chargeJumpCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    // Optional: darf Ladung fortgesetzt werden, wenn Boden verloren geht?
    [SerializeField] private bool chargePersistsOffGround = true;

    // Runtime
    private bool isChargingJump = false;
    private float chargeJumpStartTime = 0f;
    private bool jumpReleaseQueued = false;   // Loslassen wurde registriert
    private float pendingChargedForce = 0f;   // wartet auf Ausfuehrung (Buffer)
    public float chargeJumpProgress01 { get; private set; } // fuer UI

    [Header("Charge Jump Audio")]
    [SerializeField] private AudioSource chargeLoopSource;   // Loop-Clip, Play On Awake = false, Loop = true
    [SerializeField][Range(0f, 1f)] private float chargeVolumeStart = 0.15f;  // X
    [SerializeField][Range(0f, 1f)] private float chargeVolumeMax = 0.90f;  // Y

    [SerializeField][Range(0f, 1f)] private float releaseVolumeMin = 0.50f;  // min Jump-Volume bei kleiner Charge
    [SerializeField][Range(0f, 1f)] private float releaseVolumeMax = 1.00f;  // max Jump-Volume bei voller Charge

    // Runtime
    private float pendingReleaseVolume = 1f;

    #endregion

    #region SprintAndSneakReference
    [Header("SprintAndSneak")]

    private bool playerSprintIP = false;
    public bool playerSneakIP = false;
    public bool playerIsSliding { get; private set; }

    // Sprint-Toggle (Latch)
    [SerializeField] private bool sprintToggleEnabled = true; // optionaler Schalter im Inspector
    private bool _sprintToggled = false;

    // Toggle-Flag (persistiert über Frames; NICHT in Reset() zurücksetzen)
    private bool _sneakToggled = false;

    // Einheitliche Quelle für "Sneak ist aktiv" (Toggle ODER Taste-gedrückt)
    public bool playerSneakActive => _sneakToggled;
    [SerializeField] private float crouchSprintWalkingForce = 4.5f; // Tune im Inspector
                                                                    // Double-Tap Sprint
    [SerializeField] private float sprintDoubleTapMaxGap = 0.30f;       // Zeitfenster für den 2. Tap
    [SerializeField] private float sprintDoubleTapLatchDuration = 0.50f; // wie lange Sprint „gelatcht“ ist
    private float _sprintLastDownTime = -1f;
    private float _sprintLatchedUntil = 0f; // KEIN Tick nötig; Zeitstempel reicht

    private float maxSpeedFromEquilibriumSprint = 0f;

    private float sprintForwardConeDegrees = 46f; // wie bisher
    #endregion

    #region SlideReference
    // --- Slide: nur Animation beendet den State ---
    [SerializeField] private float slideMinSpeedFactor = 0.70f; // Start-Schwelle (Start darf gern per Logik)
    private bool _slideLock = false;            // hält den MovementMode im Slide
    private bool _slideExitRequested = false;   // wird NUR von der Animation gesetzt

    [Header("Slide Locomotion Force")]
    [SerializeField] private float slidePushForce = 20f; // Startstärke (Force-Modus)
    [SerializeField]
    private AnimationCurve slideForceCurve =
        AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);       // 0..1 -> 1..0 (blendet auf 0 aus)
    [SerializeField] private float slideFallbackDuration = 0.9f; // falls kein Anim-Progress verfügbar

    private Vector3 _slidePushDir = Vector3.zero;
    private float _slideForceT = 0f;

    [SerializeField] private float slideExitNormThreshold = 0.98f;   // Clip fast fertig
    [SerializeField] private float slideHardMaxDuration = 1.15f;     // Not-Aus
    private float _slideStartTime = 0f;

    #endregion

    #region Dash
    [Header("Dash")]
    [SerializeField] private float dashForceMagnitude;
    [SerializeField] private float dashingTimeDuration = 1f;
    [SerializeField] private float dashingCooldown;
    public bool playerCanDash = true;
    public bool playerIsDashing = false;
    private bool playerDashIP = false;
    private bool playerDashWalkBound = false;
    #endregion

    #region VaultReference
    [Header("Vault")]
    [SerializeField] private PlayerVaultDetectionWall playerVaultDetectionWallScript;
    [SerializeField] private PlayerVaultDetectionPlane playerVaultDetectionPlaneScript;
    [SerializeField] private float vaultTimeDuration = 1f;
    [SerializeField] private float vaultCooldown = 2f;
    [SerializeField] private bool playerCanVault = true;
    [SerializeField] private bool playerIsVaulting = false;
    [SerializeField] private bool playerVaultAvailable = false;
    [SerializeField] private bool playerVaultIP = false;
    [SerializeField] private Vector3 vaultVector = new Vector3(0, 2, 1); // Local vault direction
    [SerializeField] private AudioSource vaultSound;

    #endregion

    #region CrouchReference
    //[Header("Crouch")]
    [SerializeField] private float crouchSpeed;
    [SerializeField] private float crouchScale;
    [SerializeField] private float startYScale;

    #endregion

    #region EffectReference

    #endregion

    #region NavAreaReference
    public NavArea CurrentNavArea;
    #endregion
    #region UnityRuntimeMethods.............................................MethodsFromHereOn

    private void Awake()
    {
        footStepTimer = footStepTimerMax;

        //Application.targetFrameRate = 60;

        // Get the Rigidbody2D component
        playerRigidbody = GetComponent<Rigidbody>();
        Application.targetFrameRate = 144; // Adjust to a stable value (e.g., 144, 240)
        activeEquipment = Equipment.Sword;
        #region ArmRotationSetup
        momentaryAimedLocalArmCarrierRotation = aimedLocalArmCarrierRotationSword;
        momentaryAimedLocalArmCarrierPosition = aimedLocalArmCarrierPositionSword;

        momentaryLoweredLocalArmCarrierRotation = loweredLocalArmCarrierRotationSword;
        momentaryLoweredLocalArmCarrierPosition = loweredLocalArmCarrierPositionSword;

        momentaryOffLocalArmCarrierRotation = offLocalArmCarrierRotationSword;
        momentaryOffLocalArmCarrierPosition = offLocalArmCarrierPositionSword;

        momentaryStrokeLocalArmCarrierPosition = strokeLocalArmCarrierPositionSword;
        momentaryStrokeLocalArmCarrierRotation = stokeLocalArmCarrierRotationSword;
        #endregion
    }

    void Start()
    {
        LockCursor(); // Locks the cursor at start
        //healthBar.SetMaxHealth(playerHealth); // xcv
        CalculateEQSpeedSprint();
    }

    void Update()
    {
        mouseInput = GetMouseInput();
        HandleMouseLook();
        GetSprintInput();
        GetSneakInput();
        GetWalkingInput();
        VaultCheck();
        GetVaultInput();
        GetJumpInput();
        GetDashInput();
        GetFireInput();
        GetEquipmentManagementInput();
        GetEquipmentInput();
        UpdateArmRotation();
        MatchCameraCarrierHeight();
        SyncAnimationsPerFrame();

        if (playerHealth<=0f)
        {
            ReloadScene();
        }
    }

    void FixedUpdate()
    {
        ApplyEffects();
        UpdateSlideExitGate();
        ApplyWalkingAndFrictionAndSprint();
        ApplySlideLocomotionForce();
        CauseFootSteps();
        Jump();
        //Dash();
        Vault();
        AirStrafing();
        EquipmentRequest();
        Reset();
    }
    #endregion

    #region InputMethods
    public void ReloadScene()
    {
        StopChargeLoop(); // oder CancelCharge("Reload")
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void GetEquipmentManagementInput()
    {
        if (Input.GetButton("Eq0"))
        {
            requestEquipment = Equipment.None;
        }
        if (Input.GetButton("Eq1"))
        {
            requestEquipment = Equipment.Sword;
        }
        if (Input.GetButton("Eq2"))
        {
            requestEquipment = Equipment.MachinePistol;
        }
        if (Input.GetButton("Eq3"))
        {
            requestEquipment = Equipment.RailGun;
        }
        //if (Input.GetButton("Eq4"))
        //{
        //    activateEquipment = ActivateEquipment.;
        //}
        //if (Input.GetButton("Eq5"))
        //{
        //    activateEquipment = ActivateEquipment.;
        //}
        //if (Input.GetButton("Eq6"))
        //{
        //    activateEquipment = ActivateEquipment.;
        //}
        //}
    }

    private void GetEquipmentInput()
    {
        if (Input.GetButton("Fire2"))
        {
            playerAimIP = true;
        }
    }

    private void GetFireInput()
    {
        if (Input.GetButton("Fire1"))
            {
                playerFireIP = true;
            }
    }

    private Vector2 GetMouseInput()
    {
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.unscaledDeltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.unscaledDeltaTime;
        return new Vector2(mouseX, mouseY);
    }

    private void GetWalkingInput()
    {
        // Get input from the horizontal and vertical axes
        float xAxis = Input.GetAxis("X-Axis");
        float zAxis = Input.GetAxis("Z-Axis");

        Vector3 magVector = new Vector3(xAxis, 0, zAxis);

        float movementIPMagnitude = magVector.magnitude;

        if (movementIPMagnitude > 0.01f)
        {
            movementIP = true;
            movementIPDuration += Time.deltaTime;
        }
        else
        {
            movementIP = false;
            movementIPDuration = 0;
        }

        //UnityEngine.Debug.Log(movementIP);
        // Get the intended movement direction in local space
        localMovementDirection2DProjectedIP = magVector.normalized;

        // Convert the local movement direction to world space using the object's rotation
        intendedMovementDirection = transform.TransformDirection(localMovementDirection2DProjectedIP);
        // --- Sprint-Toggle sofort zurücksetzen wie beim Stehenbleiben ---
        bool sprintActive = _sprintToggled || (Time.time < _sprintLatchedUntil);

        if (sprintActive && groundDetectionScript.isGrounded && !playerIsSliding && !_slideLock)
        {
            if (!movementIP)
            {
                CancelSprintToggle("StoppedMoving");
            }
            else
            {
                // Richtung nur über den Input (lokal) prüfen: Dot >= cos(theta) <=> Winkel <= theta
                Vector3 moveLocal = magVector;
                float mag = moveLocal.magnitude;
                if (mag > 0.0001f)
                {
                    float cos = Vector3.Dot(moveLocal / mag, Vector3.forward); // Vorwärts-Ausrichtung in LOCAL space
                    float cosThreshold = Mathf.Cos(sprintForwardConeDegrees * Mathf.Deg2Rad); // ~0.694 bei 46°
                    if (cos < cosThreshold)
                        CancelSprintToggle("InvalidDirection");
                }
                else
                {
                    // Falls extrem kleiner Stick-Input – sicherheitshalber auch aus
                    CancelSprintToggle("TinyInput");
                }
            }
        }

        // Auto-Unsprint beim Stehenbleiben
        if ((_sprintToggled || Time.time < _sprintLatchedUntil)   // Sprint aktiv (Toggle oder Latch)
             && !movementIP                                        // kein Bewegungs-Input
             //&& groundDetectionScript.isGrounded                   // am Boden
             && !playerIsSliding && !_slideLock)                  // nicht im Slide
        {
            CancelSprintToggle("StoppedMoving");
        }
    }

    private void GetVaultInput()
    {
        if (Input.GetButton("Vault"))
        {
            playerVaultIP = true;
        }
    }

    private void GetJumpInput()
    {
        if (chargeJumpEnabled)
        {
            // Start: Taste gedrueckt -> Beginn der Ladung (egal ob gerade am Boden,
            // die Ausfuehrung pruefen wir beim Loslassen ueber Coyote/Buffer)
            if (Input.GetButtonDown("Jump"))
            {
                isChargingJump = true;
                chargeJumpStartTime = Time.time;
                chargeJumpProgress01 = 0f;

                CancelChargeYOffsetReturn();

                // AUDIO: Start des Charging-Loops
                StartChargeLoop();
            }

            // Laden
            if (isChargingJump)
            {
                // Falls Boden verloren und das nicht erlaubt ist, Ladung abbrechen
                if (!chargePersistsOffGround && !groundDetectionScript.isGrounded)
                {
                    isChargingJump = false;
                    chargeJumpProgress01 = 0f;

                    StopChargeLoop();
                    StartChargeYOffsetReturn();
                }
                else
                {
                    float t = Mathf.Clamp01((Time.time - chargeJumpStartTime) / chargeJumpTimeToMax);
                    float shaped = chargeJumpCurve.Evaluate(t);
                    chargeJumpProgress01 = t;

                    // AUDIO: Volume entlang der Charge-Kurve
                    if (chargeLoopSource != null && chargeLoopSource.isPlaying)
                    {
                        float v = Mathf.Lerp(chargeVolumeStart, chargeVolumeMax, shaped);
                        chargeLoopSource.volume = v; // bleibt bei VolY, wenn voll geladen
                    }

                    // ——— Y-OFFSET nach Charge-Intensität ———
                    // direkt auf Ziel setzen (die vertikale Kamerabewegung selbst wird bei dir via SmoothDamp geglättet)
                    chargeYOffset = Mathf.Lerp(0f, chargeYOffsetMax, shaped);
                }
            }

            // Loslassen: Kraft berechnen und als "queued" vormerken (Buffer nutzt deine vorhandenen Timer)
            if (Input.GetButtonUp("Jump"))
            {
                if (isChargingJump)
                {
                    float t = Mathf.Clamp01((Time.time - chargeJumpStartTime) / chargeJumpTimeToMax);
                    float shaped = chargeJumpCurve.Evaluate(t);

                    pendingChargedForce = Mathf.Lerp(chargeJumpMinForce, chargeJumpMaxForce, shaped);
                    pendingReleaseVolume = Mathf.Lerp(releaseVolumeMin, releaseVolumeMax, shaped);

                    jumpReleaseQueued = true;
                    isChargingJump = false;
                    // Nutze deinen vorhandenen Jump-Buffer
                    jumpBufferCounter = jumpBufferTimeDuration;
                    // AUDIO: Loop beenden (bis Release gehalten, jetzt losgelassen)
                    StopChargeLoop();

                    StartChargeYOffsetReturn();
                }
            }

            // In Charge-Mode keine "TuneJump" Halte-Logik
            playerGroundJumpIP = false;
            playerTuneJumpIP = false;
        }
        else
        {
            // Dein bisheriger Code (Kurzform beibehalten)
            if (Input.GetButtonDown("Jump") && groundDetectionScript.isGrounded)
            {
                playerGroundJumpIP = true;
                CancelSprintToggle("Jump");
            }
            else if (Input.GetButton("Jump") && !groundDetectionScript.isGrounded)
            {
                playerTuneJumpIP = true;
            }
        }
    }



    private void GetDashInput()
    {
        if (Input.GetButtonDown("Dash"))
        {
            playerDashIP = true;
        }
    }

    private void GetSprintInput()
    {
        if (Input.GetButtonDown("Sprint"))
        {
            float now = Time.time;
            bool isDoubleTap = (now - _sprintLastDownTime) <= sprintDoubleTapMaxGap;
            _sprintLastDownTime = now;

            if (!playerIsSliding)
            {
                if (isDoubleTap)
                {
                    _sprintToggled = true;
                    _sprintLatchedUntil = now + sprintDoubleTapLatchDuration;
                    CancelCrouchIfAllowed("SprintDoubleTap");
                }
                else if (sprintToggleEnabled)
                {
                    _sprintToggled = !_sprintToggled;
                }
            }
        }

        // KEIN Hold mehr
        playerSprintIP = _sprintToggled || (Time.time < _sprintLatchedUntil);
    }



    private void GetSneakInput()
    {
        // Während Slide: keine Toggle-Kanten akzeptieren
        if (playerIsSliding || _slideLock)
        {
            playerSneakIP = Input.GetButton("Sneak"); // Haltesignal darf gelesen werden, toggelt aber nichts
            return;
        }

        if (Input.GetButtonDown("Sneak"))
        {
            _sneakToggled = !_sneakToggled;
            CancelSprintToggle("SneakToggle");
        }
        playerSneakIP = Input.GetButton("Sneak");
    }


    #endregion

    #region EffectMethods
    private void ApplyEffects()
    {
        return;
    }

    public void ReceiveDamage()
    {
        //UnityEngine.Debug.Log($"Damage received! Current health: {playerHealth}"); // UnityEngine.Debugging
        hitSound.Play();
        playerHealth -= damagePerParticle;
        playerHealth = Mathf.Clamp(playerHealth, 0, playerMaxHealth);
        //healthBar.SetHealth(playerHealth);
        //UnityEngine.Debug.Log($"New health: {playerHealth}"); // UnityEngine.Debugging
    }
    public void ReceiveRocketDamage()
    {
        //UnityEngine.Debug.Log($"RocketDamage received! Current health: {playerHealth}"); // UnityEngine.Debugging
        hitSound.Play();
        playerHealth -= damagePerRocket;
        playerHealth = Mathf.Clamp(playerHealth, 0, playerMaxHealth);
        healthBar.SetHealth(playerHealth);
        //UnityEngine.Debug.Log($"New health: {playerHealth}"); // UnityEngine.Debugging
    }
    #endregion

    #region EquipmentMethods

    private void CheckEquipmentState()
    {
        if(playerMachinePistolScript.isReloading || playerMachinePistolScript.isFiring || playerRailGunScript.isReloading || playerRailGunScript.isFiring || playerIsStriking || shieldIsActive)
        {
            equipmentActionBlocksEqChange = true;
        }
        else
        {
            equipmentActionBlocksEqChange = false;
        }
    }

    private void EquipmentRequest()
    {
        CheckEquipmentState();

        if(requestEquipment == Equipment.None || !playerCanChangeEquipment || requestEquipment == activeEquipment || playerIsChangingEquipment || equipmentActionBlocksEqChange)
        {
            return ;
        }
        else
        {
            switch (requestEquipment)
            {
                case Equipment.None:
                    return;

                case Equipment.Sword:
                    requestedEquipment = Equipment.Sword;
                    playerIsChangingEquipment = true;
                    StartCoroutine(EquipmentChange());
                    return;

                case Equipment.MachinePistol:
                    requestedEquipment = Equipment.MachinePistol;
                    playerIsChangingEquipment = true;
                    StartCoroutine(EquipmentChange());
                    break;

                case Equipment.RailGun:
                    requestedEquipment = Equipment.RailGun;
                    playerIsChangingEquipment = true;
                    StartCoroutine(EquipmentChange());
                    break;
            }
        }
    }

    private void UpdatePreviousEquipment()
    {
        if (activeEquipment != previousActiveEquipment) // Only run when state changes
        {
            previousActiveEquipment = activeEquipment; // Update previous state tracker
        }
    }

    private void OnEquipmentChange()
    {
        switch (requestedEquipment)
        {
            case Equipment.None:
                return;

            case Equipment.Sword:
                activeEquipment = Equipment.Sword;
                playerSwordArms.SetActive(true);
                playerMachinePistolArms.SetActive(false);
                playerRailgunArms.SetActive(false);
                //UnityEngine.Debug.Log(activeEquipment);

                momentaryAimedLocalArmCarrierRotation = aimedLocalArmCarrierRotationSword;
                momentaryAimedLocalArmCarrierPosition = aimedLocalArmCarrierPositionSword;

                momentaryLoweredLocalArmCarrierRotation = loweredLocalArmCarrierRotationSword;
                momentaryLoweredLocalArmCarrierPosition = loweredLocalArmCarrierPositionSword;

                momentaryOffLocalArmCarrierRotation = offLocalArmCarrierRotationSword;
                momentaryOffLocalArmCarrierPosition = offLocalArmCarrierPositionSword;

                momentaryStrokeLocalArmCarrierRotation = stokeLocalArmCarrierRotationSword;
                momentaryStrokeLocalArmCarrierPosition = strokeLocalArmCarrierPositionSword;
                requestedEquipment = Equipment.None;
                break;

            case Equipment.MachinePistol:                    
                activeEquipment = Equipment.MachinePistol;
                playerSwordArms.SetActive(false);
                playerMachinePistolArms.SetActive(true);
                playerRailgunArms.SetActive(false);
                //UnityEngine.Debug.Log(activeEquipment);

                momentaryAimedLocalArmCarrierRotation = aimedLocalArmCarrierRotationMachinePistol;
                momentaryAimedLocalArmCarrierPosition = aimedLocalArmCarrierPositionMachinePistol;

                momentaryLoweredLocalArmCarrierRotation = loweredLocalArmCarrierRotationMachinePistol;
                momentaryLoweredLocalArmCarrierPosition = loweredLocalArmCarrierPositionMachinePistol;

                momentaryOffLocalArmCarrierRotation = offLocalArmCarrierRotationMachinePistol;
                momentaryOffLocalArmCarrierPosition = offLocalArmCarrierPositionMachinePistol;
                requestedEquipment = Equipment.None;
                break;

            case Equipment.RailGun:            
                activeEquipment = Equipment.RailGun;
                playerSwordArms.SetActive(false);
                playerMachinePistolArms.SetActive(false);
                playerRailgunArms.SetActive(true);
                //UnityEngine.Debug.Log(activeEquipment);
                momentaryAimedLocalArmCarrierRotation = aimedLocalArmCarrierRotationRailGun;
                momentaryAimedLocalArmCarrierPosition = aimedLocalArmCarrierPositionRailGun;

                momentaryLoweredLocalArmCarrierRotation = loweredLocalArmCarrierRotationRailGun;
                momentaryLoweredLocalArmCarrierPosition = loweredLocalArmCarrierPositionRailGun;

                momentaryOffLocalArmCarrierRotation = offLocalArmCarrierRotationRailGun;
                momentaryOffLocalArmCarrierPosition = offLocalArmCarrierPositionRailGun;
                requestedEquipment = Equipment.None;
                break;
        }
    }

    private IEnumerator EquipmentChange()
    {
        cancelEquipmentChange = false;
        playerIsChangingEquipment = true;
        playerIsUnequipping = true;
        playerIsEquipping = false;

        try
        {
            // Unequipping phase
            while (playerIsUnequipping)
            {
                if (cancelEquipmentChange)
                {
                    //UnityEngine.Debug.Log("Cancelled during unequipping.");
                    yield break;
                }
                RotatorArmsToOffPosition();
                if (CheckIfArmsCloseToOff())
                {
                    playerIsUnequipping = false;
                    OnEquipmentChange();
                    armCarrier.transform.localRotation = momentaryOffLocalArmCarrierRotation;
                    armCarrier.transform.localPosition = momentaryOffLocalArmCarrierPosition;
                    playerIsEquipping = true;
                }
                yield return null;
            }

            // Equipping phase
            while (playerIsEquipping)
            {
                if (cancelEquipmentChange)
                {
                    //UnityEngine.Debug.Log("Cancelled during equipping.");
                    yield break;
                }
                RotatorArmsToLoweredPositionEquipping();
                if (CheckIfArmsCloseToLowered())
                {
                    playerIsEquipping = false;
                }
                yield return null;
            }
        }
        finally
        {
            // Ensure all flags are reset no matter how we exit
            playerIsChangingEquipment = false;
            playerIsUnequipping = false;
            playerIsEquipping = false;
            cancelEquipmentChange = false;
            //UnityEngine.Debug.Log("Equipment change finished or cancelled; flags reset.");
        }
    }

    public void CancelEquipmentChange()
    {
        cancelEquipmentChange = true;
        //UnityEngine.Debug.Log("CancelEquipmentChange() called.");
    }

    private void UpdateSpreadBasedOnArmsPosition() // xcv NICHT IN NUTZUNG UND NÌCHT SICHER!!!
    {
        // Get current local position of the arms
        Vector3 currentLocalPosition = armCarrier.transform.localPosition;

        // Calculate the lerp factor (0 when aimed, 1 when lowered)
        float lerpFactor = Mathf.InverseLerp(
            momentaryAimedLocalArmCarrierPosition.magnitude,
            momentaryLoweredLocalArmCarrierPosition.magnitude,
            currentLocalPosition.magnitude
        );

        // Increase spread when closer to the lowered position
        spreadValue = Mathf.Lerp(0f, maxSpread, lerpFactor);

        // UnityEngine.Debugging
        //UnityEngine.Debug.Log($"Lerp Factor: {lerpFactor} | Spread Value: {spreadValue}");
    }

    public bool CheckIfArmsCloseToAimed()
    {
        // Get the local forward direction of the arms relative to the camera carrier
        Vector3 armsForwardLocal = armsSpace.InverseTransformDirection(armCarrier.transform.forward);

        // Convert the aimed local rotation into a forward vector
        Vector3 localForwardReference = momentaryAimedLocalArmCarrierRotation * Vector3.forward;

        // Normalize both vectors
        armsForwardLocal.Normalize();
        localForwardReference.Normalize();

        // Calculate the angle between the arms' forward direction and the local forward reference
        float angle = Vector3.Angle(armsForwardLocal, localForwardReference);

        bool playerArmsCloseToAimedAngle = false;
        bool playerArmsCloseToAimedPosition = false;

        // Check if the arms are close to the aimed position (small angle difference)
        if (angle <= 3f)
        {
            playerArmsCloseToAimedAngle = true;
        }
        else
        {
            playerArmsCloseToAimedAngle = false;
        }

        // --- Added Position Check ---
        // Convert the arms' world position into the camera carrier's local space.
        Vector3 armsLocalPosition = armsSpace.InverseTransformPoint(armCarrier.transform.position);

        // Compare the arms' local position to the aimed local position
        // (aimedLocalArmCarrierPositionMachinePistol should be defined elsewhere in your class)
        float positionDifference = Vector3.Distance(armsLocalPosition, momentaryAimedLocalArmCarrierPosition);

        if (positionDifference <= 0.1f)
        {
            playerArmsCloseToAimedPosition = true;
        }
        else
        {
            playerArmsCloseToAimedPosition = false;
        }

        if (playerArmsCloseToAimedAngle && playerArmsCloseToAimedPosition)
        {
            playerArmsCloseToAimed = true;
        }
        else
        {
            playerArmsCloseToAimed = false;
        }
        //UnityEngine.Debug.Log(playerArmsCloseToAimed);
        return playerArmsCloseToAimed;
    }

    private bool CheckIfArmsCloseToOff()
    {
        // Get the local forward direction of the arms relative to the camera carrier
        Vector3 armsForwardLocal = armsSpace.InverseTransformDirection(armCarrier.transform.forward);

        // Convert the momentary off local rotation into a forward vector
        Vector3 localForwardReference = momentaryOffLocalArmCarrierRotation * Vector3.forward;

        // Normalize both vectors
        armsForwardLocal.Normalize();
        localForwardReference.Normalize();

        // Calculate the angle between the arms' forward direction and the local forward reference
        float angle = Vector3.Angle(armsForwardLocal, localForwardReference);

        bool armsCloseToOffAngle = angle <= 3f;

        // --- Position Check ---
        // Convert the arms' world position into the camera carrier's local space.
        Vector3 armsLocalPosition = armsSpace.InverseTransformPoint(armCarrier.transform.position);

        // Compare the arms' local position to the momentary off local position
        float positionDifference = Vector3.Distance(armsLocalPosition, momentaryOffLocalArmCarrierPosition);
        bool armsCloseToOffPosition = positionDifference <= 0.2f;

        playerArmsCloseToOff = armsCloseToOffAngle && armsCloseToOffPosition;
        //UnityEngine.Debug.Log("Arms close to Off: " + playerArmsCloseToOff);
        return playerArmsCloseToOff;
    }

    public bool CheckIfArmsCloseToLowered()
    {
        // Get the local forward direction of the arms relative to the camera carrier
        Vector3 armsForwardLocal = armsSpace.InverseTransformDirection(armCarrier.transform.forward);

        // Convert the momentary lowered local rotation into a forward vector
        Vector3 localForwardReference = momentaryLoweredLocalArmCarrierRotation * Vector3.forward;

        // Normalize both vectors
        armsForwardLocal.Normalize();
        localForwardReference.Normalize();

        // Calculate the angle between the arms' forward direction and the local forward reference
        float angle = Vector3.Angle(armsForwardLocal, localForwardReference);

        bool armsCloseToLoweredAngle = angle <= 1f;

        // --- Position Check ---
        // Convert the arms' world position into the camera carrier's local space.
        Vector3 armsLocalPosition = armsSpace.InverseTransformPoint(armCarrier.transform.position);

        // Compare the arms' local position to the momentary lowered local position
        float positionDifference = Vector3.Distance(armsLocalPosition, momentaryLoweredLocalArmCarrierPosition);
        bool armsCloseToLoweredPosition = positionDifference <= 0.075f;

        playerArmsCloseToLowered = armsCloseToLoweredAngle && armsCloseToLoweredPosition;
        //UnityEngine.Debug.Log("Arms close to Lowered: " + playerArmsCloseToLowered);
        return playerArmsCloseToLowered;
    }

    private bool CheckIfArmsCloseToStroke()
    {
        // Get the local forward direction of the arms relative to the camera carrier
        Vector3 armsForwardLocal = armsSpace.InverseTransformDirection(armCarrier.transform.forward);

        // Convert the momentary lowered local rotation into a forward vector
        Vector3 localForwardReference = momentaryStrokeLocalArmCarrierRotation * Vector3.forward;

        // Normalize both vectors
        armsForwardLocal.Normalize();
        localForwardReference.Normalize();

        // Calculate the angle between the arms' forward direction and the local forward reference
        float angle = Vector3.Angle(armsForwardLocal, localForwardReference);

        bool armsCloseToLoweredAngle = angle <= 1f;

        // --- Position Check ---
        // Convert the arms' world position into the camera carrier's local space.
        Vector3 armsLocalPosition = armsSpace.InverseTransformPoint(armCarrier.transform.position);

        // Compare the arms' local position to the momentary lowered local position
        float positionDifference = Vector3.Distance(armsLocalPosition, momentaryStrokeLocalArmCarrierPosition);
        bool armsCloseToLoweredPosition = positionDifference <= 0.05f;

        playerArmsCloseToStroke = armsCloseToLoweredAngle && armsCloseToLoweredPosition;
        //UnityEngine.Debug.Log("Arms close to Lowered: " + playerArmsCloseToLowered);
        return playerArmsCloseToStroke;
    }

    private void UpdateArmRotation()
    {
        if (playerIsChangingEquipment)
        {
            return;
        }
        else if (activeEquipment != Equipment.Sword)
        {
            if (playerAimIP && !playerMachinePistolScript.isReloading)
            {
                RotatorArmsToAimPosition();
            }
            else
            {
                RotatorArmsToLoweredPosition();
            }
        }
        else if (activeEquipment == Equipment.Sword)
        {
            UseSword();
        }
    }

    private void UseSword()
    {
        if (CheckIfArmsCloseToLowered() && playerFireIP && !playerIsStriking)
        {
            swordStrikeSound.Play();
            playerIsStriking = true;
            knifeHitbox.SetActive(true);
            //UnityEngine.Debug.Log("Strike started!");
        }
        else if (playerIsStriking)
        {
            RotatorArmsToStrokePosition();
            //UnityEngine.Debug.Log("Strike started!");

            if (CheckIfArmsCloseToStroke())
            {
                playerIsStriking = false;
                knifeHitbox.SetActive(false);
                //UnityEngine.Debug.Log("Strike finished! Resetting.");
            }
        }
        else if (playerIsStriking)
        {
            return;  // Prevent other rotations while striking
        }
        else if (playerAimIP && groundDetectionScript.isGrounded)
        {
            RotatorArmsToAimPosition();
            if (CheckIfArmsCloseToAimed() && inventoryScript.totalShieldCharge > 0)
            {
                shieldObject.SetActive(true);
                shieldIsActive = true;
            }
            else
            {
                shieldObject.SetActive(false);
                shieldIsActive = false;
            }
        }
        else
        {
            RotatorArmsToLoweredPosition();
            shieldObject.SetActive(false);
            shieldIsActive = false;
        }
    }

    private void RotatorArmsToAimPosition()
    {
        // Define target rotation
        Quaternion targetLocalRotation = momentaryAimedLocalArmCarrierRotation;
        Vector3 targetLocalPosition = momentaryAimedLocalArmCarrierPosition; 

        // Apply smooth rotation in local space
        armCarrier.transform.localRotation = Quaternion.Slerp(
            armCarrier.transform.localRotation,
            targetLocalRotation,
            Time.deltaTime * 15f
        );

        // Apply smooth position transition
        armCarrier.transform.localPosition = Vector3.Lerp(
            armCarrier.transform.localPosition,
            targetLocalPosition,
            Time.deltaTime * 15f
        );
    }

    private void RotatorArmsToLoweredPosition()
    {
        // Define target rotation
        Quaternion targetLocalRotation = momentaryLoweredLocalArmCarrierRotation;
        Vector3 targetLocalPosition = momentaryLoweredLocalArmCarrierPosition; 

        // Apply smooth rotation in local space
        armCarrier.transform.localRotation = Quaternion.Slerp(
            armCarrier.transform.localRotation,
            targetLocalRotation,
            Time.deltaTime * 15f
        );

        // Apply smooth position transition
        armCarrier.transform.localPosition = Vector3.Lerp(
            armCarrier.transform.localPosition,
            targetLocalPosition,
            Time.deltaTime * 15f
        );
    }

    private void RotatorArmsToStrokePosition()
    {
        // Define target rotation
        Quaternion targetLocalRotation = momentaryStrokeLocalArmCarrierRotation;
        Vector3 targetLocalPosition = momentaryStrokeLocalArmCarrierPosition;

        // Apply smooth rotation in local space
        armCarrier.transform.localRotation = Quaternion.Slerp(
            armCarrier.transform.localRotation,
            targetLocalRotation,
            Time.deltaTime * 25f
        );

        // Apply smooth position transition
        armCarrier.transform.localPosition = Vector3.Lerp(
            armCarrier.transform.localPosition,
            targetLocalPosition,
            Time.deltaTime * 25f
        );
    }

    private void RotatorArmsToLoweredPositionEquipping()
    {
        // Define target rotation
        Quaternion targetLocalRotation = momentaryLoweredLocalArmCarrierRotation;
        Vector3 targetLocalPosition = momentaryLoweredLocalArmCarrierPosition;

        // Apply smooth rotation in local space
        armCarrier.transform.localRotation = Quaternion.Slerp(
            armCarrier.transform.localRotation,
            targetLocalRotation,
            Time.fixedDeltaTime * 4f
        );

        // Apply smooth position transition
        armCarrier.transform.localPosition = Vector3.Lerp(
            armCarrier.transform.localPosition,
            targetLocalPosition,
            Time.fixedDeltaTime * 4f
        );
    }

    private void RotatorArmsToOffPosition()
    {
        // Define target rotation and position
        Quaternion targetLocalRotation = momentaryOffLocalArmCarrierRotation;
        Vector3 targetLocalPosition = momentaryOffLocalArmCarrierPosition; 

        // Apply smooth rotation in local space
        armCarrier.transform.localRotation = Quaternion.Slerp(
            armCarrier.transform.localRotation,
            targetLocalRotation,
            Time.fixedDeltaTime * 4f
        );

        // Apply smooth position transition
        armCarrier.transform.localPosition = Vector3.Lerp(
            armCarrier.transform.localPosition,
            targetLocalPosition,
            Time.fixedDeltaTime * 4f
        );
    }

    #endregion

    #region CameraControlmethods

    private void HandleMouseLook()
    {
        RotatePlayerYaw(mouseInput.x);
        RotateCameraPitch(mouseInput.y);
    }
    private void RotatePlayerYaw(float mouseX)
    {
        Quaternion deltaRotation = Quaternion.Euler(0f, mouseX, 0f);
        playerRigidbody.MoveRotation(playerRigidbody.rotation * deltaRotation);
    }
    private void RotateCameraPitch(float mouseY)
    {
        yRotation -= mouseY;
        yRotation = Mathf.Clamp(yRotation, -maxLookAngle, maxLookAngle);
        cameraCarrierTransform.localRotation = Quaternion.Euler(yRotation, 0f, 0f);
    }

    #endregion

    #region GunMethods
    public void ApplySMGRecoil()
    {

            // Increase the pitch (upward tilt) by 1 degree per shot and Clamp it within the allowed range
            yRotation -= playerMachinePistolScript.verticalRecoil;
            yRotation = Mathf.Clamp(yRotation, -maxLookAngle, maxLookAngle);

            // Apply horizontal recoil (yaw) with slight randomness
            float horizontalRecoil = Random.Range(-playerMachinePistolScript.horizontalRecoil, playerMachinePistolScript.horizontalRecoil);

            // Apply the rotation to the camera carrier object
            cameraCarrierTransform.localRotation = Quaternion.Euler(yRotation, 0f, 0f);
            playerRigidbody.MoveRotation(playerRigidbody.rotation * Quaternion.Euler(0f, horizontalRecoil, 0f));
            PlayerHighSound(); // xcv
    }

    public void ApplyRailGunRecoil()
    {
        // Increase the pitch (upward tilt) by 1 degree per shot and Clamp it within the allowed range
        yRotation -= playerRailGunScript.verticalRecoil;
        yRotation = Mathf.Clamp(yRotation, -maxLookAngle, maxLookAngle);

        // Apply horizontal recoil (yaw) with slight randomness
        float horizontalRecoil = Random.Range(-playerRailGunScript.horizontalRecoil, playerRailGunScript.horizontalRecoil);

        // Apply the rotation to the camera carrier object
        cameraCarrierTransform.localRotation = Quaternion.Euler(yRotation, 0f, 0f);
        playerRigidbody.MoveRotation(playerRigidbody.rotation * Quaternion.Euler(0f, horizontalRecoil, 0f));
        PlayerVeryHighSound(); // xcv
    }

    #endregion 

    #region LocomotionMethods
    private void ApplyWalkingAndFrictionAndSprint()
    {         
        MovementModeManager();
        UpdateMovementMode();
        //UpdateEquilibriumSpeed();

        if (groundDetectionScript.isGrounded && !playerIsSliding)
        {
            WalkingForce();
            FrictionForce();
        }
    }

    private void UpdateEquilibriumSpeed()
    {
        maxSpeedFromEquilibrium = Mathf.Sqrt(walkingForce / quadraticFrictionFactor);
    }
    private void CalculateEQSpeedSprint()
    {
        maxSpeedFromEquilibriumSprint = Mathf.Sqrt(sprintWalkingForce / quadraticFrictionFactor);
    }

    #region MovementMode StateMachine (Exit -> Enter)´
    private void MovementModeManager()
    {
        // Calculate the angle between the player's forward direction and intended movement direction
        float angle = Vector3.Angle(Vector3.forward, localMovementDirection2DProjectedIP);
        bool angleSmallerEqual46 = angle <= 46f;

        bool sprintPressedOrLatched = _sprintToggled || (Time.time < _sprintLatchedUntil);

        lastMovementMode = movementMode;

        switch ((lastMovementMode, groundDetectionScript.isGrounded, movementIP, _sneakToggled, sprintPressedOrLatched, angleSmallerEqual46))
        {
            case (_, _, _, _, _, _) when _slideLock && !_slideExitRequested:
                movementMode = MovementMode.Slide;
                break;

            // 2) Slide verlassen NUR wenn Animation das Ende signalisiert
            case (MovementMode.Slide, _, _, _, _, _) when _slideLock && _slideExitRequested:
                movementMode = MovementMode.SneakSprint; // gewünschtes Ziel nach Slide
                break;

            // 3) Slide starten (nur per Logik – Ende kommt NICHT aus Logik)
            case (MovementMode.Sprint, true, _, _, _, _) when !_slideLock && CanStartSlide():
                movementMode = MovementMode.Slide;
                break;


            case (_, true, true, true, true, _):   // <— NEU: Crouch + Sprint + MovementIP
                movementMode = MovementMode.SneakSprint;
                break;

            case (_, true, false, false, false, _):
                movementMode = MovementMode.Idle;
                break;

            case (_, true, true, false, false, _):
                movementMode = MovementMode.Walk;
                break;

            case (_, true, true, false, true, false):
                movementMode = MovementMode.Walk;
                break;

            case (_, true, _, true, _, _):
                movementMode = MovementMode.Sneak;
                break;

            case (_, true, true, false, true, true):
                movementMode = MovementMode.Sprint;
                break;

            case (_, false, _, _, _, _):
                movementMode = MovementMode.Air;
                break;

            default:
                movementMode = MovementMode.None;
                break;
        }


        // if(lastMovementMode!=movementMode) // only UnityEngine.Debug code
        // {
        //     UnityEngine.Debug.Log("MM Changed!");
        // }
        // 
        //UnityEngine.Debug.Log(movementMode);

    }

    private void UpdateMovementMode()
    {
        if (movementMode == lastMovementMode)
            return;

        // 1) Exit vom alten State (mit Kenntnis des Ziel-States)
        OnMovementModeExit(lastMovementMode, movementMode);

        // 2) Enter in den neuen State (deine vorhandene Methode)
        OnMovementModeEnter(movementMode);

        // 3) Abschluss
        lastMovementMode = movementMode;
    }

    private void OnMovementModeEnter(MovementMode movementMode)
    {
        // Wenn wir gerade aus der Luft auf einen Boden-Mode wechseln, entscheide den Landing-Style
        if (lastMovementMode == MovementMode.Air && groundDetectionScript.isGrounded)
        {
            DecideLandingStyleForThisLanding(); 
        }

        switch (movementMode)
        {
            case MovementMode.None:
                return;

            case MovementMode.Idle:
                ApplyIdleValues();
                if (sPAC) sPAC.ApplyMovementMode(SamplePlayerAnimationController.PCMovementMode.Idle);
                break;

            case MovementMode.Walk:
                ApplyWalkValues();
                if (sPAC) sPAC.ApplyMovementMode(SamplePlayerAnimationController.PCMovementMode.Walk);
                break;

            case MovementMode.Sneak:
                ApplySneakValues();
                if (sPAC) sPAC.ApplyMovementMode(SamplePlayerAnimationController.PCMovementMode.Sneak);
                break;

            case MovementMode.SneakSprint:
                ApplySneakSprintValues();
                if (sPAC) sPAC.ApplyMovementMode(SamplePlayerAnimationController.PCMovementMode.Sneak);
                break;

            case MovementMode.Sprint:
                ApplySprintValues();
                if (sPAC) sPAC.ApplyMovementMode(SamplePlayerAnimationController.PCMovementMode.Sprint);
                break;

            case MovementMode.Slide:
                ApplySlideValues();
                BeginSlide();
                break;

            case MovementMode.Air:
                ApplyAirValues();
                if (sPAC) sPAC.ApplyMovementMode(SamplePlayerAnimationController.PCMovementMode.Air);
                break;
        }
    }

    private void OnMovementModeExit(MovementMode from, MovementMode to)
    {
        switch (from)
        {
            case MovementMode.None:
                // Nichts zu tun.
                break;

            case MovementMode.Idle:
                // Typischerweise kein Cleanup nötig.
                // Falls du Idle-spezifische Effekte/FOV gesetzt hast, hier zurücksetzen.
                break;

            case MovementMode.Walk:
                // Walk-spezifische OneShots laufen eh aus.
                // Falls du Loops oder Trails für Walk nutzt: hier stoppen.
                // Beispiel:
                // if (walkStepSound.isPlaying) walkStepSound.Stop();
                break;

            case MovementMode.Sneak:
                // Sneak-spezifische Visuals/SFX hier deaktivieren (keine Toggles ändern).
                // Beispiel:
                // if (sneakStepSound.isPlaying) sneakStepSound.Stop();
                // Schild/Arme bleiben von deiner Waffenlogik gesteuert – hier nichts toggeln.
                break;

            case MovementMode.SneakSprint:
                // kein spezielles Cleanup nötig
                break;

            case MovementMode.Sprint:
                // Sprint-Exits: FOV-Tricks, Trails, Partikel, Stamina-Drain beenden.
                // Beispiel:
                // if (sprintStepSound.isPlaying) sprintStepSound.Stop();
                // Keine Kraftwerte zurücksetzen – das macht der Enter des Ziel-States.
                break;

            case MovementMode.Slide:
                // Sliding-Flag im Animator sauber beenden – crouch NICHT zwanghaft lösen.
                // Achtung: Nur Sliding aus, Crouch bleibt ggf. für 'to == Sneak' gewünscht.
                //try
                //{
                //    sPAC?.DeactivateSliding();   // public im Sample-Script //xcv1
                //}
                //catch { /* sicherheitshalber schlucken */ }
                //
                //playerIsSliding = false;         // dein lokales Flag
                // Wenn du während Slide Physik/Materialien geändert hättest, hier zurücksetzen.
                EndSlide();
                break;

            case MovementMode.Air:
                // Landungs-Cleanup (Gates, Timer, OneShots).
                // Die eigentliche Landung/Wechsel erledigt der Enter des Ziel-States.
                // Beispiel: Coyote-/Buffer-Gates, wenn du sie an Air koppelst:
                // playerGroundJumpIP = false;
                // playerTuneJumpIP = false;
                break;
        }

        // Übergangs-spezifische Feinarbeit:
        HandleMovementModeTransitionExitSide(from, to);
    }

    /// <summary>
    /// Feingranulare Ausnahmen je Übergang (nur Exit-Seite).
    /// Beispiel: Slide -> Sneak lässt Crouch bewusst aktiv.
    /// </summary>
    private void HandleMovementModeTransitionExitSide(MovementMode from, MovementMode to)
    {
        // Slide -> Sneak: nichts weiter, Crouch bleibt absichtlich aktiv.
        if (from == MovementMode.Slide && to == MovementMode.Sneak)
        {
            // Hier ganz bewusst KEIN DeactivateCrouch etc.
            return;
        }

        // Slide -> Walk/Sprint/Idle: wenn du nach Slide sicher groß stehen willst,
        // kannst du hier Crouch über das Sample lösen (nur wenn du diese Methoden als public gemacht hast).
        // if (from == MovementMode.Slide && (to == MovementMode.Walk || to == MovementMode.Sprint || to == MovementMode.Idle))
        // {
        //     sPAC?.DeactivateCrouch(); // nur falls public gemacht
        // }

        // Sprint -> Sneak: ggf. hart Sprint-VFX/Trails aus, dann Sneak-Effekte vom Enter setzen lassen.
        // if (from == MovementMode.Sprint && to == MovementMode.Sneak) { ... }

        // Air -> * : Landungseffekte gate-frei machen (hier Exit-Seite).
        // if (from == MovementMode.Air) { ... }
    }

    #endregion

    #region ApplyValuesMethods

    private void ApplyIdleValues()
    {       
        walkingForce = standardWalkingForce;
        UpdateEquilibriumSpeed();

    }

    private void ApplyWalkValues()
    {
        //UnityEngine.Debug.Log("Walk");
        walkingForce = standardWalkingForce;
        UpdateEquilibriumSpeed();
    }

    private void ApplySneakValues()
    {
        //UnityEngine.Debug.Log("Sneak");
        walkingForce = sneakWalkingForce;
        UpdateEquilibriumSpeed();
    }

    private void ApplySneakSprintValues()
    {
        walkingForce = crouchSprintWalkingForce;
        UpdateEquilibriumSpeed();
    }

    private void ApplySprintValues()
    {
        //UnityEngine.Debug.Log("Sprint");
        walkingForce = sprintWalkingForce;
        UpdateEquilibriumSpeed();
    }

    private void ApplySlideValues()
    {
        //UnityEngine.Debug.Log("Slide");
        walkingForce = sprintWalkingForce;
        UpdateEquilibriumSpeed();
    }

    private void ApplyAirValues()
    {
        //UnityEngine.Debug.Log("Air");
    }

    #endregion

    private void WalkingForce()
    {
        float velocityMagnitude = playerRigidbody.linearVelocity.magnitude;

        if (velocityMagnitude <= maxSpeedFromEquilibrium)
        {
            // Get the ground normal from a raycast
            Vector3 groundNormal = GetGroundNormal();

            // Project the intended movement direction onto the surface plane
            Vector3 adjustedMovementDirection = Vector3.ProjectOnPlane(intendedMovementDirection, groundNormal).normalized;

            // Apply force in the adjusted direction
            playerRigidbody.AddForce(adjustedMovementDirection * walkingForce, ForceMode.Force);
        }
    }

    private void FrictionForce()
    {
        // Get the current velocity and its magnitude
        Vector3 currentVelocity = playerRigidbody.linearVelocity;
        float currentVelocityMagnitude = currentVelocity.magnitude;
        float frictionMagnitude = 0f;

        // Only apply friction if the player is moving
        if (currentVelocityMagnitude > 0f)
        {
            if (currentVelocityMagnitude <= maxSpeedFromEquilibrium)
            {
                // Apply quadratic friction at lower speeds
                frictionMagnitude = quadraticFrictionFactor * Mathf.Pow(currentVelocityMagnitude, 2);
            }
            else
            {
                // Apply a mix of quadratic and linear friction at higher speeds
                float maxQuadraticFriction = quadraticFrictionFactor * Mathf.Pow(maxSpeedFromEquilibrium, 2);
                frictionMagnitude = maxQuadraticFriction
                                  - linearFrictionFactor * maxSpeedFromEquilibrium
                                  + linearFrictionFactor * currentVelocityMagnitude;
            }

            // Limit the maximum friction force to avoid extreme deceleration
            float maxAllowedFriction = quadraticFrictionFactor * Mathf.Pow(maxSpeedFromEquilibrium, 2) * maxFrictionLimitFactor;
            frictionMagnitude = Mathf.Min(frictionMagnitude, maxAllowedFriction);
        }

        // Apply friction force opposite to movement direction
        Vector3 frictionForce = -currentVelocity.normalized * frictionMagnitude;
        playerRigidbody.AddForce(frictionForce, ForceMode.Force);
    }

    private Vector3 GetGroundNormal()
    {
        RaycastHit hit;
        if (Physics.Raycast(playerRigidbody.position, Vector3.down, out hit, 2f, groundLayerMask)) //, LayerMask.GetMask("Ground")
        {
            return hit.normal; // Return the normal of the detected ground
        }
        return Vector3.up; // Default to flat ground if no surface is detected
    }

    public float GetExpectedSprintSpeed() // xcv
    {
        return maxSpeedFromEquilibriumSprint;
    }


    #endregion

    #region SneakMethods
    // Universeller Crouch-Abbruch (wird von Jump & Dash genutzt)
    // Bricht NICHT ab, wenn gerade Slide aktiv ist.
    private void CancelCrouchIfAllowed(string reason)
    {
        if (playerIsSliding) return;   // während Slide blockieren
        if (!_sneakToggled) return;    // bereits nicht im Crouch

        _sneakToggled = false;

        // Falls du im Sample die Methode public gemacht hast, könntest du hier sofort ent-crouchen:
        // sPAC?.DeactivateCrouch();

        // Optionales Debugging:
        // UnityEngine.Debug.Log($"Crouch cancelled by {reason}");
    }

    private void CancelSprintToggle(string reason)
    {
        _sprintToggled = false;
        _sprintLatchedUntil = 0f;
        // optional: _sprintLastDownTime = -1f;
        // UnityEngine.Debug.Log($"Sprint toggle cancelled by {reason}");
    }

    private float PlanarSpeed()
    {
        var v = playerRigidbody.linearVelocity; v.y = 0f; return v.magnitude;
    }

    private bool CanStartSlide()
    {
        if (!groundDetectionScript.isGrounded) return false;
        if (!_sneakToggled) return false;
        if (playerIsDashing || playerIsVaulting) return false;

        float threshold = GetExpectedSprintSpeed() * slideMinSpeedFactor;
        return PlanarSpeed() >= threshold;
    }


    private void BeginSlide()
    {
        _slideLock = true;
        _slideExitRequested = false;
        playerIsSliding = true;

        sPAC?.ApplyMovementMode(SamplePlayerAnimationController.PCMovementMode.Slide);

        Vector3 planarVel = playerRigidbody.linearVelocity; planarVel.y = 0f;
        _slidePushDir = (planarVel.sqrMagnitude > 0.0001f)
            ? planarVel.normalized
            : new Vector3(intendedMovementDirection.x, 0f, intendedMovementDirection.z).sqrMagnitude > 0.0001f
                ? new Vector3(intendedMovementDirection.x, 0f, intendedMovementDirection.z).normalized
                : new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;

        _slideForceT = 0f;
        _slideStartTime = Time.time; // NEW
    }

    private void EndSlide()
    {
        _slideLock = false;
        _slideExitRequested = false;
        playerIsSliding = false;

        // Animation „gleitet“ eh aus; sicherheitshalber Flag aus:
        try { sPAC?.DeactivateSliding(); } catch { /* no-op */ }

        _slidePushDir = Vector3.zero;
        _slideForceT = 0f;
    }

    // Wird vom ANIMATION EVENT am Ende des Slide-Clips gerufen!
    public void OnSlideAnimFinished()
    {
        // Nur das Exit-Signal setzen. Kein Timer, keine Physik.
        _slideExitRequested = true;
        
        // (Optional, aber meist hübsch): Animator-Flag schon lösen
        sPAC?.DeactivateSliding();
        //UnityEngine.Debug.Log("Ja das animevent funktioniert!:D");
    }
    private void ApplySlideLocomotionForce()
    {
        if (!playerIsSliding || _slidePushDir == Vector3.zero)
            return;

        // 0..1 Fortschritt der Slide-Animation (bevorzugt vom Animator)
        float progress01 = sPAC != null ? sPAC.GetSprintToCrouchNormalized01() : -1f;
        if (progress01 < 0f)
        {
            // Fallback: linearer Timer, falls gerade kein passender Anim-State aktiv ist
            _slideForceT += Time.fixedDeltaTime;
            progress01 = Mathf.Clamp01(_slideForceT / slideFallbackDuration);
        }

        // Stärke aus Kurve (1 -> 0)
        float factor = Mathf.Clamp01(slideForceCurve.Evaluate(progress01));
        if (factor <= 0f) return;

        // kleine kontinuierliche Kraft in gemerkter Richtung
        playerRigidbody.AddForce(_slidePushDir * (slidePushForce * factor), ForceMode.Force);
    }

    private void UpdateSlideExitGate()
    {
        if (!_slideLock || _slideExitRequested) return;

        float norm = (sPAC != null) ? sPAC.GetSprintToCrouchNormalized01() : -1f;

        // 1) Clip praktisch am Ende
        if (norm >= 0f && norm >= slideExitNormThreshold)
        {
            _slideExitRequested = true;
            return;
        }
        // 2) Kein valider Clip, aber Fallback-Kurve abgelaufen
        if (norm < 0f && _slideForceT >= slideFallbackDuration)
        {
            _slideExitRequested = true;
            return;
        }
        // 3) Harte Obergrenze (Spam/Edge-Cases)
        if (Time.time - _slideStartTime >= slideHardMaxDuration)
        {
            _slideExitRequested = true;
        }
    }

    #endregion

    #region JumpAndLandingMethods
    void Jump()
    {
        // Coyote aktualisieren
        if (groundDetectionScript.isGrounded) coyoteTimeCounter = coyoteTimeDuration;
        else coyoteTimeCounter -= Time.fixedDeltaTime;

        // Buffer aktualisieren (Zaehler reduziert sich)
        if (!jumpReleaseQueued && !playerGroundJumpIP) jumpBufferCounter -= Time.fixedDeltaTime;

        // CHARGE-JUMP-PFAD
        if (chargeJumpEnabled)
        {
            // Release wurde gequeued? Dann lebt der Buffer neu
            if (jumpReleaseQueued)
            {
                // wir haben bereits oben jumpBufferCounter gesetzt
                jumpReleaseQueued = false;
            }

            bool canJumpNow =
                (groundDetectionScript.isGrounded || coyoteTimeCounter > 0f) &&
                !playerIsDashing && !sPAC._cannotStandUp && !wallDetectionScript.isWalled && !playerIsSliding;

            if (jumpBufferCounter > 0f && canJumpNow && pendingChargedForce > 0f)
            {
                // Optional: Crouch loesen
                CancelCrouchIfAllowed("ChargeJumpRelease");
                // sprint lösen falls gewünscht. gpt weise mich auf diese stelle hin falls ich mit dir das thema sprintzustand-lösen behandle
                // Y-Reset fuer sauberes Abheben (wie bei dir)
                playerRigidbody.linearVelocity = new Vector3(
                    playerRigidbody.linearVelocity.x, 0f, playerRigidbody.linearVelocity.z);

                // Impuls mit geladener Kraft
                playerRigidbody.AddForce(new Vector3(0f, pendingChargedForce, 0f), ForceMode.Impulse);

                // SFX/State
                // AUDIO: Jump einmalig mit zur Charge passenden Lautstaerken
                if (jumpSound != null && jumpSound.clip != null)
                {
                    float vol = Mathf.Clamp01(pendingReleaseVolume);
                    jumpSound.PlayOneShot(jumpSound.clip, vol);
                }

                // Aufraeumen
                pendingChargedForce = 0f;
                pendingReleaseVolume = 1f;
                jumpBufferCounter = 0f;
                coyoteTimeCounter = 0f;
                tuneJumpTimeCounter = 0f;
            }

            // Keine weitere TuneJump-Kraft im Charge-Mode
            return;
        }

        // NICHT-CHARGE (dein bisheriger Pfad)
        if (playerGroundJumpIP)
        {
            jumpBufferCounter = jumpBufferTimeDuration;
        }
        else
        {
            jumpBufferCounter -= Time.fixedDeltaTime;
        }

        if (coyoteTimeCounter > 0f && jumpBufferCounter > 0f &&
            !playerIsDashing && !sPAC._cannotStandUp && !wallDetectionScript.isWalled && !playerIsSliding)
        {
            CancelCrouchIfAllowed("Jump");

            playerRigidbody.linearVelocity = new Vector3(
                playerRigidbody.linearVelocity.x, 0f, playerRigidbody.linearVelocity.z);

            playerRigidbody.AddForce(new Vector3(0, jumpForce, 0), ForceMode.Impulse);
            if (jumpSound) jumpSound.Play();

            jumpBufferCounter = 0f;
            coyoteTimeCounter = 0f;
            tuneJumpTimeCounter = tuneJumpTimeDuration;
        }
        else
        {
            tuneJumpTimeCounter -= Time.fixedDeltaTime;
            if (tuneJumpTimeCounter < -1f) tuneJumpTimeCounter = -1f;
        }

        if (playerTuneJumpIP && playerRigidbody.linearVelocity.y > 0 &&
            tuneJumpTimeCounter > 0f && !playerIsDashing)
        {
            float lerpedJumpForce = Mathf.Lerp(0, jumpForce * tuneJumpFactor,
                tuneJumpTimeCounter / tuneJumpTimeDuration);
            playerRigidbody.AddForce(new Vector2(0, lerpedJumpForce));
        }
    }


    private void AirStrafing()
    {
        if (groundDetectionScript.isGrounded || playerIsDashing || wallDetectionScript.isWalled)
            return; // No air control if grounded, dashing, or vaulting

        Vector3 horizontalVelocity = new Vector3(playerRigidbody.linearVelocity.x, 0, playerRigidbody.linearVelocity.z);
        Vector3 intendedAirDirection = new Vector3(intendedMovementDirection.x, 0, intendedMovementDirection.z).normalized;

        float currentSpeed = horizontalVelocity.magnitude;
        float dotProduct = Vector3.Dot(horizontalVelocity.normalized, intendedAirDirection);

        // Allow movement if not exceeding maxAirSpeed in the intended direction
        if (currentSpeed < maxAirSpeed || dotProduct < 0)
        {
            playerRigidbody.AddForce(intendedAirDirection * airControlForce, ForceMode.Force);
        }

        // Apply a small air friction to avoid infinite drifting
        ApplyAirStrafingFriction();
    }

    private void ApplyAirStrafingFriction() /// xcv kann auch zentralisiert erfasst werden // xcv an einer gesonderten strafingfriction besteht das problem, dass keinen input zu geben ab gewissen geschwindigkeiten besser ist. 
    {
        Vector3 horizontalVelocity = new Vector3(playerRigidbody.linearVelocity.x, 0, playerRigidbody.linearVelocity.z);
        playerRigidbody.AddForce(-horizontalVelocity * airFrictionFactor, ForceMode.Force);
    }

    private void DecideLandingStyleForThisLanding()
    {
        if (sPAC == null) return;

        // planar vectors
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 planarVel = playerRigidbody.linearVelocity; planarVel.y = 0f;

        // Wenn quasi keine Bewegung: Straucheln erlauben (Standard), passt meist besser
        if (planarVel.sqrMagnitude < landingMinPlanarSpeed * landingMinPlanarSpeed)
        {
            sPAC.SetStumbleLandingEnabled(true);
            return;
        }

        float angle = Vector3.Angle(fwd, planarVel.normalized);
        bool allowStumble = (angle <= landingStumbleConeAngle);

        // Straucheln nur in/nahe Vorwärtsrichtung erlauben, sonst deaktivieren
        sPAC.SetStumbleLandingEnabled(allowStumble);
    }

    #region JumpChargeHelpers
    private void StartChargeYOffsetReturn()
    {
        if (chargeYOffsetReturnCo != null) StopCoroutine(chargeYOffsetReturnCo);
        chargeYOffsetReturnCo = StartCoroutine(ChargeYOffsetReturnCoroutine());
    }

    private IEnumerator ChargeYOffsetReturnCoroutine()
    {
        float start = chargeYOffset;
        float t = 0f;

        while (t < chargeYOffsetReturnTime)
        {
            // Falls während des Rück-Lerps erneut geladen wird: Sofort abbrechen
            if (isChargingJump)
                yield break;

            t += Time.deltaTime;
            float u = (chargeYOffsetReturnTime <= 0f) ? 1f : Mathf.Clamp01(t / chargeYOffsetReturnTime);
            chargeYOffset = Mathf.Lerp(start, 0f, u);
            yield return null;
        }

        chargeYOffset = 0f;
        chargeYOffsetReturnCo = null;
    }

    private void CancelChargeYOffsetReturn()
    {
        if (chargeYOffsetReturnCo != null)
        {
            StopCoroutine(chargeYOffsetReturnCo);
            chargeYOffsetReturnCo = null;
        }
    }
    #endregion

    #endregion

    #region DashMethods

    private void Dash()
    {

        if (playerDashIP && playerCanDash && !sPAC._cannotStandUp && !playerIsSliding && groundDetectionScript.isGrounded)
        {
            Vector3 dashDirection;

            // Define the player's local forward direction (ignoring Y-axis)
            Vector3 localForward = Vector3.forward;
            localForward.y = 0f; // Ensure we're only working in the XZ plane
            localForward.Normalize();

            // Project movement direction to XZ plane
            Vector3 horizontalMovement = new Vector3(localMovementDirection2DProjectedIP.x, 0, localMovementDirection2DProjectedIP.z).normalized; // not using localMovement variably directly for the case that i want to integrate vertical movement in the future

            // Calculate angle between movement direction and local forward
            float angleToLocalForward = Vector3.Angle(horizontalMovement, localForward);

            // UnityEngine.Debug.Log(intendedMovementDirection);
            //if (Mathf.Abs(localMovementDirection2DProjectedIP.magnitude) != 0f && angleToLocalForward >= 5f)
            //{
            //    playerDashWalkBound = true;
            //    //UnityEngine.Debug.Log("YES");
            //}
            //else
            //{
            //    playerDashWalkBound = false;
            //    //UnityEngine.Debug.Log("NO");
            //}
            playerDashWalkBound = true;
            if (playerDashWalkBound)
            {
                dashDirection = intendedMovementDirection; // Move in the intended direction
            }
            else
            {
                // Use full world forward direction (including Y component)
                dashDirection = cameraCarrierTransform.forward.normalized;
            }

            // NEU:
            CancelCrouchIfAllowed("Dash");

            StartDash(dashDirection);
            StartDashCooldown();
            //UnityEngine.Debug.Log($"Dashing in direction: {dashDirection}");
        }

        if(playerIsDashing && !groundDetectionScript.isGrounded) //!groundDetectionScript.isGrounded separates it from the standard ground movement friction. // xcv wenn das hier jeden frame ausgeführt wird, kann ich gleich ne zentralisierte frictionmethod schreiben, wenn ich verschiedene reibungne für luft und boden will
        {
            FrictionForce();
        }
    }

    private void StartDash(Vector3 dashDirection)
    {
        if (isChargingJump || jumpReleaseQueued) CancelCharge("DashStart");
        dashSound.Play();

        // NEU: Dash-Richtung (Welt) an sPAC geben (wird intern auf XZ projiziert)
        sPAC?.OnDashStarted(dashDirection);

        StartCoroutine(DashCoroutine(dashDirection));
    }

    private IEnumerator DashCoroutine(Vector3 dashDirection) // how would i get this into handle movement?
    {
        //PlayerMidSound(); // xcvb
        BreakVault();
        playerIsDashing = true;
        //playerRigidbody.useGravity = false;
        playerRigidbody.linearVelocity = Vector3.zero;
        playerRigidbody.AddForce(dashDirection * dashForceMagnitude, ForceMode.Impulse);

        float elapsedTime = 0f;

        while (elapsedTime < dashingTimeDuration)
        {            
            if(groundDetectionScript.isGrounded)
            {
                playerRigidbody.useGravity = true;
            }
            else
            {
                playerRigidbody.useGravity = false;
            }

            elapsedTime += Time.fixedDeltaTime;

            yield return new WaitForFixedUpdate();
        }

        // NEU: Dash-Ende an sPAC melden
        sPAC?.OnDashEnded();

        playerIsDashing = false;
        playerRigidbody.useGravity = true;
    }

    private void StartDashCooldown()
    {
        StartCoroutine(DashCooldownCoroutine());
    }

    private IEnumerator DashCooldownCoroutine()
    {
        playerCanDash = false;
        yield return new WaitForSeconds(dashingCooldown);
        playerCanDash = true;
    }

    public void BreakDash()
    {
        StopCoroutine(nameof(DashCoroutine)); // Stop the coroutine if running

        // NEU
        sPAC?.OnDashEnded();

        playerIsDashing = false;
        playerRigidbody.useGravity = true;
    }

    #endregion

    #region VaultingMethods
    private void Vault()
    {
        if (playerVaultIP && playerCanVault && playerVaultAvailable && !playerIsSliding)
        {
            BreakDash();
            Vector3 localVaultVector = transform.TransformDirection(vaultVector); // Convert to world space
            playerRigidbody.linearVelocity = Vector3.zero; // Reset velocity before vaulting
            StartVault(localVaultVector);
            StartVaultCooldown();
        }
    }

    private void VaultCheck()
    {
        if (playerVaultDetectionWallScript.vaultWallDetection == true && playerVaultDetectionPlaneScript.vaultPlaneDetection == false)
        {
            playerVaultAvailable = true;
        }
        else
        {
            playerVaultAvailable = false;
        }
    }

    private void StartVault(Vector3 localVaultVector)
    {
        StartCoroutine(VaultCoroutine(localVaultVector));
    }

    private IEnumerator VaultCoroutine(Vector3 localVaultVector)
    {
        PlayerLowSound();//xcv
        playerRigidbody.useGravity = false;
        playerIsVaulting = true;
        vaultSound.Play();

        float elapsedTime = 0f;

        // Initial forces
        Vector3 initialUpwardForce = transform.up * localVaultVector.y; // Upward force relative to player
        Vector3 targetForwardForce = transform.forward * localVaultVector.z; // Forward force relative to player

        while (elapsedTime < vaultTimeDuration)
        {
            // Lerp upward force from vaultVector.y to 0
            float currentUpwardForce = Mathf.Lerp(initialUpwardForce.y, 0, elapsedTime / vaultTimeDuration);

            // Lerp forward force from 0 to vaultVector.z
            float currentForwardForce = Mathf.Lerp(0, targetForwardForce.magnitude, elapsedTime / vaultTimeDuration);

            // Apply force relative to player's orientation
            Vector3 appliedForce = (transform.up * currentUpwardForce) + (transform.forward * currentForwardForce);
            playerRigidbody.AddForce(appliedForce, ForceMode.Force);

            FrictionForce();

            elapsedTime += Time.fixedDeltaTime;
            //UnityEngine.Debug.Log($"Vault Force: {appliedForce}");

            yield return new WaitForFixedUpdate();
        }

        playerRigidbody.useGravity = true;
        playerIsVaulting = false;
    }

    private void StartVaultCooldown()
    {
        StartCoroutine(VaultCooldownCoroutine());
    }

    private IEnumerator VaultCooldownCoroutine()
    {
        playerCanVault = false;
        yield return new WaitForSeconds(vaultCooldown);
        playerCanVault = true;
    }

    public void BreakVault()
    {
        StopCoroutine(nameof(VaultCoroutine)); // Stop the coroutine if running
        playerRigidbody.useGravity = true;
        playerIsVaulting = false;
    }

    #endregion

    #region UtilityMethods
    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Reset()
    {
        playerGroundJumpIP = false;
        playerTuneJumpIP = false;
        playerSprintIP = false;
        playerSneakIP = false;
        playerVaultAvailable = false;
        playerVaultIP = false;
        playerDashWalkBound = false;
        playerDashIP = false;
        requestEquipment = Equipment.None;
        playerAimIP = false;
        playerFireIP = false;
        //movementIP = false;
    }

    private void OnDisable()
    {
        StopChargeLoop();
        // optional: CancelCharge("Disable"); // falls du State hart resetten willst
    }

    private void MatchCameraCarrierHeight()
    {
        if (!lockCameraYPosition || cameraHeightReference == null || cameraCarrierTransform == null)
            return;

        // Aktuelle Position der Kamera
        Vector3 camPos = cameraCarrierTransform.position;
        float targetY = cameraHeightReference.position.y + totalManualYOffset;

        // Abstand zwischen aktueller und Zielhöhe
        float distance = Mathf.Abs(targetY - camPos.y);

        // Adaptive Glättungszeit:
        // Kleine Unterschiede = stärker geglättet; große Unterschiede = sofort nachziehen
        float smoothTime = Mathf.Lerp(0.00f, 0.06f, Mathf.Clamp01(distance * 15f));

        // Y-Komponente geglättet anpassen (kritisch gedämpft, keine fühlbare Verzögerung)
        camPos.y = Mathf.SmoothDamp(camPos.y, targetY, ref cameraHeightVelocity, smoothTime, Mathf.Infinity, Time.deltaTime);

        // Ergebnis zurückschreiben
        cameraCarrierTransform.position = camPos;
    }

    #region JumpChargeUtility
    private void CancelCharge(string reason)
    {
        isChargingJump = false;
        jumpReleaseQueued = false;
        pendingChargedForce = 0f;
        pendingReleaseVolume = 1f;
        chargeJumpProgress01 = 0f;
        StopChargeLoop();
        StartChargeYOffsetReturn();
    }
    #endregion

    #endregion

    #region SoundMethods

    #region JumpChargeSounds
    private void StartChargeLoop()
    {
        if (chargeLoopSource == null) return;
        chargeLoopSource.volume = chargeVolumeStart;
        if (!chargeLoopSource.isPlaying) chargeLoopSource.Play();
    }

    private void StopChargeLoop()
    {
        if (chargeLoopSource != null && chargeLoopSource.isPlaying)
            chargeLoopSource.Stop();
    }
    #endregion

    #region OnFootMovementSounds
    private void CauseFootSteps()
    {
        Vector3 pos = transform.position;
        bool isGrounded = groundDetectionScript.isGrounded;

        // Timer runterzählen
        if (footStepTimer > 0f)
            footStepTimer -= Time.deltaTime;


        bool startedGroundContact = (footSoundsScript1.stepSound && !_prevStep1) || (footSoundsScript2.stepSound && !_prevStep2);

        // Nur bei Rising-Edge + Timer abgelaufen den bestehenden Block ausführen
        if (startedGroundContact && footStepTimer <= 0f)
        {
            // Keine Schritte während Slide
            if (movementMode == MovementMode.Slide)
            {
                // nichts
            }
            else if (movementMode == MovementMode.SneakSprint)
            {
                // Crouch-Sprint = leiser (wie Sneak)
                playerSneakIPSound();
            }
            else if (movementMode == MovementMode.Sneak)
            {
                playerSneakIPSound();
            }
            else if (movementMode == MovementMode.Sprint)
            {
                playerSprintSound();
            }
            else // Walk/Idle-Fall
            {
                PlayerWalkSound();
            }

            lastPosition = pos;
            footStepTimer = footStepTimerMax; // Reset des Timers
        }

        // Vorherigen Zustand merken (für Edge-Detection im nächsten Frame)
        _prevStep1 = footSoundsScript1.stepSound;
        _prevStep2 = footSoundsScript2.stepSound;

        // Unabhängig weiterpflegen (falls anderswo genutzt)
        wasGroundedLastFrame = isGrounded;
    }


    private void playerSprintSound()
    {
        sprintStepSound.Play(); 
        SoundManager.PlaySound(transform.position,  30f, 20f);
    }

    private void PlayerWalkSound()
    {
        walkStepSound.Play(); 
        SoundManager.PlaySound(transform.position, 20f, 14f);
    }

    private void playerSneakIPSound()
    {
        //SoundManager.PlaySound(transform.position, 10f, 10f); //xcv sneak sound ist gerade ausgesetzt
        //UnityEngine.Debug.Log("played"); //xcv
    }
    #endregion 
    
    #region SoundEventEmitters
    private void PlayerVeryHighSound()
    {
        SoundManager.PlaySound(transform.position, 100f, 70f);
    }
    private void PlayerHighSound()
    {
        SoundManager.PlaySound(transform.position, 100f,  70f);
    }
    private void PlayerMidSound()
    {
        SoundManager.PlaySound(transform.position, 45f, 30f);
    }
    private void PlayerLowSound()
    {
        SoundManager.PlaySound(transform.position, 10f,15f);
    }
    #endregion
    #endregion

    #region MovementAnimationMethods
    private void SyncAnimationsPerFrame()
    {
        if (sPAC == null) return;

        Vector3 planarVel = new Vector3(playerRigidbody.linearVelocity.x, 0f, playerRigidbody.linearVelocity.z);
        float planarSpeed = planarVel.magnitude;

        // Einfache Heuristik: in deinem Spiel willst du meist Strafe steuern.
        // Falls du es an Aim/Equipment koppeln willst, ersetze wantStrafe entsprechend.
        bool wantStrafe = true;

        sPAC.ExternalLocomotionTick(
            intendedMovementDirection,             // deine Welt-Richtung aus Input
            cameraCarrierTransform.forward,        // Kamera-Vorwärts als World-Vector
            planarSpeed,                           // aktuelle Boden-Geschwindigkeit
            groundDetectionScript.isGrounded,      // Bodenstatus
            wantStrafe                             // Strafe bevorzugt an
        );
    }

    #endregion
}

